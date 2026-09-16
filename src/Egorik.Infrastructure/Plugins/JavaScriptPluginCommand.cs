using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Plugins;
using Jint;
using Jint.Runtime;

namespace Egorik.Infrastructure.Plugins;

public sealed class JavaScriptPluginCommand : IAssistantCommand
{
    private const string PluginArgument = "__plugin";
    private const string PayloadArgument = "__payload";
    private const int MaximumPayloadLength = 8 * 1024;
    private readonly IPluginCatalog _catalog;
    private readonly IAssistantSettingsService _settings;
    private readonly IReadOnlyDictionary<string, IPluginCapabilityHandler> _handlers;

    public JavaScriptPluginCommand(
        IPluginCatalog catalog,
        IAssistantSettingsService settings,
        IEnumerable<IPluginCapabilityHandler> handlers)
    {
        _catalog = catalog;
        _settings = settings;
        _handlers = handlers.ToDictionary(handler => handler.Capability, StringComparer.Ordinal);
    }

    public CommandDescriptor Descriptor { get; } = new(
        "plugin.javascript",
        "Команда JavaScript-плагина",
        "Выполняет изолированный код плагина через выданные capability.",
        CommandRisk.Medium);

    public ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var plugin in EnabledPlugins())
        {
            try
            {
                var result = Invoke(plugin, "match", cancellationToken, normalizedText);
                if (result is null || !result.RootElement.TryGetProperty("matched", out var matched) ||
                    matched.ValueKind != JsonValueKind.True)
                {
                    result?.Dispose();
                    continue;
                }

                using (result)
                {
                    var confidence = result.RootElement.TryGetProperty("confidence", out var score) &&
                                     score.TryGetDouble(out var value)
                        ? Math.Clamp(value, 0.5, 0.999)
                        : 0.95;
                    var payload = result.RootElement.TryGetProperty("payload", out var payloadElement)
                        ? payloadElement.GetRawText()
                        : "{}";
                    if (payload.Length > MaximumPayloadLength)
                    {
                        continue;
                    }

                    return ValueTask.FromResult<CommandMatch?>(CommandMatch.Create(
                        confidence,
                        (PluginArgument, plugin.Manifest.Id),
                        ("__fingerprint", plugin.Fingerprint),
                        (PayloadArgument, payload)));
                }
            }
            catch (Exception exception) when (IsScriptFailure(exception))
            {
                // Ошибка одного плагина не должна ломать остальные команды.
            }
        }

        return ValueTask.FromResult<CommandMatch?>(null);
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default)
    {
        var pluginId = match.Argument(PluginArgument);
        var fingerprint = match.Argument("__fingerprint");
        var payload = match.Argument(PayloadArgument);
        var plugin = EnabledPlugins().FirstOrDefault(candidate =>
            string.Equals(candidate.Manifest.Id, pluginId, StringComparison.Ordinal));
        if (plugin is null ||
            !string.Equals(plugin.Fingerprint, fingerprint, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(payload))
        {
            return CommandResult.Failure("JavaScript-плагин выключен или изменён.", Descriptor.Id);
        }

        try
        {
            using var result = Invoke(
                plugin,
                "execute",
                cancellationToken,
                payload,
                context.NormalizedText);
            if (result is null ||
                !result.RootElement.TryGetProperty("capability", out var capabilityElement) ||
                !result.RootElement.TryGetProperty("value", out var valueElement))
            {
                return CommandResult.Failure(
                    "Плагин не вернул capability и значение.",
                    Descriptor.Id);
            }

            var capability = capabilityElement.GetString();
            var value = valueElement.GetString();
            var entityId = ReadString(result.RootElement, "entityId") ?? "value";
            var entityName = ReadString(result.RootElement, "entityName") ?? value;
            if (string.IsNullOrWhiteSpace(capability) || string.IsNullOrWhiteSpace(value) ||
                value.Length > 80 || entityId.Length > 80 || (entityName?.Length ?? 0) > 80 ||
                !plugin.Manifest.Permissions.Contains(capability, StringComparer.Ordinal) ||
                !_handlers.TryGetValue(capability, out var handler) ||
                handler.Risk > CommandRisk.Medium)
            {
                return CommandResult.Failure(
                    "Запрошенное действие не разрешено этому плагину.",
                    Descriptor.Id);
            }

            return await handler.ExecuteAsync(
                new PluginCapabilityRequest(
                    plugin.Manifest.Id,
                    "javascript",
                    entityId,
                    entityName ?? value,
                    value),
                cancellationToken);
        }
        catch (Exception exception) when (IsScriptFailure(exception))
        {
            return CommandResult.Failure(
                $"Ошибка JavaScript-плагина: {exception.Message}",
                Descriptor.Id);
        }
    }

    private IEnumerable<LoadedAssistantPlugin> EnabledPlugins() =>
        _catalog.Plugins.Where(plugin =>
            plugin.IsScriptPlugin &&
            _settings.Current.EnabledPlugins.Contains(plugin.Manifest.Id) &&
            _settings.Current.TrustedPluginFingerprints.TryGetValue(
                plugin.Manifest.Id,
                out var fingerprint) &&
            string.Equals(fingerprint, plugin.Fingerprint, StringComparison.Ordinal));

    private static JsonDocument? Invoke(
        LoadedAssistantPlugin plugin,
        string function,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var packageFiles = ReadAndVerifyPackage(plugin);
        var engine = new Engine(options => options
            .Strict()
            .LimitMemory(8_000_000)
            .TimeoutInterval(TimeSpan.FromMilliseconds(500))
            .MaxStatements(20_000)
            .LimitRecursion(64)
            .CancellationToken(cancellationToken));

        foreach (var pair in plugin.DataFilePaths ?? new Dictionary<string, string>())
        {
            var name = Path.GetFileNameWithoutExtension(pair.Key);
            engine.SetValue($"data_{name}", DecodeText(packageFiles[pair.Value]));
        }

        engine.Execute(DecodeText(packageFiles[plugin.EntryPointPath!]));
        var value = engine.Invoke(function, arguments.Cast<object>().ToArray());
        if (value.IsNull() || value.IsUndefined())
        {
            return null;
        }

        engine.SetValue("__egorik_result", value);
        var json = engine.Evaluate("JSON.stringify(__egorik_result)").AsString();
        if (json.Length is <= 0 or > MaximumPayloadLength)
        {
            throw new InvalidDataException("Плагин вернул слишком большой результат.");
        }

        return JsonDocument.Parse(json);
    }

    private static IReadOnlyDictionary<string, byte[]> ReadAndVerifyPackage(
        LoadedAssistantPlugin plugin)
    {
        var manifestPath = Path.Combine(plugin.Directory, "plugin.json");
        var contentPaths = new[] { plugin.EntryPointPath! }
            .Concat(plugin.DataFilePaths?.Values ?? [])
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var paths = new[] { manifestPath }.Concat(contentPaths);
        var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in paths)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.LinkTarget is not null)
            {
                throw new InvalidDataException("Файл плагина удалён или заменён ссылкой.");
            }

            var root = Path.GetFullPath(plugin.Directory) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(file.FullName);
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Файл плагина вышел за каталог пакета.");
            }

            var bytes = File.ReadAllBytes(fullPath);
            contents[fullPath] = bytes;
            hash.AppendData(bytes);
        }

        var fingerprint = Convert.ToHexString(hash.GetHashAndReset());
        if (!string.Equals(fingerprint, plugin.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Файлы плагина изменились. Перезапустите Egorik и подтвердите плагин снова.");
        }

        return contents;
    }

    private static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsScriptFailure(Exception exception) =>
        exception is JavaScriptException or StatementsCountOverflowException or
            MemoryLimitExceededException or TimeoutException or RecursionDepthOverflowException or
            IOException or UnauthorizedAccessException or JsonException or InvalidDataException;
}

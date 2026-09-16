using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Egorik.Core.Commands;
using Egorik.Core.Plugins;
using Egorik.Infrastructure.Persistence;

namespace Egorik.Infrastructure.Plugins;

public sealed partial class JsonPluginCatalog : IPluginCatalog
{
    public const string SupportedApiVersion = "1";
    private const int MaximumManifestBytes = 128 * 1024;
    private const int MaximumEntitiesBytes = 512 * 1024;
    private static readonly IReadOnlyDictionary<string, string> CapabilityOwners =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dota2.hero_pick"] = "egorik.dota2.hero-picker"
        };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public JsonPluginCatalog(ApplicationDataPaths paths)
        : this(
            [paths.PluginsDirectory, Path.Combine(AppContext.BaseDirectory, "plugins")],
            paths.PluginsDirectory)
    {
    }

    public JsonPluginCatalog(
        IReadOnlyList<string> roots,
        string userPluginsDirectory)
    {
        UserPluginsDirectory = userPluginsDirectory;
        var plugins = new List<LoadedAssistantPlugin>();
        var issues = new List<PluginLoadIssue>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            LoadRoot(root, ids, plugins, issues);
        }

        Plugins = plugins
            .OrderBy(plugin => plugin.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Issues = issues.ToArray();
    }

    public IReadOnlyList<LoadedAssistantPlugin> Plugins { get; }

    public IReadOnlyList<PluginLoadIssue> Issues { get; }

    public string UserPluginsDirectory { get; }

    private static void LoadRoot(
        string root,
        HashSet<string> ids,
        List<LoadedAssistantPlugin> plugins,
        List<PluginLoadIssue> issues)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(root)
                         .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var plugin = LoadPlugin(directory);
                    if (!ids.Add(plugin.Manifest.Id))
                    {
                        continue;
                    }

                    plugins.Add(plugin);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                        JsonException or InvalidDataException)
                {
                    issues.Add(new PluginLoadIssue(directory, exception.Message));
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(new PluginLoadIssue(root, exception.Message));
        }
    }

    private static LoadedAssistantPlugin LoadPlugin(string directory)
    {
        var directoryInfo = new DirectoryInfo(directory);
        if (directoryInfo.LinkTarget is not null)
        {
            throw new InvalidDataException("Символические ссылки для плагинов не поддерживаются.");
        }

        if (Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly).Any())
        {
            throw new InvalidDataException(
                "DLL-плагины запрещены. Используйте изолированный JavaScript Plugin API v1.");
        }

        var manifestPath = Path.Combine(directory, "plugin.json");
        var manifest = ReadJson<PluginManifest>(manifestPath, MaximumManifestBytes);
        ValidateManifest(manifest);
        if (string.Equals(manifest.Kind, "javascript", StringComparison.Ordinal))
        {
            var entryPointPath = ResolveChildFile(directory, manifest.EntryPoint, ".js");
            var dataFilePaths = (manifest.DataFiles ?? [])
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    fileName => fileName,
                    fileName => ResolveChildFile(directory, fileName, ".json"),
                    StringComparer.Ordinal);
            var fingerprint = ComputeFingerprint(
                manifestPath,
                [entryPointPath, .. dataFilePaths.Values]);
            return new LoadedAssistantPlugin(
                manifest,
                directory,
                [],
                fingerprint,
                entryPointPath,
                dataFilePaths);
        }

        var commands = new List<LoadedPluginCommand>();
        foreach (var command in manifest.Commands!)
        {
            ValidateCommand(manifest, command);
            var entitiesPath = ResolveChildJson(directory, command.EntitiesFile);
            var entities = ReadJson<PluginEntity[]>(entitiesPath, MaximumEntitiesBytes);
            ValidateEntities(command, entities);
            commands.Add(new LoadedPluginCommand(command, entities));
        }

        return new LoadedAssistantPlugin(
            manifest,
            directory,
            commands,
            ComputeFingerprint(manifestPath, commands.Select(command =>
                ResolveChildJson(directory, command.Manifest.EntitiesFile)).ToArray()));
    }

    private static T ReadJson<T>(string path, int maximumBytes)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new InvalidDataException($"Не найден файл «{file.Name}».");
        }

        if (file.LinkTarget is not null)
        {
            throw new InvalidDataException("Символические ссылки внутри плагина запрещены.");
        }

        if (file.Length <= 0 || file.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"Файл «{file.Name}» пуст или превышает допустимый размер.");
        }

        using var stream = file.OpenRead();
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
               ?? throw new InvalidDataException($"Файл «{file.Name}» пуст.");
    }

    private static string ResolveChildJson(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("EntitiesFile должен быть именем JSON-файла без пути.");
        }

        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(directory, fileName));
        if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Файл сущностей вышел за границы каталога плагина.");
        }

        return result;
    }

    private static void ValidateManifest(PluginManifest manifest)
    {
        if (!PluginId().IsMatch(manifest.Id ?? string.Empty))
        {
            throw new InvalidDataException("Некорректный ID плагина.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 80 ||
            string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 24)
        {
            throw new InvalidDataException("Некорректное название или версия плагина.");
        }

        if (!string.Equals(manifest.ApiVersion, SupportedApiVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Плагину требуется неподдерживаемый API «{manifest.ApiVersion}».");
        }

        if (manifest.Permissions is null ||
            manifest.Permissions.Count > 8 ||
            manifest.Permissions.Any(permission => string.IsNullOrWhiteSpace(permission)))
        {
            throw new InvalidDataException("Плагин запросил некорректное разрешение.");
        }

        if (string.Equals(manifest.Kind, "javascript", StringComparison.Ordinal))
        {
            if (manifest.Commands is { Count: > 0 } ||
                string.IsNullOrWhiteSpace(manifest.EntryPoint) ||
                manifest.DataFiles is { Count: > 16 } ||
                manifest.DataFiles is not null &&
                manifest.DataFiles.Distinct(StringComparer.Ordinal).Count() != manifest.DataFiles.Count)
            {
                throw new InvalidDataException(
                    "JavaScript-плагину нужен entryPoint; декларативные команды недопустимы.");
            }

            return;
        }

        if (!string.Equals(manifest.Kind, "declarative", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Поддерживаются только kind=declarative и kind=javascript.");
        }

        if (manifest.Permissions.Count == 0 ||
            manifest.Permissions.Any(permission =>
                !CapabilityOwners.TryGetValue(permission, out var owner) ||
                !string.Equals(owner, manifest.Id, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Декларативный плагин запросил неизвестное разрешение.");
        }

        if (manifest.Commands is null or { Count: 0 } || manifest.Commands.Count > 16)
        {
            throw new InvalidDataException("Плагин не содержит допустимых команд.");
        }

        if (manifest.Commands.Select(command => command.Id).Distinct(StringComparer.Ordinal).Count() !=
            manifest.Commands.Count)
        {
            throw new InvalidDataException("ID команд плагина должны быть уникальными.");
        }
    }

    private static string ResolveChildFile(
        string directory,
        string? fileName,
        string expectedExtension)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            !string.Equals(
                Path.GetExtension(fileName),
                expectedExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Файл плагина должен иметь расширение {expectedExtension} и не содержать путь.");
        }

        var path = Path.Combine(directory, fileName);
        if (expectedExtension == ".json" &&
            !SnakeCaseId().IsMatch(Path.GetFileNameWithoutExtension(fileName)))
        {
            throw new InvalidDataException("Имя файла данных должно быть в snake_case.");
        }

        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null ||
            file.Length <= 0 || file.Length > MaximumEntitiesBytes)
        {
            throw new InvalidDataException("Не найден безопасный файл JavaScript-плагина.");
        }

        return file.FullName;
    }

    private static string ComputeFingerprint(string manifestPath, params string[] files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFile(hash, manifestPath);
        foreach (var file in files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            AppendFile(hash, file);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendFile(IncrementalHash hash, string path)
    {
        hash.AppendData(File.ReadAllBytes(path));
    }

    private static void ValidateCommand(
        PluginManifest plugin,
        PluginCommandManifest command)
    {
        if (!SnakeCaseId().IsMatch(command.Id ?? string.Empty) ||
            string.IsNullOrWhiteSpace(command.Title) || command.Title.Length > 80 ||
            string.IsNullOrWhiteSpace(command.Description) || command.Description.Length > 300)
        {
            throw new InvalidDataException("Некорректный дескриптор команды плагина.");
        }

        if (!plugin.Permissions.Contains(command.Capability, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Команда «{command.Id}» использует capability без разрешения.");
        }

        if (command.Prefixes is null or { Count: 0 } || command.Prefixes.Count > 16 ||
            command.Prefixes.Any(prefix =>
            {
                var normalized = TextNormalizer.Normalize(prefix);
                return normalized.Length is < 2 or > 48 || normalized != prefix;
            }))
        {
            throw new InvalidDataException("Префиксы команды должны быть нормализованными фразами.");
        }

        if (!SnakeCaseId().IsMatch(command.EntityArgument ?? string.Empty))
        {
            throw new InvalidDataException("Некорректное имя аргумента сущности.");
        }
    }

    private static void ValidateEntities(
        PluginCommandManifest command,
        IReadOnlyList<PluginEntity> entities)
    {
        if (entities.Count is < 1 or > 256)
        {
            throw new InvalidDataException(
                $"Команда «{command.Id}» содержит недопустимое число сущностей.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            var entityId = entity.Id;
            if (entityId is null || !SnakeCaseId().IsMatch(entityId) || !ids.Add(entityId) ||
                string.IsNullOrWhiteSpace(entity.Name) || entity.Name.Length > 80 ||
                string.IsNullOrWhiteSpace(entity.Value) || entity.Value.Length > 80 ||
                entity.Value.Any(character =>
                    !(char.IsAsciiLetterOrDigit(character) || character is ' ' or '-' or '_' or '\'')) ||
                entity.Aliases is null or { Count: 0 } || entity.Aliases.Count > 32)
            {
                throw new InvalidDataException(
                    $"Некорректная сущность в команде «{command.Id}».");
            }

            foreach (var alias in entity.Aliases.Append(entity.Name))
            {
                var normalized = TextNormalizer.Normalize(alias);
                if (normalized.Length is < 2 or > 80 || !aliases.Add(normalized))
                {
                    throw new InvalidDataException(
                        $"Псевдоним «{alias}» пуст, слишком длинен или неоднозначен.");
                }
            }
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PluginId();

    [GeneratedRegex("^[a-z][a-z0-9_]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SnakeCaseId();
}

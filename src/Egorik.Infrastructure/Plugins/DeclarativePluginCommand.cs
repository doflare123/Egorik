using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Plugins;

namespace Egorik.Infrastructure.Plugins;

public sealed class DeclarativePluginCommand : IAssistantCommand
{
    private readonly IPluginCatalog _catalog;
    private readonly IAssistantSettingsService _settings;
    private readonly IReadOnlyDictionary<string, IPluginCapabilityHandler> _handlers;

    public DeclarativePluginCommand(
        IPluginCatalog catalog,
        IAssistantSettingsService settings,
        IEnumerable<IPluginCapabilityHandler> handlers)
    {
        _catalog = catalog;
        _settings = settings;
        _handlers = handlers.ToDictionary(
            handler => handler.Capability,
            StringComparer.Ordinal);
    }

    public CommandDescriptor Descriptor { get; } = new(
        "plugin.command",
        "Команда подключённого плагина",
        "Выполняет декларативную команду через зарегистрированную capability.",
        CommandRisk.Medium);

    public ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var plugin in _catalog.Plugins)
        {
            if (!_settings.Current.EnabledPlugins.Contains(plugin.Manifest.Id))
            {
                continue;
            }

            foreach (var command in plugin.Commands)
            {
                foreach (var prefix in command.Manifest.Prefixes
                             .OrderByDescending(value => value.Length))
                {
                    if (!normalizedText.StartsWith(prefix + " ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var spokenEntity = normalizedText[(prefix.Length + 1)..].Trim();
                    var entity = MatchEntity(command.Entities, spokenEntity);
                    if (entity is null)
                    {
                        continue;
                    }

                    return ValueTask.FromResult<CommandMatch?>(CommandMatch.Create(
                        0.995,
                        ("plugin", plugin.Manifest.Id),
                        ("command", command.Manifest.Id),
                        ("capability", command.Manifest.Capability),
                        ("entity", entity.Id)));
                }
            }
        }

        return ValueTask.FromResult<CommandMatch?>(null);
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default)
    {
        var pluginId = match.Argument("plugin");
        var commandId = match.Argument("command");
        var capability = match.Argument("capability");
        var entityId = match.Argument("entity");
        if (pluginId is null || commandId is null || capability is null || entityId is null ||
            !_settings.Current.EnabledPlugins.Contains(pluginId))
        {
            return CommandResult.Failure(
                "Плагин выключен или команда больше недоступна.",
                Descriptor.Id);
        }

        var plugin = _catalog.Plugins.FirstOrDefault(candidate =>
            string.Equals(candidate.Manifest.Id, pluginId, StringComparison.Ordinal));
        var command = plugin?.Commands.FirstOrDefault(candidate =>
            string.Equals(candidate.Manifest.Id, commandId, StringComparison.Ordinal));
        var entity = command?.Entities.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, entityId, StringComparison.Ordinal));
        if (plugin is null || command is null || entity is null ||
            !string.Equals(command.Manifest.Capability, capability, StringComparison.Ordinal) ||
            !plugin.Manifest.Permissions.Contains(capability, StringComparer.Ordinal))
        {
            return CommandResult.Failure(
                "Декларативная команда плагина не прошла повторную проверку.",
                Descriptor.Id);
        }

        if (!_handlers.TryGetValue(capability, out var handler) ||
            handler.Risk > CommandRisk.Medium)
        {
            return CommandResult.Failure(
                "Для capability плагина нет разрешённого локального обработчика.",
                Descriptor.Id);
        }

        return await handler.ExecuteAsync(
            new PluginCapabilityRequest(
                plugin.Manifest.Id,
                command.Manifest.Id,
                entity.Id,
                entity.Name,
                entity.Value),
            cancellationToken);
    }

    private static PluginEntity? MatchEntity(
        IReadOnlyList<PluginEntity> entities,
        string spokenEntity)
    {
        var normalized = TextNormalizer.Normalize(spokenEntity);
        var exact = entities.Where(entity =>
                entity.Aliases.Append(entity.Name).Any(alias =>
                    string.Equals(
                        TextNormalizer.Normalize(alias),
                        normalized,
                        StringComparison.Ordinal)))
            .ToArray();
        return exact.Length == 1 ? exact[0] : null;
    }
}

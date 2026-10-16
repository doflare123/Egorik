using Egorik.Core.Actions;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Plugins;

namespace Egorik.Infrastructure.Actions;

public sealed class DeclarativePluginActionProvider(
    IPluginCatalog catalog,
    IAssistantSettingsService settings,
    IEnumerable<IPluginCapabilityHandler> handlers) : IAssistantActionProvider
{
    private readonly IReadOnlyDictionary<string, IPluginCapabilityHandler> _handlers = handlers
        .Where(handler => handler.Risk == CommandRisk.Low)
        .ToDictionary(handler => handler.Capability, StringComparer.Ordinal);

    public ActionProviderManifest Manifest { get; } = new("plugins", "Декларативные плагины");

    public IReadOnlyList<AssistantActionDescriptor> Actions => Entries().Select(entry => new AssistantActionDescriptor(
        Id(entry.Plugin, entry.Command), $"{entry.Plugin.Manifest.Name}: {entry.Command.Manifest.Title}",
        entry.Command.Manifest.Description,
        [new ActionParameterDescriptor("entity", "Объект", ActionParameterKind.Choice, MaxLength: 160,
            AllowedValues: entry.Command.Entities.Select(entity => entity.Id).ToArray(),
            ValueTitles: entry.Command.Entities.ToDictionary(entity => entity.Id, entity => entity.Name, StringComparer.Ordinal))])).ToArray();

    public ValueTask<CommandResult> ExecuteAsync(ActionInvocation invocation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Entries().FirstOrDefault(entry => Id(entry.Plugin, entry.Command) == invocation.ActionId);
        if (entry.Plugin is null || invocation.Arguments.Count != 1 ||
            !invocation.Arguments.TryGetValue("entity", out var entityId))
        {
            return ValueTask.FromResult(CommandResult.Failure("Действие плагина больше недоступно."));
        }

        var entity = entry.Command.Entities.FirstOrDefault(entity => entity.Id == entityId);
        if (entity is null)
        {
            return ValueTask.FromResult(CommandResult.Failure("Объект отсутствует в разрешённом каталоге плагина."));
        }

        return _handlers[entry.Command.Manifest.Capability].ExecuteAsync(new(
            entry.Plugin.Manifest.Id, entry.Command.Manifest.Id, entity.Id, entity.Name, entity.Value), cancellationToken);
    }

    private IEnumerable<(LoadedAssistantPlugin Plugin, LoadedPluginCommand Command)> Entries() => catalog.Plugins
        .Where(plugin => !plugin.IsScriptPlugin && settings.Current.EnabledPlugins.Contains(plugin.Manifest.Id))
        .SelectMany(plugin => plugin.Commands
            .Where(command => plugin.Manifest.Permissions.Contains(command.Manifest.Capability, StringComparer.Ordinal) &&
                              _handlers.ContainsKey(command.Manifest.Capability))
            .Select(command => (plugin, command)));

    private static string Id(LoadedAssistantPlugin plugin, LoadedPluginCommand command) =>
        $"plugins.{plugin.Manifest.Id}.{command.Manifest.Id}";
}

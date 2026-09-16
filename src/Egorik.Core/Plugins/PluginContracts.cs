using Egorik.Core.Commands;

namespace Egorik.Core.Plugins;

public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string ApiVersion,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<PluginCommandManifest>? Commands = null,
    string Kind = "declarative",
    string? EntryPoint = null,
    IReadOnlyList<string>? DataFiles = null);

public sealed record PluginCommandManifest(
    string Id,
    string Title,
    string Description,
    string Capability,
    IReadOnlyList<string> Prefixes,
    string EntityArgument,
    string EntitiesFile);

public sealed record PluginEntity(
    string Id,
    string Name,
    string Value,
    IReadOnlyList<string> Aliases);

public sealed record LoadedPluginCommand(
    PluginCommandManifest Manifest,
    IReadOnlyList<PluginEntity> Entities);

public sealed record LoadedAssistantPlugin(
    PluginManifest Manifest,
    string Directory,
    IReadOnlyList<LoadedPluginCommand> Commands,
    string Fingerprint,
    string? EntryPointPath = null,
    IReadOnlyDictionary<string, string>? DataFilePaths = null)
{
    public bool IsScriptPlugin =>
        string.Equals(Manifest.Kind, "javascript", StringComparison.Ordinal);
}

public sealed record PluginLoadIssue(
    string Source,
    string Message);

public interface IPluginCatalog
{
    IReadOnlyList<LoadedAssistantPlugin> Plugins { get; }

    IReadOnlyList<PluginLoadIssue> Issues { get; }

    string UserPluginsDirectory { get; }
}

public sealed record PluginCapabilityRequest(
    string PluginId,
    string CommandId,
    string EntityId,
    string EntityName,
    string Value);

public interface IPluginCapabilityHandler
{
    string Capability { get; }

    CommandRisk Risk { get; }

    ValueTask<CommandResult> ExecuteAsync(
        PluginCapabilityRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPluginActivationService
{
    string PluginId { get; }

    ValueTask<CommandResult> ApplyAsync(
        bool enabled,
        CancellationToken cancellationToken = default);
}

[Obsolete("DLL-плагины не загружаются. Используйте изолированный JavaScript Plugin API v1.")]
public interface IAssistantPlugin
{
    PluginManifest Manifest { get; }

    IEnumerable<IAssistantCommand> CreateCommands();
}

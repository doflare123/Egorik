using Egorik.Core.Commands;

namespace Egorik.Core.Plugins;

public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string ApiVersion,
    IReadOnlyList<string> Permissions);

public interface IAssistantPlugin
{
    PluginManifest Manifest { get; }

    IEnumerable<IAssistantCommand> CreateCommands();
}

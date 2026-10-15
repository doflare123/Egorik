using Egorik.Core.Configuration;
using Egorik.Core.Plugins;

namespace Egorik.Core.Commands;

public sealed record CommandHelpEntry(string Title, string Description, string Examples, string Source);

public static class CommandHelpCatalog
{
    public static IReadOnlyList<CommandHelpEntry> Build(
        IReadOnlyList<CommandDescriptor> commands,
        IReadOnlyList<LoadedAssistantPlugin> plugins,
        AssistantSettings settings)
    {
        var entries = commands.Where(command => !command.Id.StartsWith("plugin.", StringComparison.Ordinal))
            .Select(command => new CommandHelpEntry(
                command.Title, command.Description, BuiltInExamples(command.Id, settings), "Встроенная команда"))
            .ToList();

        foreach (var plugin in plugins)
        {
            var enabled = settings.EnabledPlugins.Contains(plugin.Manifest.Id) &&
                          (!plugin.IsScriptPlugin ||
                           settings.TrustedPluginFingerprints.TryGetValue(plugin.Manifest.Id, out var fingerprint) &&
                           string.Equals(fingerprint, plugin.Fingerprint, StringComparison.Ordinal));
            var source = $"Плагин: {plugin.Manifest.Name} · {(enabled ? "включён" : "выключен")}";
            if (plugin.IsScriptPlugin)
            {
                entries.Add(new CommandHelpEntry(
                    plugin.Manifest.Name, plugin.Manifest.Description,
                    string.Join("; ", plugin.Manifest.Examples ?? []), source));
            }
            else
            {
                foreach (var command in plugin.Commands)
                {
                    var entity = command.Entities.FirstOrDefault();
                    entries.Add(new CommandHelpEntry(
                        command.Manifest.Title, command.Manifest.Description,
                        string.Join("; ", command.Manifest.Prefixes.Take(3)
                            .Select(prefix => $"{prefix} {entity?.Aliases.FirstOrDefault() ?? entity?.Name}".Trim())),
                        source));
                }
            }
        }

        return entries;
    }

    private static string BuiltInExamples(string id, AssistantSettings settings) => id switch
    {
        "assistant.help" => "что ты умеешь; покажи команды",
        "client.navigate" => "открой настройки; открой сценарии",
        "application.open" => string.Join("; ", settings.Applications.Keys.Take(4).Select(name => $"открой {name}")),
        "website.open" => string.Join("; ", settings.Sites.Keys.Take(4).Select(name => $"открой {name}")),
        "web.search" => "найди погоду в Калининграде",
        "calculator.evaluate" => "сколько будет 2 + 2",
        "system.datetime" => "который час; какая сегодня дата",
        "media.control" => "пауза; следующий трек",
        "application.action" => "включи мою волну; открой библиотеку Steam",
        _ => string.Empty
    };
}

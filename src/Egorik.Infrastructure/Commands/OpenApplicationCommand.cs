using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Commands;

public sealed class OpenApplicationCommand(
    IAssistantSettingsService settings,
    IApplicationLauncher launcher) : IAssistantCommand
{
    public CommandDescriptor Descriptor { get; } = new(
        "application.open",
        "Открыть приложение",
        "Открывает настроенное приложение по имени.");

    public ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default)
    {
        var target = CommandText.AfterPrefix(normalizedText, "открой", "запусти");
        if (target is null || target.Length == 0)
        {
            return ValueTask.FromResult<CommandMatch?>(null);
        }

        var application = KnownEntityMatcher.FindUniqueMatch(settings.Current.Applications.Keys, target);

        return ValueTask.FromResult(application is null
            ? null
            : CommandMatch.Create(0.88, ("application", application)));
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default)
    {
        var alias = match.Argument("application");
        if (alias is null || !settings.Current.Applications.TryGetValue(alias, out var target))
        {
            return CommandResult.Failure("Приложение не настроено.", Descriptor.Id);
        }

        var result = await launcher.LaunchAsync(target, cancellationToken);
        return result.IsSuccess
            ? CommandResult.Success($"Открываю {alias}.", Descriptor.Id)
            : CommandResult.Failure(result.Message, Descriptor.Id);
    }
}

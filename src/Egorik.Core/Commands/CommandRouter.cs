namespace Egorik.Core.Commands;

public sealed class CommandRouter(IEnumerable<IAssistantCommand> commands) : ICommandRouter
{
    private readonly IReadOnlyList<IAssistantCommand> _commands = commands.ToArray();

    public IReadOnlyList<CommandDescriptor> AvailableCommands =>
        _commands.Select(command => command.Descriptor).ToArray();

    public async ValueTask<CommandResult?> RouteAsync(
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await RouteExactAsync(context, false, cancellationToken);
        if (result is not null)
        {
            return result;
        }

        var conversationalText = CommandPhraseNormalizer.Normalize(context.NormalizedText);
        return conversationalText == context.NormalizedText
            ? null
            : await RouteExactAsync(context with { NormalizedText = conversationalText }, true, cancellationToken);
    }

    private async ValueTask<CommandResult?> RouteExactAsync(
        CommandContext context,
        bool lowRiskOnly,
        CancellationToken cancellationToken)
    {
        IAssistantCommand? bestCommand = null;
        CommandMatch? bestMatch = null;

        foreach (var command in _commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lowRiskOnly && command.Descriptor.Risk != CommandRisk.Low)
            {
                continue;
            }

            var match = await command.MatchAsync(context.NormalizedText, cancellationToken);
            if (match is null || match.Confidence <= (bestMatch?.Confidence ?? 0))
            {
                continue;
            }

            bestCommand = command;
            bestMatch = match;
        }

        if (bestCommand is null || bestMatch is null)
        {
            return null;
        }

        return await bestCommand.ExecuteAsync(context, bestMatch, cancellationToken);
    }
}

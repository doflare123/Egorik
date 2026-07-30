using Egorik.Core.Commands;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Commands;

public sealed class MediaCommand(IMediaControlService mediaControl) : IAssistantCommand
{
    private static readonly IReadOnlyDictionary<string, MediaAction> Actions =
        new Dictionary<string, MediaAction>(StringComparer.Ordinal)
        {
            ["пауза"] = MediaAction.PlayPause,
            ["поставь на паузу"] = MediaAction.PlayPause,
            ["продолжи"] = MediaAction.PlayPause,
            ["продолжить"] = MediaAction.PlayPause,
            ["следующий трек"] = MediaAction.Next,
            ["следующая песня"] = MediaAction.Next,
            ["предыдущий трек"] = MediaAction.Previous,
            ["предыдущая песня"] = MediaAction.Previous,
            ["останови музыку"] = MediaAction.Stop,
            ["звук погромче"] = MediaAction.VolumeUp,
            ["сделай громче"] = MediaAction.VolumeUp,
            ["звук потише"] = MediaAction.VolumeDown,
            ["сделай тише"] = MediaAction.VolumeDown,
            ["выключи звук"] = MediaAction.Mute,
            ["включи звук"] = MediaAction.Mute
        };

    public CommandDescriptor Descriptor { get; } = new(
        "media.control",
        "Управление медиа",
        "Пауза, продолжение, следующий или предыдущий трек, громкость.");

    public ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default)
    {
        if (!Actions.TryGetValue(normalizedText, out var action))
        {
            return ValueTask.FromResult<CommandMatch?>(null);
        }

        return ValueTask.FromResult<CommandMatch?>(
            CommandMatch.Create(0.97, ("action", action.ToString())));
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<MediaAction>(match.Argument("action"), out var action))
        {
            return CommandResult.Failure("Неизвестная медиа-команда.", Descriptor.Id);
        }

        var result = await mediaControl.ExecuteAsync(action, cancellationToken);
        return result.IsSuccess
            ? CommandResult.Success(result.Message, Descriptor.Id)
            : CommandResult.Failure(result.Message, Descriptor.Id);
    }
}

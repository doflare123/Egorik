using Egorik.Core.Applications;
using Egorik.Core.Commands;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Applications;

public sealed record InstalledSteamGame(string AppId, string Name);

public interface ISteamGameCatalog
{
    ValueTask<IReadOnlyList<InstalledSteamGame>> FindAsync(
        string query,
        CancellationToken cancellationToken = default);
}

public sealed class SteamApplicationIntegration(
    ISteamGameCatalog gameCatalog,
    IApplicationLauncher launcher,
    IWebBrowserService browser) : IApplicationIntegration
{
    private static readonly ApplicationActionDescriptor OpenAction = new(
        "open",
        "Открыть Steam",
        "Открывает главное окно установленного клиента Steam.",
        [],
        IsAvailableToAi: true);

    private static readonly ApplicationActionDescriptor LibraryAction = new(
        "open_library",
        "Открыть библиотеку Steam",
        "Открывает библиотеку установленного клиента Steam.",
        [],
        IsAvailableToAi: true);

    private static readonly ApplicationActionDescriptor SearchAction = new(
        "search_game",
        "Найти игру в Steam",
        "Открывает безопасно закодированный поиск игры в магазине Steam.",
        [
            new ApplicationActionParameterDescriptor(
                "game",
                "Название игры.",
                MaxLength: 160)
        ],
        IsAvailableToAi: true);

    private static readonly ApplicationActionDescriptor LaunchGameAction = new(
        "launch_game",
        "Запустить установленную игру",
        "Находит игру только в локальных библиотеках Steam и запускает её по числовому App ID.",
        [
            new ApplicationActionParameterDescriptor(
                "game",
                "Название установленной игры.",
                MaxLength: 160)
        ],
        IsAvailableToAi: true);

    public ApplicationIntegrationDescriptor Descriptor { get; } = new(
        "steam",
        "Steam",
        ["steam", "стим", "стиме"],
        [OpenAction, LibraryAction, SearchAction, LaunchGameAction]);

    public async ValueTask<CommandResult> ExecuteAsync(
        ApplicationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ApplicationId != Descriptor.Id)
        {
            return CommandResult.Failure("Некорректный ID Steam.", "application.action");
        }

        if (request.ActionId == OpenAction.Id)
        {
            return FromLaunchResult(
                await launcher.LaunchAsync("steam://open/main", cancellationToken),
                "Открываю Steam.");
        }

        if (request.ActionId == LibraryAction.Id)
        {
            return FromLaunchResult(
                await launcher.LaunchAsync("steam://open/games", cancellationToken),
                "Открываю библиотеку Steam.");
        }

        if (!request.Arguments.TryGetValue("game", out var game) ||
            string.IsNullOrWhiteSpace(game))
        {
            return CommandResult.Failure("Название игры не указано.", "application.action");
        }

        game = game.Trim();
        if (request.ActionId == SearchAction.Id)
        {
            var encoded = Uri.EscapeDataString(game);
            return FromLaunchResult(
                await browser.OpenUrlAsync(
                    new Uri($"https://store.steampowered.com/search/?term={encoded}"),
                    cancellationToken),
                $"Ищу «{game}» в Steam.");
        }

        if (request.ActionId != LaunchGameAction.Id)
        {
            return CommandResult.Failure("Неизвестное действие Steam.", "application.action");
        }

        var matches = await gameCatalog.FindAsync(game, cancellationToken);
        if (matches.Count == 0)
        {
            return CommandResult.Failure(
                $"Не нашёл установленную игру «{game}» в библиотеках Steam.",
                "application.action");
        }

        if (matches.Count > 1)
        {
            var names = string.Join(
                ", ",
                matches.Take(5).Select(match => $"«{match.Name}»"));
            return CommandResult.Failure(
                $"Нашёл несколько игр: {names}. Назови игру точнее.",
                "application.action");
        }

        var selected = matches[0];
        if (!selected.AppId.All(char.IsAsciiDigit))
        {
            return CommandResult.Failure(
                "Steam вернул некорректный ID игры.",
                "application.action");
        }

        return FromLaunchResult(
            await launcher.LaunchAsync(
                $"steam://rungameid/{selected.AppId}",
                cancellationToken),
            $"Запускаю «{selected.Name}» в Steam.");
    }

    private static CommandResult FromLaunchResult(LaunchResult result, string successMessage) =>
        result.IsSuccess
            ? CommandResult.Success(successMessage, "application.action")
            : CommandResult.Failure(result.Message, "application.action");
}

using Egorik.Core.Applications;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Applications;

public sealed class DefaultBrowserApplicationIntegration(
    IAssistantSettingsService settings,
    IWebBrowserService browser) : IApplicationIntegration
{
    private static readonly ApplicationActionDescriptor OpenAction = new(
        "open",
        "Открыть браузер",
        "Открывает стартовую страницу в браузере, назначенном в системе по умолчанию.",
        [],
        IsAvailableToAi: true);

    private static readonly ApplicationActionDescriptor SearchAction = new(
        "search",
        "Найти в интернете",
        "Открывает безопасно закодированный поисковый запрос в браузере по умолчанию.",
        [
            new ApplicationActionParameterDescriptor(
                "query",
                "Текст поискового запроса.",
                MaxLength: 300)
        ],
        IsAvailableToAi: true);

    public ApplicationIntegrationDescriptor Descriptor { get; } = new(
        "default_browser",
        "Браузер по умолчанию",
        ["браузер", "браузер по умолчанию"],
        [OpenAction, SearchAction]);

    public async ValueTask<CommandResult> ExecuteAsync(
        ApplicationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ApplicationId != Descriptor.Id)
        {
            return CommandResult.Failure("Некорректный ID браузера.", "application.action");
        }

        LaunchResult result;
        if (request.ActionId == OpenAction.Id)
        {
            result = await browser.OpenUrlAsync(
                GetStartPage(settings.Current.SearchProvider),
                cancellationToken);
        }
        else if (request.ActionId == SearchAction.Id &&
                 request.Arguments.TryGetValue("query", out var query) &&
                 !string.IsNullOrWhiteSpace(query))
        {
            result = await browser.SearchAsync(
                query.Trim(),
                settings.Current.SearchProvider,
                cancellationToken);
        }
        else
        {
            return CommandResult.Failure("Неизвестное действие браузера.", "application.action");
        }

        return result.IsSuccess
            ? CommandResult.Success(result.Message, "application.action")
            : CommandResult.Failure(result.Message, "application.action");
    }

    private static Uri GetStartPage(string provider) =>
        new(provider.ToLowerInvariant() switch
        {
            "google" => "https://www.google.com/",
            "duckduckgo" => "https://duckduckgo.com/",
            _ => "https://ya.ru/"
        });
}

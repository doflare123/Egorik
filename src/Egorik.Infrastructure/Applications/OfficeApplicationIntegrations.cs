using Egorik.Core.Applications;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Applications;

public sealed class MicrosoftWordApplicationIntegration(
    IApplicationLauncher launcher,
    IOfficeDocumentService documents)
    : IApplicationIntegration
{
    private static readonly ApplicationActionDescriptor OpenAction = new(
        "open",
        "Открыть Microsoft Word",
        "Открывает установленный Microsoft Word без доступа к документам.",
        [],
        IsAvailableToAi: true);

    private static readonly ApplicationActionDescriptor CreateNoteAction = new(
        "create_note",
        "Создать заметку Word",
        "Создаёт новый документ в каталоге Egorik и открывает его, не изменяя существующие файлы.",
        [
            new ApplicationActionParameterDescriptor(
                "text",
                "Текст новой заметки.",
                MaxLength: 4_000)
        ]);

    public ApplicationIntegrationDescriptor Descriptor { get; } = new(
        "microsoft_word",
        "Microsoft Word",
        ["word", "ворд", "microsoft word"],
        [OpenAction, CreateNoteAction]);

    public async ValueTask<CommandResult> ExecuteAsync(
        ApplicationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ApplicationId != Descriptor.Id)
        {
            return CommandResult.Failure("Неизвестное действие Microsoft Word.", "application.action");
        }

        LaunchResult result;
        string successMessage;
        if (request.ActionId == OpenAction.Id)
        {
            result = await launcher.LaunchAsync("winword.exe", cancellationToken);
            successMessage = "Открываю Microsoft Word.";
        }
        else if (request.ActionId == CreateNoteAction.Id &&
                 request.Arguments.TryGetValue("text", out var text) &&
                 !string.IsNullOrWhiteSpace(text) &&
                 text.Length <= 4_000)
        {
            result = await documents.CreateWordNoteAsync(text, cancellationToken);
            successMessage = result.Message;
        }
        else
        {
            return CommandResult.Failure("Неизвестное действие Microsoft Word.", "application.action");
        }

        return result.IsSuccess
            ? CommandResult.Success(successMessage, "application.action")
            : CommandResult.Failure(result.Message, "application.action");
    }
}

public sealed class MicrosoftExcelApplicationIntegration(
    IApplicationLauncher launcher,
    IOfficeDocumentService documents,
    IAssistantSettingsService settings)
    : IApplicationIntegration
{
    private static readonly ApplicationActionDescriptor OpenAction = new(
        "open",
        "Открыть Microsoft Excel",
        "Открывает установленный Microsoft Excel без доступа к книгам.",
        [],
        IsAvailableToAi: true);

    private static readonly ApplicationActionDescriptor SearchAction = new(
        "search_workbook",
        "Найти данные в книге Excel",
        "Ищет текст только в выбранной пользователем книге .xlsx, не изменяя файл.",
        [
            new ApplicationActionParameterDescriptor(
                "document",
                "Псевдоним книги из настроек.",
                MaxLength: 100),
            new ApplicationActionParameterDescriptor(
                "query",
                "Текст для поиска.",
                MaxLength: 160)
        ]);

    public ApplicationIntegrationDescriptor Descriptor { get; } = new(
        "microsoft_excel",
        "Microsoft Excel",
        ["excel", "эксель", "microsoft excel"],
        [OpenAction, SearchAction]);

    public async ValueTask<CommandResult> ExecuteAsync(
        ApplicationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ApplicationId != Descriptor.Id)
        {
            return CommandResult.Failure("Неизвестное действие Microsoft Excel.", "application.action");
        }

        if (request.ActionId == OpenAction.Id)
        {
            var launch = await launcher.LaunchAsync("excel.exe", cancellationToken);
            return launch.IsSuccess
                ? CommandResult.Success("Открываю Microsoft Excel.", "application.action")
                : CommandResult.Failure(launch.Message, "application.action");
        }

        if (request.ActionId != SearchAction.Id ||
            !request.Arguments.TryGetValue("document", out var document) ||
            !request.Arguments.TryGetValue("query", out var query) ||
            !settings.Current.Documents.TryGetValue(document, out var path))
        {
            return CommandResult.Failure(
                "Книга Excel отсутствует в разрешённом списке.",
                "application.action");
        }

        var result = await documents.SearchWorkbookAsync(path, query, cancellationToken);
        if (!result.IsSuccess)
        {
            return CommandResult.Failure(result.Message, "application.action");
        }

        if (result.Matches.Count == 0)
        {
            return CommandResult.Success(result.Message, "application.action");
        }

        var details = string.Join(
            "; ",
            result.Matches.Take(10).Select(match =>
                $"{match.Sheet}!{match.Cell}: {match.Value}"));
        return CommandResult.Success(
            $"{result.Message} {details}",
            "application.action");
    }
}

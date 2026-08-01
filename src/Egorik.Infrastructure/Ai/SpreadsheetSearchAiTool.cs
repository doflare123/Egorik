using Egorik.Core.Ai;
using Egorik.Core.Applications;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;

namespace Egorik.Infrastructure.Ai;

public sealed class SpreadsheetSearchAiTool(
    IAssistantSettingsService settings,
    IApplicationActionRegistry registry) : IAiTool
{
    public AiToolDescriptor Descriptor
    {
        get
        {
            var documents = settings.Current.Documents.Keys
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new AiToolDescriptor(
                "spreadsheet_search",
                "Ищет текст только в выбранной пользователем книге .xlsx из локального разрешённого списка. Файл не изменяется.",
                [
                    new AiToolParameterDescriptor(
                        "document",
                        "Точный псевдоним книги Excel из разрешённого списка.",
                        MaxLength: 100,
                        AllowedValues: documents.Length > 0
                            ? documents
                            : ["__documents_not_configured__"]),
                    new AiToolParameterDescriptor(
                        "query",
                        "Текст для поиска внутри книги.",
                        MaxLength: 160)
                ]);
        }
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("document", out var document) ||
            !arguments.TryGetValue("query", out var query) ||
            !settings.Current.Documents.ContainsKey(document))
        {
            return CommandResult.Failure(
                "Книга Excel отсутствует в разрешённом списке.",
                Descriptor.Id);
        }

        return await registry.ExecuteAsync(
            new ApplicationActionRequest(
                "microsoft_excel",
                "search_workbook",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["document"] = document,
                    ["query"] = query
                }),
            CommandRisk.Low,
            cancellationToken);
    }
}

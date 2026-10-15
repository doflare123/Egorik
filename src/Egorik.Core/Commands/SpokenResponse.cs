namespace Egorik.Core.Commands;

public static class SpokenResponse
{
    public static string For(CommandResult result, bool detailedFailures) =>
        result.IsSuccess || detailedFailures
            ? result.Message
            : result.CommandId == "ai.plan"
                ? "API ИИ недоступен."
                : "Не удалось выполнить команду.";
}

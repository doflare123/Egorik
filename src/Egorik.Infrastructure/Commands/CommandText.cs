namespace Egorik.Infrastructure.Commands;

internal static class CommandText
{
    public static string? AfterPrefix(string text, params string[] prefixes)
    {
        foreach (var prefix in prefixes.OrderByDescending(value => value.Length))
        {
            if (text.Equals(prefix, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            if (text.StartsWith(prefix + " ", StringComparison.Ordinal))
            {
                return text[(prefix.Length + 1)..].Trim();
            }
        }

        return null;
    }
}

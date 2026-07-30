using System.Text;
using System.Text.RegularExpressions;

namespace Egorik.Core.Commands;

public static partial class TextNormalizer
{
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = text.Trim().ToLowerInvariant().Replace('ё', 'е');
        var buffer = new StringBuilder(normalized.Length);

        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (char.IsLetterOrDigit(character) ||
                character is ':' or '/' or '-' or '+' or '*' or '(' or ')')
            {
                buffer.Append(character);
                continue;
            }

            var isBetweenWordCharacters =
                index > 0 &&
                index < normalized.Length - 1 &&
                char.IsLetterOrDigit(normalized[index - 1]) &&
                char.IsLetterOrDigit(normalized[index + 1]);

            if (character == '.' && isBetweenWordCharacters)
            {
                buffer.Append(character);
            }
            else if (character == ',' && isBetweenWordCharacters &&
                     char.IsDigit(normalized[index - 1]) &&
                     char.IsDigit(normalized[index + 1]))
            {
                buffer.Append(character);
            }
            else
            {
                buffer.Append(' ');
            }
        }

        return MultipleWhitespace().Replace(buffer.ToString(), " ").Trim();
    }

    public static string RemoveWakeName(string normalizedText, string assistantName)
    {
        var normalizedName = Normalize(assistantName);
        if (normalizedName.Length == 0)
        {
            return normalizedText;
        }

        if (string.Equals(normalizedText, normalizedName, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return normalizedText.StartsWith(normalizedName + " ", StringComparison.Ordinal)
            ? normalizedText[(normalizedName.Length + 1)..].Trim()
            : normalizedText;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultipleWhitespace();
}

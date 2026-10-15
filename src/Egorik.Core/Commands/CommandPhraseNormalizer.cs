namespace Egorik.Core.Commands;

public static class CommandPhraseNormalizer
{
    private static readonly string[] RequestPrefixes =
    [
        "можешь ли ты", "можешь ты", "ты можешь", "можешь",
        "мог бы ты", "мог бы", "будь добр", "будь добра", "пожалуйста"
    ];

    private static readonly (string Phrase, string Command)[] VerbForms =
    [
        ("открыть", "открой"), ("открывай", "открой"),
        ("запустить", "запусти"), ("запускай", "запусти"),
        ("показать", "покажи"), ("найти", "найди"), ("поискать", "поищи"),
        ("включить", "включи"), ("выключить", "выключи"),
        ("поставить на паузу", "поставь на паузу"),
        ("сделать громче", "сделай громче"), ("сделать тише", "сделай тише")
    ];

    private static readonly string[] OpeningVerbs = ["открой", "запусти", "покажи", "включи", "выключи"];

    public static string Normalize(string normalizedText)
    {
        var text = normalizedText;
        while (true)
        {
            var prefix = RequestPrefixes.FirstOrDefault(value => text.StartsWith(value + " ", StringComparison.Ordinal));
            if (prefix is null)
            {
                break;
            }

            text = text[(prefix.Length + 1)..];
        }

        foreach (var (phrase, command) in VerbForms)
        {
            if (text == phrase || text.StartsWith(phrase + " ", StringComparison.Ordinal))
            {
                text = command + text[phrase.Length..];
                break;
            }
        }

        // Repair spaces at the start of a known verb without changing any letters.
        foreach (var verb in OpeningVerbs)
        {
            var position = 0;
            var matched = true;
            foreach (var letter in verb)
            {
                while (position < text.Length && text[position] == ' ')
                {
                    position++;
                }

                if (position >= text.Length || text[position++] != letter)
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                text = verb + (position < text.Length ? " " + text[position..].TrimStart() : string.Empty);
                break;
            }
        }

        // Fillers are removed only around an explicit opening action, never inside payloads.
        foreach (var verb in OpeningVerbs)
        {
            if (!text.StartsWith(verb + " ", StringComparison.Ordinal))
            {
                continue;
            }

            var target = text[(verb.Length + 1)..];
            while (target.StartsWith("мне ", StringComparison.Ordinal) ||
                   target.StartsWith("пожалуйста ", StringComparison.Ordinal))
            {
                target = target[(target.IndexOf(' ') + 1)..];
            }

            text = verb + " " + target;
            break;
        }

        var hasLiteralPayload = new[] { "найди ", "поищи ", "поиск ", "запиши ", "создай " }
            .Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal));
        return !hasLiteralPayload && text.EndsWith(" пожалуйста", StringComparison.Ordinal)
            ? text[..^" пожалуйста".Length]
            : text;
    }
}

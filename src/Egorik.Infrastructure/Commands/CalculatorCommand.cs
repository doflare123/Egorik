using System.Globalization;
using Egorik.Core.Commands;

namespace Egorik.Infrastructure.Commands;

public sealed class CalculatorCommand : IAssistantCommand
{
    public CommandDescriptor Descriptor { get; } = new(
        "calculator.evaluate",
        "Калькулятор",
        "Вычисляет арифметическое выражение без запуска внешнего кода.");

    public ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default)
    {
        var expression = CommandText.AfterPrefix(
            normalizedText,
            "сколько будет",
            "посчитай",
            "вычисли");

        if (expression is null || expression.Length == 0)
        {
            return ValueTask.FromResult<CommandMatch?>(null);
        }

        expression = PrepareExpression(expression);
        return ValueTask.FromResult(ExpressionParser.TryEvaluate(expression, out _)
            ? CommandMatch.Create(0.96, ("expression", expression))
            : null);
    }

    public ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default)
    {
        var expression = match.Argument("expression");
        if (expression is null || !ExpressionParser.TryEvaluate(expression, out var value))
        {
            return ValueTask.FromResult(
                CommandResult.Failure("Не получилось вычислить выражение.", Descriptor.Id));
        }

        return ValueTask.FromResult(CommandResult.Success(
            $"Получается {value.ToString("0.##########", CultureInfo.GetCultureInfo("ru-RU"))}.",
            Descriptor.Id));
    }

    private static string PrepareExpression(string expression) =>
        expression
            .Replace("умножить на", "*", StringComparison.Ordinal)
            .Replace("умножь на", "*", StringComparison.Ordinal)
            .Replace("разделить на", "/", StringComparison.Ordinal)
            .Replace("поделить на", "/", StringComparison.Ordinal)
            .Replace("плюс", "+", StringComparison.Ordinal)
            .Replace("минус", "-", StringComparison.Ordinal)
            .Replace("x", "*", StringComparison.OrdinalIgnoreCase)
            .Replace(',', '.');
}

internal sealed class ExpressionParser
{
    private readonly string _text;
    private int _position;

    private ExpressionParser(string text) => _text = text;

    public static bool TryEvaluate(string text, out double value)
    {
        try
        {
            var parser = new ExpressionParser(text);
            value = parser.ParseExpression();
            parser.SkipWhitespace();
            return parser._position == text.Length && double.IsFinite(value);
        }
        catch (FormatException)
        {
            value = 0;
            return false;
        }
        catch (DivideByZeroException)
        {
            value = 0;
            return false;
        }
    }

    private double ParseExpression()
    {
        var value = ParseTerm();
        while (true)
        {
            SkipWhitespace();
            if (Take('+'))
            {
                value += ParseTerm();
            }
            else if (Take('-'))
            {
                value -= ParseTerm();
            }
            else
            {
                return value;
            }
        }
    }

    private double ParseTerm()
    {
        var value = ParseFactor();
        while (true)
        {
            SkipWhitespace();
            if (Take('*'))
            {
                value *= ParseFactor();
            }
            else if (Take('/'))
            {
                var divisor = ParseFactor();
                if (Math.Abs(divisor) < double.Epsilon)
                {
                    throw new DivideByZeroException();
                }

                value /= divisor;
            }
            else
            {
                return value;
            }
        }
    }

    private double ParseFactor()
    {
        SkipWhitespace();
        if (Take('+'))
        {
            return ParseFactor();
        }

        if (Take('-'))
        {
            return -ParseFactor();
        }

        if (Take('('))
        {
            var nested = ParseExpression();
            SkipWhitespace();
            if (!Take(')'))
            {
                throw new FormatException("Missing closing parenthesis.");
            }

            return nested;
        }

        return ParseNumber();
    }

    private double ParseNumber()
    {
        SkipWhitespace();
        var start = _position;
        var decimalPointSeen = false;

        while (_position < _text.Length)
        {
            var character = _text[_position];
            if (char.IsDigit(character))
            {
                _position++;
                continue;
            }

            if (character == '.' && !decimalPointSeen)
            {
                decimalPointSeen = true;
                _position++;
                continue;
            }

            break;
        }

        if (start == _position ||
            !double.TryParse(
                _text[start.._position],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw new FormatException("Expected a number.");
        }

        return value;
    }

    private bool Take(char expected)
    {
        if (_position >= _text.Length || _text[_position] != expected)
        {
            return false;
        }

        _position++;
        return true;
    }

    private void SkipWhitespace()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
        {
            _position++;
        }
    }
}

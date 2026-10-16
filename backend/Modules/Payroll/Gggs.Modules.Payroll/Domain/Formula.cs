using System.Globalization;
using Gggs.BuildingBlocks.Domain.Exceptions;

namespace Gggs.Modules.Payroll.Domain;

/// <summary>
/// Salary formulas (spec §21): arithmetic over component codes and a few variables, e.g.
/// <c>MIN(BASIC * 0.5, 50000)</c> or <c>IF(GROSS &lt;= 21000, GROSS * 0.0075, 0)</c>. Numbers,
/// <c>+ - * /</c>, comparisons (true is 1), parentheses and <c>MIN MAX ROUND FLOOR CEIL IF ABS</c> -
/// nothing else, so a formula can never do more than compute a number.
/// </summary>
public static class Formula
{
    private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase) { "MIN", "MAX", "ROUND", "FLOOR", "CEIL", "IF", "ABS" };

    public static decimal Evaluate(string formula, IReadOnlyDictionary<string, decimal> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var parser = new Parser(Tokenize(formula), variables);
        decimal value = parser.Expression();
        parser.ExpectEnd();
        return value;
    }

    /// <summary>The variables a formula reads, upper-cased - to order components and to reject unknown names.</summary>
    public static IReadOnlySet<string> References(string formula)
        => Tokenize(formula).Where(token => token.Kind == TokenKind.Name && !Functions.Contains(token.Text)).Select(token => token.Text.ToUpperInvariant()).ToHashSet();

    /// <summary>Throws a readable error unless the formula parses and reads only <paramref name="known"/> names.</summary>
    public static void Validate(string formula, IReadOnlyCollection<string> known)
    {
        ArgumentNullException.ThrowIfNull(known);
        var unknown = References(formula).Where(name => !known.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
        {
            throw Error($"The formula uses {string.Join(", ", unknown)}, which is not a component code or a known variable.");
        }

        Evaluate(formula, known.ToDictionary(name => name.ToUpperInvariant(), _ => 1m));
    }

    private enum TokenKind
    {
        Number,
        Name,
        Symbol,
    }

    private sealed record Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
        {
            throw Error("The formula is empty.");
        }

        if (formula.Length > 500)
        {
            throw Error("Keep the formula under 500 characters.");
        }

        var tokens = new List<Token>();
        int i = 0;
        while (i < formula.Length)
        {
            char c = formula[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (char.IsDigit(c) || (c == '.' && i + 1 < formula.Length && char.IsDigit(formula[i + 1])))
            {
                int start = i;
                while (i < formula.Length && (char.IsDigit(formula[i]) || formula[i] == '.'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Number, formula[start..i]));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < formula.Length && (char.IsLetterOrDigit(formula[i]) || formula[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Name, formula[start..i]));
            }
            else if (i + 1 < formula.Length && formula.Substring(i, 2) is "<=" or ">=" or "==" or "!=")
            {
                tokens.Add(new Token(TokenKind.Symbol, formula.Substring(i, 2)));
                i += 2;
            }
            else if ("+-*/(),<>".Contains(c, StringComparison.Ordinal))
            {
                tokens.Add(new Token(TokenKind.Symbol, c.ToString()));
                i++;
            }
            else
            {
                throw Error($"Unexpected '{c}' in the formula.");
            }
        }

        return tokens;
    }

    private static DomainRuleException Error(string message) => new("PAYROLL_FORMULA", message);

    private sealed class Parser(List<Token> tokens, IReadOnlyDictionary<string, decimal> variables)
    {
        private int _position;

        public void ExpectEnd()
        {
            if (_position < tokens.Count)
            {
                throw Error($"Unexpected '{tokens[_position].Text}' in the formula.");
            }
        }

        public decimal Expression()
        {
            decimal left = Additive();
            if (Peek() is { Kind: TokenKind.Symbol, Text: "<" or ">" or "<=" or ">=" or "==" or "!=" } comparison)
            {
                _position++;
                decimal right = Additive();
                bool result = comparison.Text switch
                {
                    "<" => left < right,
                    ">" => left > right,
                    "<=" => left <= right,
                    ">=" => left >= right,
                    "==" => left == right,
                    _ => left != right,
                };
                return result ? 1 : 0;
            }

            return left;
        }

        private decimal Additive()
        {
            decimal value = Term();
            while (Peek() is { Kind: TokenKind.Symbol, Text: "+" or "-" } op)
            {
                _position++;
                decimal right = Term();
                value = op.Text == "+" ? value + right : value - right;
            }

            return value;
        }

        private decimal Term()
        {
            decimal value = Unary();
            while (Peek() is { Kind: TokenKind.Symbol, Text: "*" or "/" } op)
            {
                _position++;
                decimal right = Unary();
                if (op.Text == "/" && right == 0)
                {
                    value = 0;
                    continue;
                }

                value = op.Text == "*" ? value * right : value / right;
            }

            return value;
        }

        private decimal Unary()
        {
            if (Peek() is { Kind: TokenKind.Symbol, Text: "-" })
            {
                _position++;
                return -Unary();
            }

            return Primary();
        }

        private decimal Primary()
        {
            Token token = Next() ?? throw Error("The formula ends too soon.");
            switch (token.Kind)
            {
                case TokenKind.Number:
                    return decimal.TryParse(token.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number)
                        ? number
                        : throw Error($"'{token.Text}' is not a number.");

                case TokenKind.Name when Peek() is { Text: "(" }:
                    return Call(token.Text);

                case TokenKind.Name:
                    return variables.TryGetValue(token.Text.ToUpperInvariant(), out decimal value)
                        ? value
                        : throw Error($"'{token.Text}' is not known here.");

                case TokenKind.Symbol when token.Text == "(":
                    decimal inner = Expression();
                    Expect(")");
                    return inner;

                default:
                    throw Error($"Unexpected '{token.Text}' in the formula.");
            }
        }

        private decimal Call(string name)
        {
            if (!Functions.Contains(name))
            {
                throw Error($"'{name}' is not a function formulas can use.");
            }

            Expect("(");
            var arguments = new List<decimal> { Expression() };
            while (Peek() is { Text: "," })
            {
                _position++;
                arguments.Add(Expression());
            }

            Expect(")");
            return name.ToUpperInvariant() switch
            {
                "MIN" => arguments.Min(),
                "MAX" => arguments.Max(),
                "ABS" => Math.Abs(Arity(arguments, 1, name)[0]),
                "FLOOR" => Math.Floor(Arity(arguments, 1, name)[0]),
                "CEIL" => Math.Ceiling(Arity(arguments, 1, name)[0]),
                "ROUND" => arguments.Count switch
                {
                    1 => Math.Round(arguments[0], 0, MidpointRounding.AwayFromZero),
                    2 => Math.Round(arguments[0], (int)Math.Clamp(arguments[1], 0, 6), MidpointRounding.AwayFromZero),
                    _ => throw Error("ROUND takes a value and, optionally, the decimal places."),
                },
                _ => Arity(arguments, 3, name)[0] != 0 ? arguments[1] : arguments[2],
            };
        }

        private static List<decimal> Arity(List<decimal> arguments, int count, string name)
            => arguments.Count == count ? arguments : throw Error($"{name.ToUpperInvariant()} takes {count} argument(s).");

        private Token? Peek() => _position < tokens.Count ? tokens[_position] : null;

        private Token? Next() => _position < tokens.Count ? tokens[_position++] : null;

        private void Expect(string symbol)
        {
            if (Next() is not { Kind: TokenKind.Symbol } token || token.Text != symbol)
            {
                throw Error($"Expected '{symbol}' in the formula.");
            }
        }
    }
}

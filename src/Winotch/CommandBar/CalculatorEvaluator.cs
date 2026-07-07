using System.Globalization;

namespace Winotch.CommandBar;

public static class CalculatorEvaluator
{
    private enum TokenKind { Number, Operator, LeftParen, RightParen }

    private sealed record Token(TokenKind Kind, decimal Number = 0, char Operator = '\0');

    private sealed record OperatorInfo(int Precedence, bool RightAssociative);

    private static readonly IReadOnlyDictionary<char, OperatorInfo> Operators = new Dictionary<char, OperatorInfo>
    {
        ['+'] = new(1, false),
        ['-'] = new(1, false),
        ['*'] = new(2, false),
        ['/'] = new(2, false),
        ['%'] = new(2, false),
        ['^'] = new(3, true)
    };

    public static bool TryEvaluate(string input, out decimal value, out string error)
    {
        value = 0;
        return TryTokenize(input, out var tokens, out error) &&
            TryToRpn(tokens, out var rpn, out error) &&
            TryEvaluateRpn(rpn, out value, out error);
    }

    public static string Format(decimal value) =>
        value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static bool TryTokenize(string input, out List<Token> tokens, out string error)
    {
        tokens = [];
        error = "";
        if (string.IsNullOrWhiteSpace(input) || input.Any(char.IsLetter))
        {
            error = "Only math expressions are supported.";
            return false;
        }

        var expectingValue = true;
        for (var i = 0; i < input.Length;)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Unary minus is folded into the following numeric token so the operator parser stays simple.
            if ((char.IsDigit(c) || c == '.' || c == '-') &&
                TryReadNumber(input, i, expectingValue, out var number, out var next))
            {
                tokens.Add(new Token(TokenKind.Number, number));
                expectingValue = false;
                i = next;
                continue;
            }

            if (Operators.ContainsKey(c))
            {
                if (expectingValue)
                {
                    error = "Missing value.";
                    return false;
                }

                tokens.Add(new Token(TokenKind.Operator, Operator: c));
                expectingValue = true;
                i++;
                continue;
            }

            if (c == '(')
            {
                tokens.Add(new Token(TokenKind.LeftParen));
                expectingValue = true;
                i++;
                continue;
            }

            if (c == ')')
            {
                tokens.Add(new Token(TokenKind.RightParen));
                expectingValue = false;
                i++;
                continue;
            }

            error = "Only math operators, decimals, and parentheses are supported.";
            return false;
        }

        if (tokens.Count == 0 || tokens[^1].Kind == TokenKind.Operator)
        {
            error = "Incomplete expression.";
            return false;
        }

        return true;
    }

    private static bool TryReadNumber(string input, int start, bool expectingValue, out decimal value, out int next)
    {
        value = 0;
        next = start;
        var index = start;
        if (input[index] == '-')
        {
            if (!expectingValue || index + 1 >= input.Length || (!char.IsDigit(input[index + 1]) && input[index + 1] != '.'))
            {
                return false;
            }

            index++;
        }

        var seenDigit = false;
        var seenDot = false;
        while (index < input.Length && (char.IsDigit(input[index]) || input[index] == '.'))
        {
            seenDigit |= char.IsDigit(input[index]);
            if (input[index] == '.')
            {
                if (seenDot)
                {
                    return false;
                }

                seenDot = true;
            }

            index++;
        }

        next = index;
        return seenDigit &&
            decimal.TryParse(input[start..index], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryToRpn(IReadOnlyList<Token> tokens, out List<Token> output, out string error)
    {
        output = [];
        error = "";
        var stack = new Stack<Token>();
        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Number)
            {
                output.Add(token);
                continue;
            }

            if (token.Kind == TokenKind.Operator)
            {
                // Shunting-yard enforces precedence without evaluating untrusted text as code.
                while (stack.TryPeek(out var top) && top.Kind == TokenKind.Operator && ShouldPop(top.Operator, token.Operator))
                {
                    output.Add(stack.Pop());
                }

                stack.Push(token);
                continue;
            }

            if (token.Kind == TokenKind.LeftParen)
            {
                stack.Push(token);
                continue;
            }

            if (!DrainParenthesis(stack, output))
            {
                error = "Unmatched parenthesis.";
                return false;
            }
        }

        while (stack.Count > 0)
        {
            var token = stack.Pop();
            if (token.Kind == TokenKind.LeftParen)
            {
                error = "Unmatched parenthesis.";
                return false;
            }

            output.Add(token);
        }

        return true;
    }

    private static bool DrainParenthesis(Stack<Token> stack, List<Token> output)
    {
        while (stack.Count > 0)
        {
            var top = stack.Pop();
            if (top.Kind == TokenKind.LeftParen)
            {
                return true;
            }

            output.Add(top);
        }

        return false;
    }

    private static bool ShouldPop(char stacked, char incoming)
    {
        var left = Operators[stacked];
        var right = Operators[incoming];
        return left.Precedence > right.Precedence ||
            (left.Precedence == right.Precedence && !right.RightAssociative);
    }

    private static bool TryEvaluateRpn(IReadOnlyList<Token> rpn, out decimal value, out string error)
    {
        value = 0;
        error = "";
        var values = new Stack<decimal>();
        foreach (var token in rpn)
        {
            if (token.Kind == TokenKind.Number)
            {
                values.Push(token.Number);
                continue;
            }

            if (values.Count < 2)
            {
                error = "Missing operand.";
                return false;
            }

            var right = values.Pop();
            var left = values.Pop();
            if (!TryApply(left, right, token.Operator, out var applied, out error))
            {
                return false;
            }

            values.Push(applied);
        }

        if (values.Count != 1)
        {
            error = "Invalid expression.";
            return false;
        }

        value = values.Pop();
        return true;
    }

    private static bool TryApply(decimal left, decimal right, char op, out decimal value, out string error)
    {
        value = 0;
        error = "";
        if (op is '/' or '%' && right == 0)
        {
            error = "Division by zero.";
            return false;
        }

        value = op switch
        {
            '+' => left + right,
            '-' => left - right,
            '*' => left * right,
            '/' => left / right,
            '%' => left % right,
            '^' => (decimal)Math.Pow((double)left, (double)right),
            _ => 0
        };
        return op is '+' or '-' or '*' or '/' or '%' or '^';
    }
}


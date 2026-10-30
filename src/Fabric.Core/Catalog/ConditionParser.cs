namespace Fabric.Core.Catalog;

/// <summary>
/// Parses <c>&lt;dimension&gt; (== | != | in | has) &lt;option(s)&gt;</c>. The grammar has no
/// <c>or</c> and no nesting on purpose (docs/catalog-rules.md).
/// </summary>
public static class ConditionParser
{
    public static Condition Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var cursor = new Cursor(text);
        var dimension = cursor.Identifier("a dimension");
        var condition = cursor.Operator() switch
        {
            ConditionOperator.In => new Condition(dimension, ConditionOperator.In, cursor.OptionList()),
            var single => new Condition(dimension, single, [cursor.Identifier("an option")]),
        };
        cursor.End();

        return condition;
    }

    private sealed class Cursor(string text)
    {
        private int position;

        public string Identifier(string expected)
        {
            SkipWhitespace();
            var start = position;
            if (position < text.Length && IsIdentifierStart(text[position]))
            {
                while (position < text.Length && IsIdentifierPart(text[position]))
                {
                    position++;
                }
            }

            return position > start ? text[start..position] : throw Expected(expected);
        }

        public ConditionOperator Operator()
        {
            SkipWhitespace();
            if (Accept("=="))
            {
                return ConditionOperator.Equals;
            }

            if (Accept("!="))
            {
                return ConditionOperator.NotEquals;
            }

            var start = position;
            var word = position < text.Length && IsIdentifierStart(text[position]) ? Identifier("an operator") : string.Empty;
            switch (word)
            {
                case "in":
                    return ConditionOperator.In;
                case "has":
                    return ConditionOperator.Has;
                default:
                    position = start;
                    throw Expected("an operator (==, !=, in, has)");
            }
        }

        public List<string> OptionList()
        {
            SkipWhitespace();
            if (!Accept("("))
            {
                throw Expected("'('");
            }

            List<string> options = [Identifier("an option")];
            while (true)
            {
                SkipWhitespace();
                if (Accept(")"))
                {
                    return options;
                }

                if (!Accept(","))
                {
                    throw Expected("',' or ')'");
                }

                options.Add(Identifier("an option"));
            }
        }

        public void End()
        {
            SkipWhitespace();
            if (position < text.Length)
            {
                throw Expected("the end");
            }
        }

        private bool Accept(string token)
        {
            if (string.CompareOrdinal(text, position, token, 0, token.Length) != 0)
            {
                return false;
            }

            position += token.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }
        }

        private ConditionSyntaxException Expected(string what)
        {
            var found = position < text.Length ? $"'{text[position]}'" : "the end";
            return new ConditionSyntaxException(
                $"expected {what} at column {position + 1}, found {found} in '{text}'.",
                position + 1);
        }

        private static bool IsIdentifierStart(char character) => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character);

        private static bool IsIdentifierPart(char character) => IsIdentifierStart(character) || character is '.' or '-';
    }
}

using System.Text.RegularExpressions;

namespace LumosPresenter.Core.Parsing;

internal enum TokenKind { Word, Number, Colon, Dash }

internal readonly record struct Token(TokenKind Kind, string Word, int Value)
{
    public static Token OfWord(string word) => new(TokenKind.Word, word, 0);
    public static Token OfNumber(int value) => new(TokenKind.Number, "", value);
    public static readonly Token Colon = new(TokenKind.Colon, "", 0);
    public static readonly Token Dash = new(TokenKind.Dash, "", 0);
}

/// <summary>
/// Turns an utterance into word/number/colon/dash tokens, normalizing spoken numbers:
/// "sixteen" → 16, "twenty three" / "twenty-third" → 23, "one hundred and nineteen" → 119,
/// "one oh five" → 105, ordinals and "3:16" digit forms included. Adjacent simple numbers
/// are deliberately NOT combined ("three sixteen" stays 3, 16 — chapter then verse).
/// </summary>
internal static partial class Tokenizer
{
    [GeneratedRegex(@"\d+(?:st|nd|rd|th)?|[a-z']+|[:\-–—]")]
    private static partial Regex TokenPattern();

    private enum NumberClass { Unit, Teen, Ten, Hundred }

    private static readonly Dictionary<string, (int Value, NumberClass Class)> NumberWords = new()
    {
        ["one"] = (1, NumberClass.Unit), ["two"] = (2, NumberClass.Unit), ["three"] = (3, NumberClass.Unit),
        ["four"] = (4, NumberClass.Unit), ["five"] = (5, NumberClass.Unit), ["six"] = (6, NumberClass.Unit),
        ["seven"] = (7, NumberClass.Unit), ["eight"] = (8, NumberClass.Unit), ["nine"] = (9, NumberClass.Unit),
        ["first"] = (1, NumberClass.Unit), ["second"] = (2, NumberClass.Unit), ["third"] = (3, NumberClass.Unit),
        ["fourth"] = (4, NumberClass.Unit), ["fifth"] = (5, NumberClass.Unit), ["sixth"] = (6, NumberClass.Unit),
        ["seventh"] = (7, NumberClass.Unit), ["eighth"] = (8, NumberClass.Unit), ["ninth"] = (9, NumberClass.Unit),
        ["ii"] = (2, NumberClass.Unit), ["iii"] = (3, NumberClass.Unit),
        ["ten"] = (10, NumberClass.Teen), ["eleven"] = (11, NumberClass.Teen), ["twelve"] = (12, NumberClass.Teen),
        ["thirteen"] = (13, NumberClass.Teen), ["fourteen"] = (14, NumberClass.Teen), ["fifteen"] = (15, NumberClass.Teen),
        ["sixteen"] = (16, NumberClass.Teen), ["seventeen"] = (17, NumberClass.Teen), ["eighteen"] = (18, NumberClass.Teen),
        ["nineteen"] = (19, NumberClass.Teen),
        ["tenth"] = (10, NumberClass.Teen), ["eleventh"] = (11, NumberClass.Teen), ["twelfth"] = (12, NumberClass.Teen),
        ["thirteenth"] = (13, NumberClass.Teen), ["fourteenth"] = (14, NumberClass.Teen), ["fifteenth"] = (15, NumberClass.Teen),
        ["sixteenth"] = (16, NumberClass.Teen), ["seventeenth"] = (17, NumberClass.Teen), ["eighteenth"] = (18, NumberClass.Teen),
        ["nineteenth"] = (19, NumberClass.Teen),
        ["twenty"] = (20, NumberClass.Ten), ["thirty"] = (30, NumberClass.Ten), ["forty"] = (40, NumberClass.Ten),
        ["fifty"] = (50, NumberClass.Ten), ["sixty"] = (60, NumberClass.Ten), ["seventy"] = (70, NumberClass.Ten),
        ["eighty"] = (80, NumberClass.Ten), ["ninety"] = (90, NumberClass.Ten),
        ["twentieth"] = (20, NumberClass.Ten), ["thirtieth"] = (30, NumberClass.Ten), ["fortieth"] = (40, NumberClass.Ten),
        ["fiftieth"] = (50, NumberClass.Ten), ["sixtieth"] = (60, NumberClass.Ten), ["seventieth"] = (70, NumberClass.Ten),
        ["eightieth"] = (80, NumberClass.Ten), ["ninetieth"] = (90, NumberClass.Ten),
        ["hundred"] = (100, NumberClass.Hundred), ["hundredth"] = (100, NumberClass.Hundred),
    };

    public static List<Token> Tokenize(string utterance)
    {
        var raw = new List<Token>();
        foreach (Match m in TokenPattern().Matches(utterance.ToLowerInvariant()))
        {
            var text = m.Value;
            if (char.IsAsciiDigit(text[0]))
            {
                var digits = text.TrimEnd('s', 't', 'n', 'd', 'r', 'h');
                if (int.TryParse(digits, out var value))
                {
                    raw.Add(Token.OfNumber(value));
                }
            }
            else if (text == ":")
            {
                raw.Add(Token.Colon);
            }
            else if (text is "-" or "–" or "—")
            {
                raw.Add(Token.Dash);
            }
            else
            {
                var word = text.Replace("'", "");
                if (word.Length > 0)
                {
                    raw.Add(Token.OfWord(word));
                }
            }
        }
        return MergeNumberWords(raw);
    }

    private static List<Token> MergeNumberWords(List<Token> raw)
    {
        var result = new List<Token>(raw.Count);
        var i = 0;
        while (i < raw.Count)
        {
            var token = raw[i];
            if (token is { Kind: TokenKind.Word, Word: "a" or "an" } && WordAt(raw, i + 1, "hundred", "hundredth"))
            {
                i++; // "a hundred and fifty" — drop the article, let the hundred branch combine
                continue;
            }
            if (token.Kind != TokenKind.Word || !NumberWords.TryGetValue(token.Word, out var entry))
            {
                result.Add(token);
                i++;
                continue;
            }

            switch (entry.Class)
            {
                case NumberClass.Unit when WordAt(raw, i + 1, "hundred", "hundredth"):
                {
                    var value = entry.Value * 100;
                    i = ConsumeHundredTail(raw, i + 2, ref value);
                    result.Add(Token.OfNumber(value));
                    break;
                }
                case NumberClass.Unit when WordAt(raw, i + 1, "oh", "o") && UnitAt(raw, i + 2, out var lastDigit):
                    // "one oh five" → 105
                    result.Add(Token.OfNumber(entry.Value * 100 + lastDigit));
                    i += 3;
                    break;
                case NumberClass.Unit:
                case NumberClass.Teen:
                    result.Add(Token.OfNumber(entry.Value));
                    i++;
                    break;
                case NumberClass.Ten:
                {
                    var value = entry.Value;
                    i++;
                    // "twenty three" or hyphenated "twenty-third"
                    if (UnitAt(raw, i, out var unit))
                    {
                        value += unit;
                        i++;
                    }
                    else if (raw.Count > i + 1 && raw[i].Kind == TokenKind.Dash && UnitAt(raw, i + 1, out var hyphenUnit))
                    {
                        value += hyphenUnit;
                        i += 2;
                    }
                    result.Add(Token.OfNumber(value));
                    break;
                }
                case NumberClass.Hundred:
                {
                    // "a hundred and fifty"
                    var value = 100;
                    i = ConsumeHundredTail(raw, i + 1, ref value);
                    result.Add(Token.OfNumber(value));
                    break;
                }
            }
        }
        return result;
    }

    private static int ConsumeHundredTail(List<Token> raw, int index, ref int value)
    {
        if (WordAt(raw, index, "and") && IsNumberWord(raw, index + 1))
        {
            index++;
        }
        if (index < raw.Count
            && raw[index].Kind == TokenKind.Word
            && NumberWords.TryGetValue(raw[index].Word, out var tail)
            && tail.Class != NumberClass.Hundred)
        {
            value += tail.Value;
            index++;
            if (tail.Class == NumberClass.Ten && UnitAt(raw, index, out var unit))
            {
                value += unit;
                index++;
            }
        }
        return index;
    }

    private static bool WordAt(List<Token> raw, int index, params string[] words) =>
        index < raw.Count && raw[index].Kind == TokenKind.Word && words.Contains(raw[index].Word);

    private static bool IsNumberWord(List<Token> raw, int index) =>
        index < raw.Count && raw[index].Kind == TokenKind.Word && NumberWords.ContainsKey(raw[index].Word);

    private static bool UnitAt(List<Token> raw, int index, out int value)
    {
        value = 0;
        if (index < raw.Count
            && raw[index].Kind == TokenKind.Word
            && NumberWords.TryGetValue(raw[index].Word, out var entry)
            && entry.Class == NumberClass.Unit)
        {
            value = entry.Value;
            return true;
        }
        return false;
    }
}

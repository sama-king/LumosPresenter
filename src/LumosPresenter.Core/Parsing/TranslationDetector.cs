using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Parsing;

/// <summary>
/// Detects a spoken switch to another Bible translation ("let's read from the King James",
/// "switch to the BSB"). Aliases are built from the translations actually in the store
/// (code + name via <see cref="Configure"/>), so imported translations become detectable
/// with no code changes. Multi-word names are distinctive enough to match on bare mention;
/// short letter codes only count when a <see cref="TranslationCues"/> phrase immediately
/// precedes them — mirroring the reference parser's cue-boost design.
/// </summary>
public sealed class TranslationDetector
{
    private sealed record Alias(string[] Words, string Code, bool RequiresCue);

    /// <summary>Name suffixes too generic to identify a translation on their own.</summary>
    private static readonly string[] GenericSuffixes = ["version", "bible", "translation", "edition"];

    private volatile List<Alias> _aliases = [];

    public bool IsConfigured => _aliases.Count > 0;

    /// <summary>Rebuilds the alias table from the store's translation list.</summary>
    public void Configure(IEnumerable<Translation> translations)
    {
        var aliases = new List<Alias>();
        foreach (var translation in translations)
        {
            var code = translation.Id;
            var nameWords = Tokenizer.Tokenize(translation.Name)
                .Where(t => t.Kind == TokenKind.Word)
                .Select(t => t.Word)
                .ToArray();

            // Full name, then the name with generic suffixes trimmed ("King James Version" →
            // "King James"). Single words are too ambiguous to stand alone as bare mentions.
            for (var length = nameWords.Length; length >= 2; length--)
            {
                if (length < nameWords.Length && !GenericSuffixes.Contains(nameWords[length]))
                {
                    break;
                }
                aliases.Add(new Alias(nameWords[..length], code, RequiresCue: false));
            }

            // The letter code, spoken as one word ("KJV") or spelled out ("K J V") — cue-gated.
            var codeWord = code.ToLowerInvariant();
            aliases.Add(new Alias([codeWord], code, RequiresCue: true));
            if (codeWord.Length > 1)
            {
                aliases.Add(new Alias(codeWord.Select(c => c.ToString()).ToArray(), code, RequiresCue: true));
            }
        }
        _aliases = aliases.OrderByDescending(a => a.Words.Length).ToList();
    }

    /// <summary>
    /// The code of the last translation named in the utterance, or null. Last match wins:
    /// "not the King James — let's use the Berean Standard" lands on BSB.
    /// </summary>
    public string? Detect(string utterance)
    {
        var aliases = _aliases;
        if (aliases.Count == 0)
        {
            return null;
        }

        var tokens = Tokenizer.Tokenize(utterance);
        string? found = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            foreach (var alias in aliases)
            {
                if (!MatchesAt(tokens, i, alias.Words))
                {
                    continue;
                }
                if (alias.RequiresCue && !HasCueEndingAt(tokens, i))
                {
                    continue;
                }
                found = alias.Code;
                i += alias.Words.Length - 1;
                break;
            }
        }
        return found;
    }

    private static bool MatchesAt(List<Token> tokens, int index, string[] words)
    {
        if (index + words.Length > tokens.Count)
        {
            return false;
        }
        for (var k = 0; k < words.Length; k++)
        {
            if (tokens[index + k].Kind != TokenKind.Word || tokens[index + k].Word != words[k])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>True if a cue phrase occupies the words ending just before <paramref name="index"/>.</summary>
    private static bool HasCueEndingAt(List<Token> tokens, int index)
    {
        foreach (var phrase in TranslationCues.Phrases)
        {
            var start = index - phrase.Length;
            if (start < 0)
            {
                continue;
            }
            var matched = true;
            for (var k = 0; k < phrase.Length; k++)
            {
                if (tokens[start + k].Kind != TokenKind.Word || tokens[start + k].Word != phrase[k])
                {
                    matched = false;
                    break;
                }
            }
            if (matched)
            {
                return true;
            }
        }
        return false;
    }
}

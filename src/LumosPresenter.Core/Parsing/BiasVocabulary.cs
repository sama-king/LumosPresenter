namespace LumosPresenter.Core.Parsing;

/// <summary>
/// Vocabulary for biasing speech engines toward scripture terms. Only book names and
/// numbers need to survive transcription, so priming the decoder with the 66 book names
/// plus "chapter"/"verse" measurably reduces book mis-transcriptions ("Filipians" →
/// "Philippians"). Used as Whisper's initial prompt and as sherpa-onnx hotwords.
/// </summary>
public static class BiasVocabulary
{
    private static readonly string[] Ordinals = ["First", "Second", "Third"];

    /// <summary>Distinct book names in speakable form (e.g. "1 Corinthians" → "First Corinthians").</summary>
    public static IReadOnlyList<string> BookPhrases { get; } = BuildBookPhrases();

    /// <summary>
    /// A single natural-language sentence suitable as Whisper's <c>WithPrompt</c> seed.
    /// Whisper conditions on it as if it were prior context, nudging spelling and word choice.
    /// </summary>
    public static string WhisperInitialPrompt { get; } =
        "Scripture reading. Books, chapters, and verses: " +
        string.Join(", ", BookPhrases) + ". Chapter and verse.";

    private static List<string> BuildBookPhrases()
    {
        var phrases = new List<string>(BookCatalog.Books.Count);
        foreach (var book in BookCatalog.Books)
        {
            // Canonical names carry a leading digit for numbered books ("1 Corinthians");
            // speech engines respond better to the spoken ordinal form.
            if (book.Name.Length > 2 && char.IsAsciiDigit(book.Name[0]) && book.Name[1] == ' ')
            {
                var ordinal = Ordinals[book.Name[0] - '1'];
                phrases.Add($"{ordinal} {book.Name[2..]}");
            }
            else
            {
                phrases.Add(book.Name);
            }
        }
        return phrases;
    }
}

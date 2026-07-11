using LumosPresenter.Core.Parsing;

namespace LumosPresenter.Tests.Parsing;

public class BiasVocabularyTests
{
    [Fact]
    public void BookPhrases_CoverAll66Books()
    {
        Assert.Equal(66, BiasVocabulary.BookPhrases.Count);
    }

    [Fact]
    public void BookPhrases_UseSpokenOrdinalsForNumberedBooks()
    {
        Assert.Contains("First Corinthians", BiasVocabulary.BookPhrases);
        Assert.Contains("Second Timothy", BiasVocabulary.BookPhrases);
        Assert.Contains("Third John", BiasVocabulary.BookPhrases);
        Assert.DoesNotContain("1 Corinthians", BiasVocabulary.BookPhrases);
    }

    [Fact]
    public void WhisperInitialPrompt_MentionsBooksAndStructureWords()
    {
        var prompt = BiasVocabulary.WhisperInitialPrompt;
        Assert.Contains("Genesis", prompt);
        Assert.Contains("Revelation", prompt);
        Assert.Contains("Philippians", prompt);
        Assert.Contains("chapter", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verse", prompt, StringComparison.OrdinalIgnoreCase);
    }
}

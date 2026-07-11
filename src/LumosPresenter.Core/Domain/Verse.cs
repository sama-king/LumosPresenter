namespace LumosPresenter.Core.Domain;

/// <summary>A single verse of scripture in a specific translation.</summary>
public sealed record Verse(string TranslationId, string Book, int Chapter, int Number, string Text);

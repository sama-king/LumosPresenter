namespace LumosPresenter.Core.Domain;

/// <summary>
/// A detected scripture reference, translation-agnostic. <paramref name="Confidence"/>
/// (0..1) feeds the confirm gate: low-confidence detections queue for operator approval.
/// </summary>
public sealed record BibleReference(
    string Book,
    int Chapter,
    int? VerseStart,
    int? VerseEnd,
    double Confidence)
{
    public override string ToString() =>
        VerseStart is null ? $"{Book} {Chapter}"
        : VerseEnd is null || VerseEnd == VerseStart ? $"{Book} {Chapter}:{VerseStart}"
        : $"{Book} {Chapter}:{VerseStart}-{VerseEnd}";
}

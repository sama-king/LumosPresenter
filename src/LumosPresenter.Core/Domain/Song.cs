namespace LumosPresenter.Core.Domain;

/// <summary>One projection slide of a song: a blank-line-separated lyrics block.</summary>
public sealed record SongSection(int Position, string? Label, string Text);

/// <summary>A song in the library with its ordered sections (slides).</summary>
public sealed record Song(
    int Id,
    string Title,
    string? Author,
    string? Copyright,
    IReadOnlyList<SongSection> Sections);

/// <summary>Library listing row — enough for search results without loading lyrics.</summary>
public sealed record SongSummary(int Id, string Title, string? Author);

/// <summary>
/// A song about to be created or saved. Source records how it entered the library
/// ('editor' | 'txt' | 'easyworship') so imports are distinguishable later.
/// </summary>
public sealed record SongDraft(
    string Title,
    string? Author,
    string? Copyright,
    IReadOnlyList<SongSection> Sections,
    string Source = "editor");

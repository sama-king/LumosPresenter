using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;

namespace LumosPresenter.WebHost.Realtime;

/// <summary>
/// The translation verses are resolved in; switchable from the operator console or by
/// voice ("switch to the King James"). Every switch — whatever its source — re-pushes
/// the current live scripture in the new translation, so the displays follow immediately.
/// </summary>
public sealed class TranslationState(
    EventBroadcaster broadcaster,
    LiveState live,
    ILogger<TranslationState> logger)
{
    private volatile string _current = "KJV";

    public string Current => _current;

    public async Task SelectAsync(string code, IVerseRepository repository, CancellationToken cancellationToken = default)
    {
        var translations = await repository.GetTranslationsAsync(cancellationToken);
        var match = translations.FirstOrDefault(t => t.Id.Equals(code, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"Unknown translation '{code}'. Available: {string.Join(", ", translations.Select(t => t.Id))}.");
        if (_current == match.Id)
        {
            return;
        }
        _current = match.Id;
        broadcaster.Publish(new PipelineEvent("translation", new { translation = match.Id }));
        await RepushLiveAsync(match.Id, repository, cancellationToken);
    }

    /// <summary>
    /// Re-resolves whatever is live in the newly selected translation and shows it, so a
    /// switch updates the displays without the operator pushing again. Items without a
    /// structured reference (or passages the new translation lacks) stay as they are.
    /// </summary>
    private async Task RepushLiveAsync(string code, IVerseRepository repository, CancellationToken cancellationToken)
    {
        if (live.Current is not { Book: not null, Chapter: not null, VerseStart: not null } item
            || item.Translation == code)
        {
            return;
        }
        try
        {
            var reference = new BibleReference(item.Book, item.Chapter.Value, item.VerseStart, item.VerseEnd, 1.0);
            var verses = await repository.GetVersesAsync(code, reference, cancellationToken);
            if (verses.Count == 0)
            {
                logger.LogWarning("No text for {Reference} in {Translation}; keeping current live item",
                    item.Reference, code);
                return;
            }
            live.Show(item with
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = string.Join(" ", verses.Select(v => v.Text)),
                Translation = code,
                At = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Re-push after translation switch failed for {Reference} ({Translation})",
                item.Reference, code);
        }
    }

    /// <summary>Sets the startup default without validation (the DB may still be seeding).</summary>
    public void SetDefault(string code) => _current = code;
}

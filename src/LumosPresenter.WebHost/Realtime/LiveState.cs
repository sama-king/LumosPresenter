namespace LumosPresenter.WebHost.Realtime;

/// <summary>
/// One item shown on the projection displays. Book/Chapter/VerseStart/VerseEnd carry the
/// structured reference so a translation switch can re-resolve the same passage; they are
/// null for free-form pushes, which then simply keep their text across switches. Kind
/// ('scripture' | 'song') tells the display which config block to style with; it defaults
/// to 'scripture' so every existing push (auto-live pipeline, console) is unaffected.
/// </summary>
public sealed record LiveItem(
    string Id,
    string Reference,
    string Text,
    string Translation,
    string Source,
    DateTimeOffset At,
    string? Book = null,
    int? Chapter = null,
    int? VerseStart = null,
    int? VerseEnd = null,
    string Kind = "scripture");

/// <summary>
/// The single source of truth for what is live on the displays. Every push —
/// manual (operator console) or automatic (confidence-gated detections) — flows
/// through here and fans out as an SSE "live" event, so displays never care who
/// initiated the change.
/// </summary>
public sealed class LiveState(EventBroadcaster broadcaster)
{
    private readonly Lock _gate = new();
    private LiveItem? _current;

    public LiveItem? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Show(LiveItem item)
    {
        lock (_gate)
        {
            // Duplicate suppression: re-detections of what is already showing (or the
            // console mirroring a server auto-push) must not re-fire the displays.
            if (_current is { } current
                && current.Kind == item.Kind
                && current.Reference == item.Reference
                && current.Text == item.Text
                && current.Translation == item.Translation)
            {
                return;
            }
            _current = item;
        }
        broadcaster.Publish(new PipelineEvent("live", item));
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
        }
        broadcaster.Publish(new PipelineEvent("live", new { cleared = true }));
    }
}

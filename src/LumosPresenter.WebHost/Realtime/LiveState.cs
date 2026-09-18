namespace LumosPresenter.WebHost.Realtime;

/// <summary>
/// One item shown on the projection displays. Book/Chapter/VerseStart/VerseEnd carry the
/// structured reference so a translation switch can re-resolve the same passage; they are
/// null for free-form pushes, which then simply keep their text across switches. Kind
/// ('scripture' | 'song' | 'media') tells the display which config block to style with; it
/// defaults to 'scripture' so every existing push (auto-live pipeline, console) is unaffected.
/// MediaId/MediaKind are set only for 'media' pushes and name a media_library row, which the
/// display fetches from /api/media/library/{id}/file. MediaLoop tells the display whether a
/// video repeats: a standalone clip loops, but a queue member must be allowed to end so it can
/// report back and let the console advance to the next video.
///
/// Revision is stamped by <see cref="LiveState"/> and increases with every change. A display
/// that missed an event (dropped from a full SSE buffer, or a reconnect it slept through) sees
/// a revision ahead of the one it applied and re-fetches, so a stale picture cannot outlive
/// one heartbeat.
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
    string Kind = "scripture",
    string? MediaId = null,
    string? MediaKind = null,
    bool MediaLoop = true,
    long Revision = 0);

/// <summary>
/// Playback state for the video that is live, owned by the operator console and mirrored by
/// every display. Displays never run their own transport: they hold a video element, and this
/// record — position at <see cref="At"/>, plus whether the clock is running — is the only thing
/// that tells them where to be. That is what keeps two screens frame-close instead of drifting
/// apart from independent autoplay.
///
/// ItemId names the live item this applies to, so a transport for a superseded push is ignored
/// rather than seeking whatever happens to be showing now.
///
/// Volume and Muted are the PROGRAM audio — what the room hears. They ride here for the same
/// reason play/pause does: one console control, every display obeying it. Which displays
/// actually emit sound is a per-display setting (MediaDisplayConfig.Audio), so a second screen
/// or a confidence monitor does not double the audio; and the operator's own monitoring at the
/// desk is local to the console and never travels.
/// </summary>
public sealed record MediaTransport(
    string ItemId,
    string MediaId,
    bool Playing,
    double Position,
    bool Loop,
    DateTimeOffset At,
    double Volume = 1,
    bool Muted = false,
    long Revision = 0);

/// <summary>
/// A full picture of the live channel, for late joiners and out-of-date displays. Backdrop is
/// only meaningful when Item is null: it names the text config ('scripture' | 'songs') whose
/// window background stays up after a text-only clear, or null when the screen is fully clear.
/// </summary>
public sealed record LiveSnapshot(long Revision, LiveItem? Item, MediaTransport? Transport, string? Backdrop = null);

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
    private MediaTransport? _transport;
    private string? _backdrop;
    private long _revision;

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

    /// <summary>Everything a display needs to catch up in one read.</summary>
    public LiveSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new LiveSnapshot(_revision, _current, _transport, _backdrop);
        }
    }

    /// <summary>
    /// Puts an item on the displays. Returns what is live afterwards, which is the item passed
    /// in unless it was suppressed as a duplicate — the caller needs to know, because the id it
    /// gets back is the one every later transport command and end-report is matched against.
    /// </summary>
    public LiveItem Show(LiveItem item)
    {
        LiveItem stamped;
        MediaTransport? transport;
        lock (_gate)
        {
            // Duplicate suppression: re-detections of what is already showing (or the
            // console mirroring a server auto-push) must not re-fire the displays. Media is
            // exempt — a media push carries no text to tell two of them apart, and re-pushing
            // the frame that is already up is how the console restarts a video or steps a
            // slideshow onto a repeated image.
            if (item.Kind != "media"
                && _current is { } current
                && current.Kind == item.Kind
                && current.Reference == item.Reference
                && current.Text == item.Text
                && current.Translation == item.Translation)
            {
                return current;
            }
            var carriedVolume = _transport?.Volume ?? 1;
            var carriedMuted = _transport?.Muted ?? false;
            stamped = item with { Revision = ++_revision };
            _current = stamped;
            // Media fills its own viewport and has no text window, so a background left up by
            // an earlier text-only clear does not survive it.
            if (stamped.Kind == "media")
            {
                _backdrop = null;
            }
            // A new push invalidates the old transport. A video starts from the top, playing;
            // anything else has no transport at all, which is also what tells a display to
            // tear down a video element it may still be holding.
            // Volume carries across pushes: an operator who set the level for one clip means it
            // for the next, and a queue that reset to full every time would be unusable.
            _transport = stamped is { Kind: "media", MediaKind: "video", MediaId: { } mediaId }
                ? new MediaTransport(stamped.Id, mediaId, Playing: true, Position: 0, stamped.MediaLoop,
                    DateTimeOffset.UtcNow, carriedVolume, carriedMuted, _revision)
                : null;
            transport = _transport;
        }
        broadcaster.Publish(new PipelineEvent("live", stamped));
        if (transport is not null)
        {
            broadcaster.Publish(new PipelineEvent("mediatransport", transport));
        }
        return stamped;
    }

    /// <summary>
    /// Applies a console transport command (play / pause / seek / loop) to the live video and
    /// fans it out. Returns false when the command names an item that is no longer live —
    /// a late click on a queue entry the operator has already moved past.
    /// </summary>
    public bool SetTransport(
        string itemId,
        bool playing,
        double position,
        bool? loop,
        double? volume = null,
        bool? muted = null)
    {
        MediaTransport transport;
        lock (_gate)
        {
            if (_current is not { Kind: "media", MediaKind: "video", MediaId: { } mediaId }
                || _current.Id != itemId)
            {
                return false;
            }
            transport = new MediaTransport(
                itemId,
                mediaId,
                playing,
                Math.Max(0, position),
                loop ?? _transport?.Loop ?? _current.MediaLoop,
                DateTimeOffset.UtcNow,
                Math.Clamp(volume ?? _transport?.Volume ?? 1, 0, 1),
                muted ?? _transport?.Muted ?? false,
                ++_revision);
            _transport = transport;
        }
        broadcaster.Publish(new PipelineEvent("mediatransport", transport));
        return true;
    }

    /// <summary>
    /// Takes the live item off the displays. A full clear leaves them transparent. A text-only
    /// clear removes the words but keeps the text window's background (solid, image or looping
    /// video) up, styled by whichever text config was live: the operator's "between slides"
    /// state, so the screen does not flash to black. Clearing text while media is live has no
    /// text window to keep, so it clears fully; repeating it while already text-cleared keeps
    /// the background that is up.
    /// </summary>
    public void Clear(bool textOnly = false)
    {
        long revision;
        string? backdrop;
        lock (_gate)
        {
            _backdrop = !textOnly
                ? null
                : _current switch
                {
                    null => _backdrop,
                    { Kind: "media" } => null,
                    { Kind: "song" } => "songs",
                    _ => "scripture",
                };
            _current = null;
            _transport = null;
            revision = ++_revision;
            backdrop = _backdrop;
        }
        broadcaster.Publish(new PipelineEvent("live", new
        {
            cleared = true,
            revision,
            scope = textOnly ? "text" : "all",
            backdrop,
        }));
    }
}

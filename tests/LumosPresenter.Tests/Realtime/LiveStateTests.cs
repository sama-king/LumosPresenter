using System.Threading.Channels;
using LumosPresenter.WebHost.Realtime;

namespace LumosPresenter.Tests.Realtime;

/// <summary>
/// The live channel is what the projection displays obey, so the contracts here are the ones
/// that decide whether two screens agree: every change moves the revision (which is how a
/// display that missed an event finds out), a media push is never suppressed as a duplicate,
/// and video transport only applies to the item that is actually live.
/// </summary>
public sealed class LiveStateTests
{
    private static (LiveState Live, ChannelReader<PipelineEvent> Events) Subscribed()
    {
        var broadcaster = new EventBroadcaster();
        var live = new LiveState(broadcaster);
        var (_, reader) = broadcaster.Subscribe();
        return (live, reader);
    }

    private static LiveItem Scripture(string reference, string text) =>
        new(Guid.NewGuid().ToString("N"), reference, text, "KJV", "manual", DateTimeOffset.UtcNow);

    private static LiveItem Media(string mediaId, string kind = "video", bool loop = true) =>
        new(Guid.NewGuid().ToString("N"), mediaId, string.Empty, string.Empty, "manual",
            DateTimeOffset.UtcNow, Kind: "media", MediaId: mediaId, MediaKind: kind, MediaLoop: loop);

    private static List<PipelineEvent> Drain(ChannelReader<PipelineEvent> reader)
    {
        var events = new List<PipelineEvent>();
        while (reader.TryRead(out var next)) events.Add(next);
        return events;
    }

    [Fact]
    public void Show_stamps_an_increasing_revision()
    {
        var (live, _) = Subscribed();

        var first = live.Show(Scripture("John 3:16", "For God so loved"));
        var second = live.Show(Scripture("John 3:17", "For God sent not"));

        Assert.True(second.Revision > first.Revision);
        Assert.Equal(second.Revision, live.Snapshot().Revision);
    }

    [Fact]
    public void Show_suppresses_a_repeated_scripture_push()
    {
        var (live, events) = Subscribed();
        var shown = live.Show(Scripture("John 3:16", "For God so loved"));
        Drain(events);

        // A re-detection of what is already up must not re-fire the displays — but the caller
        // still needs the id of what IS live, not the id it offered.
        var again = live.Show(Scripture("John 3:16", "For God so loved"));

        Assert.Equal(shown.Id, again.Id);
        Assert.Empty(Drain(events));
    }

    [Fact]
    public void Show_never_suppresses_a_media_push()
    {
        var (live, events) = Subscribed();
        live.Show(Media("clip-1"));
        Drain(events);

        // Re-pushing the frame that is already up is how the console restarts a clip and how
        // a slideshow steps onto a repeated image; suppressing it would stall the queue.
        var again = live.Show(Media("clip-1"));

        Assert.Equal("clip-1", live.Snapshot().Item?.MediaId);
        Assert.Equal(again.Id, live.Snapshot().Item?.Id);
        Assert.Contains(Drain(events), e => e.Type == "live");
    }

    [Fact]
    public void Showing_a_video_starts_it_playing_from_the_top()
    {
        var (live, events) = Subscribed();

        var item = live.Show(Media("clip-1"));

        var transport = live.Snapshot().Transport;
        Assert.NotNull(transport);
        Assert.Equal(item.Id, transport.ItemId);
        Assert.True(transport.Playing);
        Assert.Equal(0, transport.Position);
        Assert.Contains(Drain(events), e => e.Type == "mediatransport");
    }

    [Fact]
    public void Showing_an_image_leaves_no_transport_behind()
    {
        var (live, _) = Subscribed();
        live.Show(Media("clip-1"));

        live.Show(Media("still-1", kind: "image"));

        // A stale clock must not drive whatever comes next — this is the display's cue to
        // tear down the video element it was holding.
        Assert.Null(live.Snapshot().Transport);
    }

    [Fact]
    public void SetTransport_applies_to_the_live_item_and_moves_the_revision()
    {
        var (live, events) = Subscribed();
        var item = live.Show(Media("clip-1"));
        var afterShow = live.Snapshot().Revision;
        Drain(events);

        var applied = live.SetTransport(item.Id, playing: false, position: 12.5, loop: null);

        Assert.True(applied);
        var transport = live.Snapshot().Transport;
        Assert.NotNull(transport);
        Assert.False(transport.Playing);
        Assert.Equal(12.5, transport.Position);
        Assert.True(live.Snapshot().Revision > afterShow);
        Assert.Contains(Drain(events), e => e.Type == "mediatransport");
    }

    [Fact]
    public void SetTransport_ignores_a_command_for_an_item_that_is_no_longer_live()
    {
        var (live, _) = Subscribed();
        var first = live.Show(Media("clip-1"));
        live.Show(Media("clip-2"));

        // A click that landed after the operator moved on must not seek what is up now.
        var applied = live.SetTransport(first.Id, playing: false, position: 30, loop: null);

        Assert.False(applied);
        Assert.Equal(0, live.Snapshot().Transport?.Position);
    }

    [Fact]
    public void Clear_drops_the_item_and_its_transport()
    {
        var (live, events) = Subscribed();
        live.Show(Media("clip-1"));
        var beforeClear = live.Snapshot().Revision;
        Drain(events);

        live.Clear();

        var snapshot = live.Snapshot();
        Assert.Null(snapshot.Item);
        Assert.Null(snapshot.Transport);
        Assert.True(snapshot.Revision > beforeClear);
        Assert.Contains(Drain(events), e => e.Type == "live");
    }

    private static LiveItem Song(string text) =>
        new(Guid.NewGuid().ToString("N"), "Amazing Grace", text, string.Empty, "manual",
            DateTimeOffset.UtcNow, Kind: "song");

    [Fact]
    public void Clearing_text_keeps_the_background_of_the_text_type_that_was_live()
    {
        var (live, _) = Subscribed();

        live.Show(Scripture("John 3:16", "For God so loved"));
        live.Clear(textOnly: true);
        Assert.Null(live.Snapshot().Item);
        Assert.Equal("scripture", live.Snapshot().Backdrop);

        live.Show(Song("Amazing grace, how sweet the sound"));
        live.Clear(textOnly: true);
        Assert.Equal("songs", live.Snapshot().Backdrop);
    }

    [Fact]
    public void Clearing_text_again_keeps_the_background_that_is_up()
    {
        var (live, _) = Subscribed();
        live.Show(Song("Amazing grace"));
        live.Clear(textOnly: true);

        live.Clear(textOnly: true);

        Assert.Equal("songs", live.Snapshot().Backdrop);
    }

    [Fact]
    public void Clearing_all_drops_the_background()
    {
        var (live, _) = Subscribed();
        live.Show(Scripture("John 3:16", "For God so loved"));
        live.Clear(textOnly: true);

        live.Clear();

        Assert.Null(live.Snapshot().Backdrop);
    }

    [Fact]
    public void Clearing_text_while_media_is_live_clears_fully()
    {
        var (live, _) = Subscribed();
        live.Show(Scripture("John 3:16", "For God so loved"));
        live.Show(Media("clip-1"));

        live.Clear(textOnly: true);

        Assert.Null(live.Snapshot().Backdrop);
    }

    [Fact]
    public void A_media_push_takes_down_a_background_left_by_a_text_clear()
    {
        var (live, _) = Subscribed();
        live.Show(Scripture("John 3:16", "For God so loved"));
        live.Clear(textOnly: true);

        live.Show(Media("still-1", kind: "image"));
        live.Clear(textOnly: true);

        Assert.Null(live.Snapshot().Backdrop);
    }

    [Fact]
    public void Reshowing_the_same_verse_after_a_text_clear_is_not_suppressed()
    {
        var (live, events) = Subscribed();
        live.Show(Scripture("John 3:16", "For God so loved"));
        live.Clear(textOnly: true);
        Drain(events);

        live.Show(Scripture("John 3:16", "For God so loved"));

        Assert.NotNull(live.Snapshot().Item);
        Assert.Contains(Drain(events), e => e.Type == "live");
    }
}

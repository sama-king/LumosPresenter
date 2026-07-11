using System.Collections.Concurrent;
using System.Threading.Channels;

namespace LumosPresenter.WebHost.Realtime;

/// <summary>An event pushed to browsers over the one-way SSE stream at /events.</summary>
public sealed record PipelineEvent(string Type, object Payload);

/// <summary>
/// Fans pipeline events out to every connected SSE client. Slow clients drop the
/// oldest events instead of applying backpressure to the pipeline.
/// </summary>
public sealed class EventBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<PipelineEvent>> _subscribers = new();

    public (Guid Id, ChannelReader<PipelineEvent> Reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<PipelineEvent>(new BoundedChannelOptions(capacity: 256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public void Publish(PipelineEvent pipelineEvent)
    {
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(pipelineEvent);
        }
    }
}

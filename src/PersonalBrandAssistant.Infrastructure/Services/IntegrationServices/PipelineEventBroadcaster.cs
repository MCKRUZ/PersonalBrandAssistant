using System.Collections.Concurrent;
using System.Threading.Channels;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.IntegrationServices;

public sealed class PipelineEventBroadcaster : IPipelineEventBroadcaster
{
    private readonly ConcurrentDictionary<ChannelReader<PipelineEvent>, Channel<PipelineEvent>> _subscribers = new();

    public ChannelReader<PipelineEvent> Subscribe()
    {
        var channel = Channel.CreateBounded<PipelineEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _subscribers.TryAdd(channel.Reader, channel);
        return channel.Reader;
    }

    public void Unsubscribe(ChannelReader<PipelineEvent> reader)
    {
        if (_subscribers.TryRemove(reader, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public ValueTask BroadcastAsync(PipelineEvent pipelineEvent)
    {
        foreach (var (reader, channel) in _subscribers)
        {
            if (!channel.Writer.TryWrite(pipelineEvent))
            {
                if (channel.Writer.TryComplete())
                {
                    _subscribers.TryRemove(reader, out _);
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}

using System.Threading.Channels;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IPipelineEventBroadcaster
{
    ChannelReader<PipelineEvent> Subscribe();
    void Unsubscribe(ChannelReader<PipelineEvent> reader);
    ValueTask BroadcastAsync(PipelineEvent pipelineEvent);
}

using Microsoft.Extensions.AI;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IChatClientFactory
{
    IChatClient CreateClient(ModelTier tier);
    IChatClient CreateStreamingClient(ModelTier tier);
}

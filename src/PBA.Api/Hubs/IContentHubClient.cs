namespace PBA.Api.Hubs;

public interface IContentHubClient
{
    Task ReceiveToken(string token);
    Task GenerationComplete(string fullResponse);
    Task GenerationError(string error);
}

namespace PBA.Application.Common.Interfaces;

public interface IContentPublisher
{
    Task PublishAsync(Guid contentId);
}

using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Common.Interfaces;

// One keyed service per analytics platform (YouTube, Instagram, TikTok). A thin facade over an injectable
// HTTP/SDK client; PollAsync never throws (API failures -> Result.Fail). The credential carries the
// decrypted-at-use access token (the poller ensures freshness before calling).
public interface IChannelAnalyticsService
{
    Platform Platform { get; }

    Task<Result<ChannelPollResult>> PollAsync(
        PlatformCredential credential, int recentVideoCount, CancellationToken ct);
}

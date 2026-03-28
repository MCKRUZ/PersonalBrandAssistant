using Microsoft.Extensions.Logging;

namespace PersonalBrandAssistant.Infrastructure.Services.BlogServices;

internal sealed class AuthHeaderRedactingHandler : DelegatingHandler
{
    private readonly ILogger<AuthHeaderRedactingHandler> _logger;

    public AuthHeaderRedactingHandler(ILogger<AuthHeaderRedactingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var method = request.Method;
            var uri = request.RequestUri;
            _logger.LogDebug("GitHub API request: {Method} {Uri}", method, uri);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GitHub API error: {StatusCode} for {Method} {Uri}",
                (int)response.StatusCode, request.Method, request.RequestUri);
        }

        return response;
    }
}

using System.Net;

namespace QuotesApi.Resilience;

/// <summary>
/// A <see cref="DelegatingHandler"/> that short-circuits every outbound HTTP request
/// with a 503 Service Unavailable response when fault injection is enabled.
///
/// This gives us a deterministic, zero-network way to drive the Polly pipeline
/// through CLOSED → OPEN → HALF-OPEN → CLOSED without relying on real network
/// failures or a separate mock server.
///
/// Thread-safety: the <see cref="Enabled"/> flag is a <c>volatile bool</c>.
/// Toggling it is safe from any thread (including concurrent API handlers).
///
/// Registration: added as an additional handler INSIDE the Polly resilience
/// handler, so Polly sees the 503 and applies its retry / circuit-breaker logic.
/// </summary>
public sealed class FaultInjectionHandler : DelegatingHandler
{
    private static volatile bool _enabled;

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_enabled)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(
                    "Fault injection active — simulated 503 Service Unavailable")
            });
        }

        return base.SendAsync(request, cancellationToken);
    }
}

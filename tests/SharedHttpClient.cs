using System.Globalization;
using System.Net;

namespace Meziantou.RenovateConfig.Tests;

internal static class SharedHttpClient
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        const int MaxRetries = 5;
        var defaultDelay = TimeSpan.FromMilliseconds(200);

        for (var attempt = 1; ; attempt++, defaultDelay += defaultDelay)
        {
            TimeSpan? delayHint = null;
            HttpResponseMessage? response = null;

            try
            {
                using var request = requestFactory();
                response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!IsLastAttempt(attempt) && ShouldRetry(response, out delayHint))
                {
                    response.Dispose();
                }
                else
                {
                    return response;
                }
            }
            catch (HttpRequestException) when (!IsLastAttempt(attempt))
            {
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken != cancellationToken && !IsLastAttempt(attempt))
            {
            }

            await Task.Delay(delayHint is { } retryDelay && retryDelay > TimeSpan.Zero ? retryDelay : defaultDelay, cancellationToken).ConfigureAwait(false);
        }

        static bool IsLastAttempt(int attempt) => attempt >= MaxRetries;
    }

    private static HttpClient CreateHttpClient()
    {
        var socketHandler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        };

        return new HttpClient(socketHandler, disposeHandler: true);
    }

    private static bool ShouldRetry(HttpResponseMessage response, out TimeSpan? delay)
    {
        delay = GetRetryDelay(response);
        return (int)response.StatusCode >= 500
            || response.StatusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429
            || (response.StatusCode == HttpStatusCode.Forbidden && delay is not null);
    }

    private static TimeSpan? GetRetryDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            return retryAfter switch
            {
                { Date: { } date } => date - DateTimeOffset.UtcNow,
                { Delta: { } delta } => delta,
                _ => null,
            };
        }

        if (response.StatusCode == HttpStatusCode.Forbidden
            && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues)
            && remainingValues.Any(static value => value == "0")
            && response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var resetUnixTimeSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(resetUnixTimeSeconds) - DateTimeOffset.UtcNow;
        }

        return null;
    }
}

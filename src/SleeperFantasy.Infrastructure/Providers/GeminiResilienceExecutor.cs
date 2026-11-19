using System.Net;
using SleeperFantasy.Infrastructure.Configuration;

namespace SleeperFantasy.Infrastructure.Providers;

internal sealed class GeminiResilienceExecutor(ProviderOptions settings)
{
    public async Task<HttpResponseMessage> ExecuteAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.LanguageModelTimeoutSeconds));

            try
            {
                var response = await send(timeout.Token);
                if (!IsRetryable(response.StatusCode) || attempt >= settings.LanguageModelMaxRetries)
                    return response;

                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < settings.LanguageModelMaxRetries)
            {
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                && attempt < settings.LanguageModelMaxRetries)
            {
            }

            var delay = Math.Max(0, settings.LanguageModelRetryDelayMilliseconds) * (attempt + 1);
            await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
        }
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}

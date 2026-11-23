using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Platform.Aggregator.Resilience;

public static class ResilienceExtensions
{
    /// <summary>
    /// Замінює стандартний обробник з ServiceDefaults (ConfigureHttpClientDefaults) власним для цього клієнта:
    /// спершу RemoveAllResilienceHandlers — щоб обробники НЕ нашаровувались, — потім AddStandardResilienceHandler
    /// з параметрами профілю. Порядок стратегій лишається стандартним: rate limiter → загальний таймаут → retry →
    /// circuit breaker → таймаут спроби.
    /// </summary>
    /// <param name="client">HTTP- або gRPC-клієнт.</param>
    /// <param name="profile">Профіль критичності сервісу.</param>
    /// <param name="idempotentPost">true для gRPC-клієнтів агрегатора: gRPC надсилає КОЖЕН виклик як HTTP POST, але всі RPC
    /// агрегатора — читання (ідемпотентні), тож їх безпечно повторювати. Для REST-клієнтів POST/PUT/DELETE не повторюються.</param>
    public static IHttpClientBuilder AddPlatformResilience(this IHttpClientBuilder client, ResilienceProfile profile, bool idempotentPost = false)
    {
        client.RemoveAllResilienceHandlers();
        client.AddStandardResilienceHandler().Configure((o, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger($"Platform.Aggregator.Resilience.{profile.Name}");

            // ---------- Retry: експоненційна затримка з jitter (без thundering herd), лише ідемпотентні методи ----------
            o.Retry.MaxRetryAttempts = profile.MaxRetryAttempts;
            o.Retry.Delay = profile.RetryDelay;
            o.Retry.BackoffType = DelayBackoffType.Exponential;
            o.Retry.UseJitter = true;
            if (!idempotentPost)
                o.Retry.DisableForUnsafeHttpMethods();   // REST: POST/PUT/PATCH/DELETE не повторюються (інакше — подвійні зміни)

            // ---------- Таймаути: спроба й загальний (з урахуванням повторів) ----------
            o.AttemptTimeout.Timeout = profile.AttemptTimeout;
            o.TotalRequestTimeout.Timeout = profile.TotalTimeout;

            // ---------- Circuit breaker: fail-fast під час систематичних збоїв ----------
            o.CircuitBreaker.FailureRatio = profile.FailureRatio;
            o.CircuitBreaker.MinimumThroughput = profile.MinimumThroughput;
            o.CircuitBreaker.SamplingDuration = profile.SamplingDuration;
            o.CircuitBreaker.BreakDuration = profile.BreakDuration;
            o.CircuitBreaker.OnOpened = args =>
            {
                logger.LogError("Circuit OPENED for {ResilienceProfile} on {BreakDurationSeconds} s: {Outcome}",
                    profile.Name, args.BreakDuration.TotalSeconds, Describe(args.Outcome));
                return ValueTask.CompletedTask;
            };
            o.CircuitBreaker.OnHalfOpened = _ =>
            {
                logger.LogWarning("Circuit HALF-OPEN for {ResilienceProfile}: пропускаємо пробний запит", profile.Name);
                return ValueTask.CompletedTask;
            };
            o.CircuitBreaker.OnClosed = _ =>
            {
                logger.LogInformation("Circuit CLOSED for {ResilienceProfile}: сервіс знову відповідає", profile.Name);
                return ValueTask.CompletedTask;
            };
            o.Retry.OnRetry = args =>
            {
                logger.LogWarning("Retry {AttemptNumber} for {ResilienceProfile} after {RetryDelayMs} ms: {Outcome}",
                    args.AttemptNumber + 1, profile.Name, args.RetryDelay.TotalMilliseconds, Describe(args.Outcome));
                return ValueTask.CompletedTask;
            };
        });
        return client;
    }

    /// <summary>
    /// Retry-політика gRPC-каналу за gRPC-статусами. HTTP-обробник їх не бачить: gRPC повертає помилку статусом
    /// у трейлерах відповіді з HTTP 200. Повторюються лише тимчасові статуси (UNAVAILABLE, RESOURCE_EXHAUSTED);
    /// усі RPC агрегатора — читання, тобто ідемпотентні. Загальну межу задає deadline виклику.
    /// </summary>
    public static IHttpClientBuilder AddGrpcRetryPolicy(this IHttpClientBuilder client, ResilienceProfile profile) =>
        client.ConfigureChannel(channel => channel.ServiceConfig = new ServiceConfig
        {
            MethodConfigs =
            {
                new MethodConfig
                {
                    Names = { MethodName.Default },
                    RetryPolicy = new RetryPolicy
                    {
                        MaxAttempts = profile.GrpcMaxAttempts,
                        InitialBackoff = profile.GrpcInitialBackoff,
                        MaxBackoff = TimeSpan.FromSeconds(2),
                        BackoffMultiplier = 2,
                        RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.ResourceExhausted }
                    }
                }
            }
        });

    private static string Describe(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is { } ex ? $"{ex.GetType().Name}: {ex.Message}" : $"HTTP {(int?)outcome.Result?.StatusCode}";
}

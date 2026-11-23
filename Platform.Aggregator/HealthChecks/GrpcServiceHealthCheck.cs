using Grpc.Core;
using Grpc.Health.V1;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Aggregator.HealthChecks;

/// <summary>
/// Перевірка gRPC-залежності агрегатора за стандартним gRPC Health Checking Protocol:
/// виклик Health.Check з коротким deadline (2 с). Сервіс сам відповідає на основі своїх ASP.NET Core health checks
/// (AddGrpcHealthChecks + MapGrpcHealthChecksService). Перевірка ідемпотентна й без побічних ефектів.
/// RpcException → статус збою реєстрації (Unhealthy для критичних сервісів, Degraded для некритичних).
/// </summary>
public sealed class GrpcServiceHealthCheck(Health.HealthClient client, string service, ILogger<GrpcServiceHealthCheck> logger)
    : IHealthCheck
{
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var reply = await client.CheckAsync(new HealthCheckRequest(),
                deadline: DateTime.UtcNow.Add(Deadline), cancellationToken: ct);

            return reply.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy($"{service} is serving")
                : new HealthCheckResult(context.Registration.FailureStatus, $"{service} status: {reply.Status}");
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "{Service:l} gRPC health check failed with {GrpcStatus}", service, ex.StatusCode);
            return new HealthCheckResult(context.Registration.FailureStatus, $"{service} is unavailable ({ex.StatusCode})", ex);
        }
    }
}

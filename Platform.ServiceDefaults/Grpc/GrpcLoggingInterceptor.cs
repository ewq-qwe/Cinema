using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace Platform.ServiceDefaults.Grpc;

/// <summary>
/// Серверний interceptor для всіх gRPC-сервісів платформи: метод, peer (адреса клієнта), статус і тривалість —
/// структурованими властивостями. Помилки логуються зі статусом gRPC і stack trace.
/// CorrelationId потрапляє в ці записи з LogContext (CorrelationIdMiddleware читає його з метаданих = заголовків HTTP/2).
/// </summary>
public sealed class GrpcLoggingInterceptor(ILogger<GrpcLoggingInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        Activity.Current?.SetTag("rpc.grpc.peer", context.Peer);
        try
        {
            var response = await continuation(request, context);
            logger.LogInformation("gRPC {GrpcMethod:l} from {GrpcPeer:l}: {GrpcStatus} in {ElapsedMs} ms",
                context.Method, context.Peer, StatusCode.OK, sw.ElapsedMilliseconds);
            return response;
        }
        catch (RpcException ex)
        {
            // Очікувані відмови (NotFound, InvalidArgument) — Warning; збої сервера — Error
            var level = ex.StatusCode is StatusCode.Internal or StatusCode.Unknown or StatusCode.DataLoss
                ? LogLevel.Error
                : LogLevel.Warning;
            logger.Log(level, ex, "gRPC {GrpcMethod:l} from {GrpcPeer:l}: {GrpcStatus} in {ElapsedMs} ms — {GrpcDetail}",
                context.Method, context.Peer, ex.StatusCode, sw.ElapsedMilliseconds, ex.Status.Detail);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "gRPC {GrpcMethod:l} from {GrpcPeer:l}: unhandled {ExceptionType} in {ElapsedMs} ms",
                context.Method, context.Peer, ex.GetType().Name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}

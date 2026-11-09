using System.Diagnostics;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

namespace Platform.ApiGateway.Observability;

/// <summary>
/// Логує рішення про маршрутизацію: маршрут, кластер (логічне ім'я сервісу), реальну адресу від service discovery,
/// статус, тривалість і розмір відповіді. Помилки проксування (сервіс недоступний, таймаут) — окремо, з деталями.
/// CorrelationId уже в LogContext (CorrelationIdMiddleware стоїть перед цим middleware).
/// </summary>
public sealed class GatewayLoggingMiddleware(RequestDelegate next, GatewayMetrics metrics, ILogger<GatewayLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var originalBody = context.Response.Body;
        await using var counting = new CountingStream(originalBody);
        context.Response.Body = counting;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
            sw.Stop();
            Log(context, counting.BytesWritten, sw.Elapsed.TotalMilliseconds);
        }
    }

    private void Log(HttpContext context, long bytes, double elapsedMs)
    {
        var proxy = context.Features.Get<IReverseProxyFeature>();
        if (proxy is null) return;   // не проксований запит (/health, /) — його меж достатньо в CorrelationIdMiddleware

        var route = proxy.Route.Config.RouteId;
        var cluster = proxy.Cluster.Config.ClusterId;
        var destination = proxy.ProxiedDestination?.Model.Config.Address ?? "-";
        var status = context.Response.StatusCode;

        metrics.Record(route, cluster, status, elapsedMs);

        var error = context.Features.Get<IForwarderErrorFeature>();
        if (error is not null && error.Error != ForwarderError.None)
        {
            metrics.RecordProxyError(route, cluster, error.Error.ToString());
            logger.LogError(error.Exception,
                "Gateway: {Method:l} {Path:l} -> route {Route:l} / cluster {Cluster:l} ({Destination:l}) FAILED: {ProxyError}, status {StatusCode} in {ElapsedMs:0} ms",
                context.Request.Method, context.Request.Path, route, cluster, destination, error.Error, status, elapsedMs);
            return;
        }

        logger.LogInformation(
            "Gateway: {Method:l} {Path:l}{Query:l} -> route {Route:l} / cluster {Cluster:l} ({Destination:l}): {StatusCode} in {ElapsedMs:0} ms, {ResponseBytes} bytes",
            context.Request.Method, context.Request.Path, context.Request.QueryString, route, cluster, destination,
            status, elapsedMs, bytes);
    }

    /// <summary>Рахує байти тіла відповіді (Content-Length є не завжди — напр., chunked).</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await inner.WriteAsync(buffer, ct);
            BytesWritten += buffer.Length;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        // Зовнішній потік належить Kestrel — не закриваємо його
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Platform.ServiceDefaults.Correlation;
using Platform.ServiceDefaults.Grpc;
using Platform.ServiceDefaults.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Наскрізні налаштування, однакові для всіх сервісів платформи (як eShop.ServiceDefaults).
/// У сервісі: <c>builder.AddServiceDefaults()</c> на початку, <c>app.UseMiddleware&lt;CorrelationIdMiddleware&gt;()</c>
/// одразу після Build і <c>app.MapDefaultEndpoints()</c>.
/// </summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>Джерело власних spans MongoDB-драйвера (підписка — DiagnosticsActivityEventSubscriber у сервісі відгуків).</summary>
    public const string MongoActivitySource = "MongoDB.Driver.Core.Extensions.DiagnosticSources";

    /// <summary>ActivitySource бізнес-шарів сервісів: Booking.Bll, Catalog.Bll, Reviews.Application.</summary>
    private static readonly string[] BusinessActivitySources = ["Booking.Bll", "Catalog.Bll", "Reviews.Application"];

    /// <summary>Meter-и кешу (Лаб. 06): cache.hits / cache.misses з тегом cache.level (L1 | L2).</summary>
    private static readonly string[] CacheMeters = ["Catalog.Cache", "Reviews.Cache", "Platform.Aggregator.Cache"];

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureSerilog();
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationIdHandler>();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // retry, timeout, circuit breaker (детально — Лаб. 07); таймаути сервіс може звузити в секції HttpResilience
            http.AddStandardResilienceHandler(o => builder.Configuration.GetSection("HttpResilience").Bind(o));
            http.AddServiceDiscovery();                         // http://catalog-api → реальна адреса від Aspire
            http.AddHttpMessageHandler<CorrelationIdHandler>(); // CorrelationId у кожен вихідний запит
        });

        return builder;
    }

    /// <summary>
    /// Serilog — єдиний провайдер логування. Кожен запис — об'єкт з полями: ServiceName, Environment,
    /// MachineName, ThreadId, CorrelationId (LogContext), TraceId / SpanId.
    /// writeToProviders: true передає записи далі провайдеру OpenTelemetry → вкладка Structured logs у Dashboard.
    /// </summary>
    public static TBuilder ConfigureSerilog<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? builder.Environment.ApplicationName;

        builder.Logging.ClearProviders(); // інакше стандартний Console-провайдер дублював би кожен рядок

        builder.Services.AddSerilog((services, cfg) => cfg
            .MinimumLevel.Is(builder.Environment.IsDevelopment() ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("Polly", LogEventLevel.Warning)
            .MinimumLevel.Override("MongoDB", LogEventLevel.Warning)
            .MinimumLevel.Override("Yarp", LogEventLevel.Information)
            .MinimumLevel.Override("Grpc", LogEventLevel.Warning)                 // внутрішні Debug-логи gRPC на кожне повідомлення; свої логи — в interceptor
            .ReadFrom.Configuration(builder.Configuration)   // точкові перевизначення рівнів з appsettings сервісу
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.With<TraceContextEnricher>()
            .WriteTo.Console(new CompactJsonFormatter())     // JSON у вкладці Console logs
            .WriteTo.File(new CompactJsonFormatter(), $"logs/{serviceName}-.json",
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7),
            writeToProviders: true);

        return builder;
    }

    /// <summary>
    /// OpenTelemetry: spans вхідних запитів (ASP.NET Core), вихідних викликів (HttpClient), SQL (SqlClient — і Dapper,
    /// і EF Core) та MongoDB; метрики; експорт OTLP в Aspire Dashboard (батчево, не блокує запити).
    /// </summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var serviceName = builder.Environment.ApplicationName;

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddAttributes(
            [
                new("deployment.environment.name", builder.Environment.EnvironmentName)
            ]))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(serviceName)                               // власні метрики сервісу (напр., Gateway)
                .AddMeter(CacheMeters))                              // метрики кешу
            .WithTracing(t => t
                .SetSampler(CreateSampler(builder))
                .AddSource(serviceName)                              // власні spans застосунку (напр., Platform.Aggregator)
                .AddSource(BusinessActivitySources)                  // spans бізнес-операцій сервісів
                .AddSource(MongoActivitySource)                      // MongoDB (Лаб. 04)
                .AddSource("Grpc.Net.Client")                        // клієнтський span gRPC-виклику (Лаб. 06): батько для HTTP/2-запиту
                .AddAspNetCoreInstrumentation(o =>
                    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments(HealthEndpointPath)
                                      && !ctx.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation());                     // Dapper (Лаб. 02) та EF Core (Лаб. 03)

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();  // адресу задає Aspire

        return builder;
    }

    /// <summary>100 % трасувань у Development; у інших середовищах — частка з OpenTelemetry:SamplingRatio (за замовчуванням 10 %).</summary>
    private static Sampler CreateSampler(IHostApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment()) return new AlwaysOnSampler();
        var ratio = builder.Configuration.GetValue("OpenTelemetry:SamplingRatio", 0.1);
        return new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio));
    }

    /// <summary>
    /// gRPC-сервер з однаковим для всіх сервісів interceptor-ом логування (метод, peer, статус, тривалість).
    /// Spans серверних викликів (rpc.service, rpc.method) додає інструментація ASP.NET Core.
    /// </summary>
    public static IServiceCollection AddDefaultGrpc(this IServiceCollection services, bool detailedErrors = false)
    {
        services.AddGrpc(o =>
        {
            o.Interceptors.Add<GrpcLoggingInterceptor>();
            o.EnableDetailedErrors = detailedErrors;
        });
        return services;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    /// <summary>/health — усі перевірки (у т. ч. бази даних); /alive — лише «процес живий».</summary>
    /// <summary>JSON-звіт /health: статус кожної перевірки та її дані (напр., hit ratio кешу L1 / L2).</summary>
    private static Task WriteHealthJson(Microsoft.AspNetCore.Http.HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.ToDictionary(e => e.Key, e => new
            {
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                data = e.Value.Data.Count == 0 ? null : e.Value.Data
            })
        }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions { ResponseWriter = WriteHealthJson });
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        return app;
    }
}

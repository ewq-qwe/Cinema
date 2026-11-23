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
    public const string LivenessEndpointPath = "/health/live";
    public const string ReadinessEndpointPath = "/health/ready";

    /// <summary>Джерело власних spans MongoDB-драйвера (підписка — DiagnosticsActivityEventSubscriber у сервісі відгуків).</summary>
    public const string MongoActivitySource = "MongoDB.Driver.Core.Extensions.DiagnosticSources";

    /// <summary>ActivitySource бізнес-шарів сервісів: Booking.Bll, Catalog.Bll, Reviews.Application.</summary>
    private static readonly string[] BusinessActivitySources = ["Booking.Bll", "Catalog.Bll", "Reviews.Application"];

    /// <summary>Meter-и кешу (Лаб. 06): cache.hits / cache.misses з тегом cache.level (L1 | L2).</summary>
    private static readonly string[] CacheMeters = ["Catalog.Cache", "Reviews.Cache", "Platform.Aggregator.Cache"];

    /// <summary>
    /// Лаб. 07: бізнес-метрики сервісів (Meter = назва сервісу) і телеметрія resilience — Meter "Polly"
    /// (resilience.polly.strategy.events з event.name = OnRetry / OnCircuitOpened / OnTimeout тощо).
    /// </summary>
    private static readonly string[] BusinessMeters = ["Booking.Api", "Catalog.Api", "Reviews.WebApi", "Platform.Aggregator", "Polly"];

    /// <summary>
    /// Лаб. 08: шина подій (Platform.EventBus) — ActivitySource spans публікації (Producer) і обробки (Consumer),
    /// відновлених із заголовка traceparent, та Meter (messaging.published / consumed / dlq.depth).
    /// </summary>
    private const string MessagingSource = "Platform.Messaging";

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
            .MinimumLevel.Override("RabbitMQ", LogEventLevel.Warning)             // внутрішні логи клієнта RabbitMQ; рішення щодо повідомлень логує Platform.EventBus
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
                .AddMeter(CacheMeters)                               // метрики кешу (Лаб. 06)
                .AddMeter(BusinessMeters)                            // бізнес-метрики й Polly (Лаб. 07)
                .AddMeter(MessagingSource))                          // метрики шини подій (Лаб. 08)
            .WithTracing(t => t
                .SetSampler(CreateSampler(builder))
                .AddSource(serviceName)                              // власні spans застосунку (напр., Platform.Aggregator)
                .AddSource(BusinessActivitySources)                  // spans бізнес-операцій сервісів
                .AddSource(MongoActivitySource)                      // MongoDB (Лаб. 04)
                .AddSource("Grpc.Net.Client")                        // клієнтський span gRPC-виклику (Лаб. 06): батько для HTTP/2-запиту
                .AddSource(MessagingSource)                          // публікація й обробка подій через RabbitMQ (Лаб. 08)
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

    /// <summary>Перевірка «процес живий» — без зовнішніх залежностей, тег live (liveness).</summary>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("Процес відповідає."), [HealthTags.Live]);
        return builder;
    }

    /// <summary>JSON-звіт: статус кожної перевірки, її теги, тривалість, дані й причину збою.</summary>
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
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                tags = e.Value.Tags,
                error = e.Value.Exception?.Message,
                data = e.Value.Data.Count == 0 ? null : e.Value.Data
            })
        }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        }));
    }

    /// <summary>
    /// Endpoints стану (Лаб. 07). Публікуються в усіх середовищах — їх використовують оркестратор (Kubernetes, Лаб. 10)
    /// і Aspire (WithHttpHealthCheck):
    /// <list type="bullet">
    /// <item><c>/health/live</c> — liveness: лише перевірки з тегом live (процес живий), без зовнішніх залежностей; збій → перезапуск;</item>
    /// <item><c>/health/ready</c> — readiness: перевірки з тегом ready (БД, Redis, gRPC-залежності); збій → сервіс виключають з балансування;</item>
    /// <item><c>/health</c> — усі перевірки (разом зі статистикою кешу) для людини; <c>/alive</c> — сумісність із шаблоном Aspire.</item>
    /// </list>
    /// Degraded (напр., недоступний Redis) відповідає 200: сервіс працює далі, лише повільніше.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(LivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(HealthTags.Live),
            ResponseWriter = WriteHealthJson
        });
        app.MapHealthChecks(ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(HealthTags.Ready),
            ResponseWriter = WriteHealthJson
        });
        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions { ResponseWriter = WriteHealthJson });
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains(HealthTags.Live) });
        return app;
    }
}

/// <summary>Єдині теги health checks для всіх сервісів платформи.</summary>
public static class HealthTags
{
    /// <summary>Liveness: процес живий (без зовнішніх залежностей).</summary>
    public const string Live = "live";

    /// <summary>Readiness: залежності, без яких сервіс не може обслуговувати трафік (або обслуговує в режимі Degraded).</summary>
    public const string Ready = "ready";

    public const string Database = "database";
    public const string Cache = "cache";
    public const string Grpc = "grpc";
    public const string Downstream = "downstream";
}

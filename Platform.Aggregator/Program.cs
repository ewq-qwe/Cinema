using System.Reflection;
using System.Text.Json.Serialization;
using Platform.Aggregator.Caching;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.Aggregator.Clients;
using Platform.Aggregator.HealthChecks;
using Platform.Aggregator.Metrics;
using Platform.Aggregator.Resilience;
using Platform.Aggregator.Services;
using Platform.ServiceDefaults.Correlation;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, service discovery, resilience + CorrelationIdHandler для HttpClient

// ---------- Лаб. 06: типізовані gRPC-клієнти (згенеровані з .proto сервісів) ----------
// "_grpc." — іменований endpoint ресурсу AppHost з HTTP/2 (h2c); решту адреси підставляє service discovery.
// Фабрика gRPC-клієнтів будує їх на IHttpClientFactory, тож діють налаштування ServiceDefaults:
// service discovery, resilience і CorrelationIdHandler (X-Correlation-Id → метадані gRPC-виклику).
// Лаб. 07: для кожного клієнта стандартний обробник з ServiceDefaults ЗАМІНЕНО (RemoveAllResilienceHandlers → власний)
// за профілем критичності сервісу: Каталог — критичний, Бронювання — основний, Відгуки — некритичні (README, Лаб. 07).
// gRPC-клієнти отримують ще й retry-політику каналу за gRPC-статусами (UNAVAILABLE тощо): HTTP-обробник їх не бачить.
builder.Services.AddGrpcClient<Booking.Grpc.BookingService.BookingServiceClient>(o => o.Address = new("http://_grpc.booking-api"))
    .AddPlatformResilience(ResilienceProfile.Standard, idempotentPost: true).AddGrpcRetryPolicy(ResilienceProfile.Standard);
builder.Services.AddGrpcClient<Catalog.Grpc.CatalogService.CatalogServiceClient>(o => o.Address = new("http://_grpc.catalog-api"))
    .AddPlatformResilience(ResilienceProfile.Critical, idempotentPost: true).AddGrpcRetryPolicy(ResilienceProfile.Critical);
builder.Services.AddGrpcClient<Reviews.Grpc.ReviewsService.ReviewsServiceClient>(o => o.Address = new("http://_grpc.reviews-api"))
    .AddPlatformResilience(ResilienceProfile.NonCritical, idempotentPost: true).AddGrpcRetryPolicy(ResilienceProfile.NonCritical);

// ---------- Typed HttpClients (Лаб. 05) — лишено для порівняння затримок HTTP і gRPC (?transport=http) ----------
builder.Services.AddHttpClient<BookingClient>(c => c.BaseAddress = new("https+http://booking-api"))
    .AddPlatformResilience(ResilienceProfile.Standard);
builder.Services.AddHttpClient<CatalogClient>(c => c.BaseAddress = new("https+http://catalog-api"))
    .AddPlatformResilience(ResilienceProfile.Critical);
builder.Services.AddHttpClient<ReviewsClient>(c => c.BaseAddress = new("https+http://reviews-api"))
    .AddPlatformResilience(ResilienceProfile.NonCritical);

// ---------- Лаб. 07: бізнес-метрики агрегатора ----------
builder.Services.AddSingleton<AggregatorMetrics>();

// ---------- Лаб. 07: health checks залежностей (readiness) ----------
// gRPC Health Checking Protocol: окремий клієнт Health на кожен сервіс, без повторів — health check має бути одним швидким пробним викликом
foreach (var (resource, _, _) in GrpcDependencies())
    builder.Services.AddGrpcClient<Health.HealthClient>($"{resource}-health", o => o.Address = new($"http://_grpc.{resource}"))
        .RemoveAllResilienceHandlers();

var health = builder.Services.AddHealthChecks();
foreach (var (resource, failureStatus, why) in GrpcDependencies())
    health.Add(new HealthCheckRegistration($"{resource}-grpc",
        sp => new GrpcServiceHealthCheck(
            sp.GetRequiredService<GrpcClientFactory>().CreateClient<Health.HealthClient>($"{resource}-health"),
            resource, sp.GetRequiredService<ILogger<GrpcServiceHealthCheck>>()),
        failureStatus, [HealthTags.Grpc, HealthTags.Downstream, HealthTags.Ready], GrpcServiceHealthCheck.Deadline + TimeSpan.FromSeconds(1)));

// Redis — необов'язкова залежність (кеш агрегатів): недоступність → Degraded, а не Unhealthy
health.AddRedis(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("redis")!,
    name: "redis-cache", failureStatus: HealthStatus.Degraded,
    tags: [HealthTags.Cache, "redis", HealthTags.Ready], timeout: TimeSpan.FromSeconds(3));

builder.Services.AddScoped<GrpcPlatformSources>();
builder.Services.AddScoped<HttpPlatformSources>();
builder.Services.AddKeyedScoped<IAggregationService>(Transports.Grpc, (sp, _) =>
    new AggregationService(sp.GetRequiredService<GrpcPlatformSources>(), sp.GetRequiredService<ILogger<AggregationService>>()));
builder.Services.AddKeyedScoped<IAggregationService>(Transports.Http, (sp, _) =>
    new AggregationService(sp.GetRequiredService<HttpPlatformSources>(), sp.GetRequiredService<ILogger<AggregationService>>()));

// ---------- Лаб. 06: кеш агрегованих відповідей у Redis (ресурс "redis"), TTL 30 с ----------
builder.AddRedisDistributedCache("redis",
    // Кеш — не критична залежність: його відмова дає Degraded у CacheHealthCheck, а не Unhealthy всього сервісу
    settings => settings.DisableHealthChecks = true,
    // Короткі таймаути: недоступний Redis має швидко «відпускати» запит до L1 / БД, а не тримати його 5 с
    options => { options.AbortOnConnectFail = false; options.ConnectTimeout = 1000; options.SyncTimeout = 1000; options.AsyncTimeout = 1000; });
builder.Services.AddSingleton<AggregatedResponseCache>();

// ---------- JSON: camelCase, null-поля не серіалізуються ----------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new()
    {
        Title = "Cinema Aggregator API",
        Version = "v1.0",
        Description = "Aggregator Pattern (Лаб. 05): картки бронювання й фільму з даних booking-api, catalog-api і reviews-api"
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

// Залежності агрегатора та статус, з яким їхня відмова потрапляє в readiness:
// без Каталогу й Бронювання картки не зібрати → Unhealthy; без Відгуків — часткова відповідь → Degraded.
static IEnumerable<(string Resource, HealthStatus FailureStatus, string Why)> GrpcDependencies() =>
[
    ("catalog-api", HealthStatus.Unhealthy, "критичний"),
    ("booking-api", HealthStatus.Unhealthy, "основний ресурс картки бронювання"),
    ("reviews-api", HealthStatus.Degraded, "некритичний: часткова відповідь")
];

public partial class Program;

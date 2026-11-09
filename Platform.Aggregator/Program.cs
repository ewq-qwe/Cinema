using System.Reflection;
using System.Text.Json.Serialization;
using Platform.Aggregator.Caching;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using Platform.ServiceDefaults.Correlation;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, service discovery, resilience + CorrelationIdHandler для HttpClient

// ---------- Лаб. 06: типізовані gRPC-клієнти (згенеровані з .proto сервісів) ----------
// "_grpc." — іменований endpoint ресурсу AppHost з HTTP/2 (h2c); решту адреси підставляє service discovery.
// Фабрика gRPC-клієнтів будує їх на IHttpClientFactory, тож діють налаштування ServiceDefaults:
// service discovery, resilience і CorrelationIdHandler (X-Correlation-Id → метадані gRPC-виклику).
builder.Services.AddGrpcClient<Booking.Grpc.BookingService.BookingServiceClient>(o => o.Address = new("http://_grpc.booking-api"));
builder.Services.AddGrpcClient<Catalog.Grpc.CatalogService.CatalogServiceClient>(o => o.Address = new("http://_grpc.catalog-api"));
builder.Services.AddGrpcClient<Reviews.Grpc.ReviewsService.ReviewsServiceClient>(o => o.Address = new("http://_grpc.reviews-api"));

// ---------- Typed HttpClients (Лаб. 05) — лишено для порівняння затримок HTTP і gRPC (?transport=http) ----------
builder.Services.AddHttpClient<BookingClient>(c => c.BaseAddress = new("https+http://booking-api"));
builder.Services.AddHttpClient<CatalogClient>(c => c.BaseAddress = new("https+http://catalog-api"));
builder.Services.AddHttpClient<ReviewsClient>(c => c.BaseAddress = new("https+http://reviews-api"));

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

public partial class Program;

using System.Reflection;
using Reviews.Application;
using Reviews.Application.Common.Mapping;
using Reviews.Infrastructure;
using Reviews.WebApi.Errors;
using Reviews.WebApi.Grpc;
using Reviews.WebApi.HealthChecks;
using Reviews.WebApi.Metrics;
using Reviews.WebApi.Mapping;
using Platform.ServiceDefaults.Correlation;
using Grpc.AspNetCore.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// ---------- Лаб. 05: спільні налаштування платформи — Serilog, OpenTelemetry, health checks, service discovery ----------
builder.AddServiceDefaults();

// ---------- Шари Clean Architecture: кожен реєструє себе власним методом розширення ----------
builder.Services
    .AddApplication(builder.Configuration)      // MediatR + behaviors (Logging → Validation → Performance), валідатори
    .AddInfrastructure(builder.Configuration);  // MongoDB: мапінг, контекст, репозиторії, запити, індекси, сідінг

// ---------- Лаб. 06: L2-кеш — Redis через Aspire (ресурс "redis"); L1 і CachingBehavior реєструє AddApplication ----------
builder.AddRedisDistributedCache("redis",
    // Кеш — не критична залежність: його відмова дає Degraded у CacheHealthCheck, а не Unhealthy всього сервісу
    settings => settings.DisableHealthChecks = true,
    // Короткі таймаути: недоступний Redis має швидко «відпускати» запит до L1 / БД, а не тримати його 5 с
    options => { options.AbortOnConnectFail = false; options.ConnectTimeout = 1000; options.SyncTimeout = 1000; options.AsyncTimeout = 1000; });

// ---------- Лаб. 06: gRPC-сервер (interceptor логування — зі ServiceDefaults) ----------
builder.Services.AddDefaultGrpc(detailedErrors: builder.Environment.IsDevelopment());

// ---------- Лаб. 07: health checks — MongoDB (Unhealthy) і Redis (Degraded) у readiness + gRPC Health Checking Protocol ----------
builder.Services.AddReviewsHealthChecks();
// grpc.health.v1.Health (стандартний gRPC Health Checking Protocol) — його викликає Aggregator.
// Мапимо лише перевірки БД: gRPC-статус означає «можу обслуговувати запити»; відмова кешу (Degraded) не робить сервіс NOT_SERVING.
builder.Services.AddGrpcHealthChecks();
builder.Services.Configure<GrpcHealthChecksOptions>(o => o.Services.Map("", r => r.Tags.Contains(HealthTags.Database)));

// ---------- Лаб. 07: бізнес-метрики (Meter "Reviews.WebApi") ----------
builder.Services.AddSingleton<ReviewsMetrics>();

// ---------- AutoMapper: профілі Application (сутності → DTO) і WebApi (HTTP-моделі → команди) ----------
builder.Services.AddAutoMapper(cfg =>
{
    var licenseKey = builder.Configuration["LuckyPenny:LicenseKey"];
    if (!string.IsNullOrWhiteSpace(licenseKey)) cfg.LicenseKey = licenseKey;
    cfg.AddMaps(typeof(ReviewProfile).Assembly, typeof(RequestProfile).Assembly);
});

// ---------- Помилки → ProblemDetails (у т. ч. MongoDB-винятки) ----------
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<ExceptionToProblemDetailsHandler>();

builder.Services.AddControllers();

// ---------- Swagger ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new()
    {
        Title = "Cinema Reviews API",
        Version = "v1.0",
        Description = "Мікросервіс «Відгуки та обговорення» кінотеатру «Кадр» (Лаб. 04: Clean Architecture, CQRS + MediatR, MongoDB)"
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});

var app = builder.Build();

// ---------- Конвеєр ----------
app.UseMiddleware<CorrelationIdMiddleware>();          // CorrelationId у всіх логах запиту + межі запиту (ServiceDefaults)
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGrpcService<ReviewsGrpcService>();   // HTTP/2 (endpoint "grpc" в AppHost)
app.MapGrpcHealthChecksService();            // grpc.health.v1.Health/Check — його викликає Aggregator
app.MapDefaultEndpoints();   // /health/live, /health/ready, /health, /alive
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

public partial class Program;

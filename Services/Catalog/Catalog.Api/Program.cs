using System.Reflection;
using Catalog.Api.Errors;
using Catalog.Api.Grpc;
using Catalog.Api.HealthChecks;
using Catalog.Api.IntegrationEvents;
using Catalog.Api.Metrics;
using Catalog.Bll.Caching;
using Catalog.Bll.IntegrationEvents;
using Platform.EventBus;
using Catalog.Bll.Mapping;
using Catalog.Bll.Services;
using Catalog.Bll.Validators;
using Catalog.Dal;
using Catalog.Dal.Abstractions;
using Catalog.Dal.Seeding;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.ServiceDefaults.Correlation;
using Grpc.AspNetCore.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// ---------- Лаб. 05: спільні налаштування платформи — Serilog, OpenTelemetry, health checks, service discovery ----------
// Рівень логів SQL-запитів EF Core перевизначено в appsettings.json (Serilog:MinimumLevel:Override)
builder.AddServiceDefaults();

// ---------- EF Core: DbContext у Catalog.Dal, рядок підключення "CatalogDb" — тут (Aspire перевизначає його) ----------
var connectionString = builder.Configuration.GetConnectionString("CatalogDb")
    ?? throw new InvalidOperationException("Connection string 'CatalogDb' is missing");

builder.Services.AddDbContext<CatalogDbContext>(o => o
    .UseSqlServer(connectionString)
    // Опис фільму (1:1) прив'язаний до фільму з фільтром архівних — це свідомо, попередження зайве
    .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

// ---------- Лаб. 06: дворівневий кеш — L1 IMemoryCache (обмежений за розміром) + L2 Redis (Aspire, ресурс "redis") ----------
builder.Services.AddMemoryCache(o =>
{
    o.SizeLimit = builder.Configuration.GetValue("Caching:MemorySizeLimitKb", 20_000); // одиниці Size = КБ значення
    o.CompactionPercentage = 0.25;                                                     // при переповненні витісняється чверть
    o.TrackStatistics = true;                                                          // Лаб. 07: gauges розміру L1
});
// IDistributedCache поверх Redis; рядок підключення ConnectionStrings:redis
builder.AddRedisDistributedCache("redis",
    // Кеш — не критична залежність: його відмова дає Degraded у CacheHealthCheck, а не Unhealthy всього сервісу
    settings => settings.DisableHealthChecks = true,
    // Короткі таймаути: недоступний Redis має швидко «відпускати» запит до L1 / БД, а не тримати його 5 с
    options => { options.AbortOnConnectFail = false; options.ConnectTimeout = 1000; options.SyncTimeout = 1000; options.AsyncTimeout = 1000; });
builder.Services.AddSingleton<CacheMetrics>();
builder.Services.AddSingleton<ITwoLevelCache, TwoLevelCache>();
// ---------- Лаб. 07: health checks — SQL Server (Unhealthy) і Redis (Degraded) у readiness + статистика кешу ----------
builder.Services.AddCatalogHealthChecks(builder.Configuration);
// grpc.health.v1.Health (стандартний gRPC Health Checking Protocol) — його викликає Aggregator.
// Мапимо лише перевірки БД: gRPC-статус означає «можу обслуговувати запити»; відмова кешу (Degraded) не робить сервіс NOT_SERVING.
builder.Services.AddGrpcHealthChecks();
builder.Services.Configure<GrpcHealthChecksOptions>(o => o.Services.Map("", r => r.Tags.Contains(HealthTags.Database)));

// ---------- Лаб. 07: бізнес-метрики (Meter "Catalog.Api") ----------
builder.Services.AddSingleton<CatalogMetrics>();

// ---------- Лаб. 06: gRPC-сервер (interceptor логування — зі ServiceDefaults) ----------
builder.Services.AddDefaultGrpc(detailedErrors: builder.Environment.IsDevelopment());

// ---------- Лаб. 08: публікація integration events через RabbitMQ (transactional outbox) ----------
// IConnection (singleton) + health check від Aspire; ресурс "messaging" в AppHost. Вбудоване трасування клієнта вимкнено:
// контекст (traceparent) передається вручну в Platform.EventBus — так видно, як він переходить через брокер.
builder.AddRabbitMQClient(EventBusExtensions.ConnectionName, settings => settings.DisableTracing = true);
builder.Services.AddEventBus("catalog-api");                                         // IEventPublisher, метрики, CorrelationId
builder.Services.AddScoped<ICatalogIntegrationEventService, CatalogIntegrationEventService>();
builder.Services.AddHostedService<OutboxPublisherService>();                         // дошле події, що лишилися в outbox

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IMovieService, MovieService>();
builder.Services.AddScoped<IGenreService, GenreService>();
builder.Services.AddScoped<IPersonService, PersonService>();

// ---------- AutoMapper: профілі з BLL — централізовано ----------
builder.Services.AddAutoMapper(cfg =>
{
    var licenseKey = builder.Configuration["AutoMapper:LicenseKey"];
    if (!string.IsNullOrWhiteSpace(licenseKey)) cfg.LicenseKey = licenseKey;
    cfg.AddMaps(typeof(MovieProfile).Assembly);
});

// ---------- FluentValidation: валідатори з BLL; викликаються ЯВНО в контролерах (без застарілого AddFluentValidation) ----------
builder.Services.AddValidatorsFromAssemblyContaining<CreateMovieDtoValidator>();

// ---------- Помилки → ProblemDetails ----------
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ExceptionToProblemDetailsHandler>();

builder.Services.AddControllers();

// ---------- Swagger ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new()
    {
        Title = "Cinema Catalog API",
        Version = "v1.0",
        Description = "Мікросервіс «Каталог фільмів» кінотеатру «Кадр» (Лаб. 03: EF Core Code-First, Repository / Unit of Work, специфікації)"
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});

var app = builder.Build();

// ---------- Міграції + ідемпотентний сідінг (можна вимкнути; у Production сідінг не виконується) ----------
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
        await db.Database.MigrateAsync();
    if (!app.Environment.IsProduction() && app.Configuration.GetValue("Database:SeedOnStartup", true))
        await DbInitializer.SeedAsync(db, logger);
}

// ---------- Конвеєр ----------
app.UseMiddleware<CorrelationIdMiddleware>();          // CorrelationId у всіх логах запиту + межі запиту (ServiceDefaults)
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGrpcService<CatalogGrpcService>();   // HTTP/2 (endpoint "grpc" в AppHost)
app.MapGrpcHealthChecksService();            // grpc.health.v1.Health/Check — його викликає Aggregator
app.MapDefaultEndpoints();   // /health/live, /health/ready, /health, /alive
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

public partial class Program;

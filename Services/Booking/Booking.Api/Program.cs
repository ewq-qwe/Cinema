using System.Reflection;
using Booking.Api.Errors;
using Booking.Api.Grpc;
using Booking.Api.HealthChecks;
using Booking.Api.IntegrationEvents;
using Booking.Api.Metrics;
using Booking.Bll.Mapping;
using Booking.Bll.Services;
using Booking.Dal;
using Booking.Dal.Abstractions;
using Booking.Dal.Migrations;
using Platform.EventBus;
using Platform.EventBus.Events;
using Platform.ServiceDefaults.Correlation;
using Grpc.AspNetCore.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// ---------- Лаб. 05: спільні налаштування платформи — Serilog, OpenTelemetry, health checks, service discovery ----------
builder.AddServiceDefaults();

// ---------- Рядок підключення: appsettings.json → перевизначається ConnectionStrings__BookingDb ----------
// Під Aspire його передає AppHost (ресурс бази має те саме ім'я "BookingDb").
var connectionString = builder.Configuration.GetConnectionString("BookingDb")
    ?? throw new InvalidOperationException("Connection string 'BookingDb' is missing");

// ---------- DI ----------
// Unit of Work — Scoped: один екземпляр і одне з'єднання на HTTP-запит
builder.Services.AddScoped<IUnitOfWork>(_ => new UnitOfWork(connectionString));
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddSingleton(TimeProvider.System);

// AutoMapper: усі профілі з BLL реєструються централізовано
builder.Services.AddAutoMapper(cfg =>
{
    var licenseKey = builder.Configuration["AutoMapper:LicenseKey"];
    if (!string.IsNullOrWhiteSpace(licenseKey))
        cfg.LicenseKey = licenseKey;   // без ключа AutoMapper працює, лише попереджає в лог
    cfg.AddMaps(typeof(BookingProfile).Assembly);
});

// ---------- Помилки: доменні винятки → ProblemDetails ----------
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddControllers();

// ---------- Лаб. 06: gRPC-сервер для внутрішніх викликів (interceptor логування — зі ServiceDefaults) ----------
builder.Services.AddDefaultGrpc(detailedErrors: builder.Environment.IsDevelopment());

// ---------- Лаб. 07: health checks (SQL Server у readiness) + стандартний gRPC Health Checking Protocol ----------
builder.Services.AddBookingHealthChecks(builder.Configuration);
// grpc.health.v1.Health (стандартний gRPC Health Checking Protocol) — його викликає Aggregator.
// Мапимо лише перевірки БД: gRPC-статус означає «можу обслуговувати запити»; відмова кешу (Degraded) не робить сервіс NOT_SERVING.
builder.Services.AddGrpcHealthChecks();
builder.Services.Configure<GrpcHealthChecksOptions>(o => o.Services.Map("", r => r.Tags.Contains(HealthTags.Database)));

// ---------- Лаб. 07: бізнес-метрики (Meter "Booking.Api") ----------
builder.Services.AddSingleton<BookingMetrics>();
builder.Services.AddHostedService<BookingSnapshotService>();

// ---------- Лаб. 08: споживання подій Каталогу (RabbitMQ) — оновлення локальної копії довідника Movies ----------
// IConnection (singleton) + health check від Aspire; вбудоване трасування клієнта вимкнено — контекст відновлюється вручну
builder.AddRabbitMQClient(EventBusExtensions.ConnectionName, settings => settings.DisableTracing = true);
builder.Services.AddEventBus("booking-api");
builder.Services.AddEventHandler<MovieCreatedEvent, MovieCreatedHandler>();                           // окремий обробник
builder.Services.AddEventHandler<MovieRenamedEvent, MovieRenamedHandler>();                           // на кожен тип події,
builder.Services.AddEventHandler<MovieScreeningInfoChangedEvent, MovieScreeningInfoChangedHandler>(); // Scoped — свій UoW
builder.Services.AddEventHandler<MovieArchivedEvent, MovieArchivedHandler>();                         // на повідомлення
builder.Services.AddHostedService<CatalogEventsConsumer>();                                           // черга booking.catalog-sync

// ---------- OpenAPI / Swagger ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new()
    {
        Title = "Cinema Booking API",
        Version = "v1.0",
        Description = "Мікросервіс «Бронювання квитків» кінотеатру «Кадр» (Лаб. 02: DAL → BLL → API)"
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});

var app = builder.Build();

// ---------- Лаб. 08: міграції схеми після Лаб. 01 (dbo.ProcessedEvents, Movies.CatalogUpdatedAt / IsArchived) ----------
// Виконуються до старту споживача (hosted services стартують у app.Run), тож обробники вже бачать нові таблиці.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    var applied = await SchemaMigrator.MigrateAsync(connectionString);
    if (applied.Count > 0)
        app.Logger.LogInformation("Applied schema migrations {Migrations}", applied);
}

// CorrelationId + межі запиту — зовнішнім шаром, щоб у лог потрапляв фінальний статус (404/409), а не виняток як 500
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGrpcService<BookingGrpcService>();   // HTTP/2 (endpoint "grpc" в AppHost)
app.MapGrpcHealthChecksService();            // grpc.health.v1.Health/Check — його викликає Aggregator
app.MapDefaultEndpoints();   // /health/live, /health/ready, /health, /alive
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

// Для WebApplicationFactory у тестах
public partial class Program;

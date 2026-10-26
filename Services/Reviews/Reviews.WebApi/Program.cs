using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reviews.Application;
using Reviews.Application.Common.Mapping;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Persistence;
using Reviews.WebApi.Errors;
using Reviews.WebApi.Logging;
using Reviews.WebApi.Mapping;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Serilog: розділ "Serilog" в appsettings.json (Console + File, структурований JSON) ----------
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Reviews.WebApi"));

// ---------- Шари Clean Architecture: кожен реєструє себе власним методом розширення ----------
builder.Services
    .AddApplication(builder.Configuration)      // MediatR + behaviors (Logging → Validation → Performance), валідатори
    .AddInfrastructure(builder.Configuration);  // MongoDB: мапінг, контекст, репозиторії, запити, індекси, сідінг

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

// ---------- Health check підключення до MongoDB (детально — Лаб. 07) ----------
builder.Services.AddHealthChecks()
    .AddMongoDb(sp => sp.GetRequiredService<MongoDbContext>().Database,
        name: "mongodb", failureStatus: HealthStatus.Unhealthy, tags: ["ready"], timeout: TimeSpan.FromSeconds(3));

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
app.UseMiddleware<CorrelationIdMiddleware>();          // CorrelationId / RequestPath / UserId у всіх логах запиту
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

public partial class Program;

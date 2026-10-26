using System.Reflection;
using Catalog.Api.Errors;
using Catalog.Api.Logging;
using Catalog.Bll.Mapping;
using Catalog.Bll.Services;
using Catalog.Bll.Validators;
using Catalog.Dal;
using Catalog.Dal.Abstractions;
using Catalog.Dal.Seeding;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ---------- Serilog: налаштування — розділ "Serilog" в appsettings.json (sinks: Console + File) ----------
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information) // SQL-запити EF Core
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Catalog.Api"));

// ---------- EF Core: DbContext у Catalog.Dal, рядок підключення "CatalogDb" — тут (Aspire перевизначає його) ----------
var connectionString = builder.Configuration.GetConnectionString("CatalogDb")
    ?? throw new InvalidOperationException("Connection string 'CatalogDb' is missing");

builder.Services.AddDbContext<CatalogDbContext>(o => o
    .UseSqlServer(connectionString)
    // Опис фільму (1:1) прив'язаний до фільму з фільтром архівних — це свідомо, попередження зайве
    .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

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
app.UseMiddleware<CorrelationIdMiddleware>();          // CorrelationId / RequestPath / UserId у всіх логах запиту
app.UseSerilogRequestLogging(o => o.EnrichDiagnosticContext = (diag, http) =>
    diag.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString()));
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

public partial class Program;

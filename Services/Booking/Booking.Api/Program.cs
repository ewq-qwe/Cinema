using System.Reflection;
using Booking.Api.Errors;
using Booking.Bll.Mapping;
using Booking.Bll.Services;
using Booking.Dal;
using Booking.Dal.Abstractions;
using Platform.ServiceDefaults.Correlation;

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
app.MapDefaultEndpoints();   // /health і /alive
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

// Для WebApplicationFactory у тестах
public partial class Program;

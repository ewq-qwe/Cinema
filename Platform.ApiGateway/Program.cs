using Platform.ApiGateway.Observability;
using Platform.ServiceDefaults.Correlation;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, health checks, service discovery

// ---------- YARP: маршрути й кластери — розділ ReverseProxy в appsettings.json ----------
// Адреси кластерів — логічні імена ресурсів AppHost (http://booking-api); реальну адресу підставляє service discovery
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

builder.Services.AddSingleton<GatewayMetrics>();

var app = builder.Build();

// CorrelationId генерується тут, на вході в систему, ДО YARP: далі YARP копіює заголовок у проксований запит
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GatewayLoggingMiddleware>();

app.MapReverseProxy();
app.MapDefaultEndpoints();

// Довідка: які маршрути обслуговує Gateway
app.MapGet("/", (IProxyConfigProvider config) => Results.Ok(new
{
    gateway = "Cinema «Кадр» API Gateway (YARP)",
    routes = config.GetConfig().Routes
        .OrderBy(r => r.Order ?? 0)
        .Select(r => new { r.RouteId, path = r.Match.Path, r.ClusterId, r.Order }),
    clusters = config.GetConfig().Clusters
        .Select(c => new { c.ClusterId, destinations = c.Destinations?.Values.Select(d => d.Address) })
}));

app.Run();

using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;

// Gateway mínimo: sólo reparte los pedidos a los 5 servicios y resuelve CORS en un solo lugar.
// No valida tokens: lo hace cada servicio.

const string CorrelationHeader = "X-Correlation-Id";
const string CorsPolicyName = "Frontend";

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Servicio", "Gateway")
    .WriteTo.Console(new CompactJsonFormatter()));

// Orígenes desde Cors:AllowedOrigins (en variables de entorno: Cors__AllowedOrigins__0, __1, ...).
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
    policy.WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders(CorrelationHeader)));

builder.Services.AddHealthChecks();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Si el pedido no trae X-Correlation-Id, se genera acá y se reenvía al servicio,
// así el mismo id aparece en los logs del gateway y del servicio.
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers[CorrelationHeader].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64
        || !correlationId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
    {
        correlationId = Guid.NewGuid().ToString();
        context.Request.Headers[CorrelationHeader] = correlationId;
    }

    context.Response.OnStarting(() =>
    {
        context.Response.Headers[CorrelationHeader] = correlationId;
        return Task.CompletedTask;
    });

    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next(context);
    }
});

app.UseSerilogRequestLogging();
app.UseCors(CorsPolicyName);

app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();

using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;

// Minimal gateway: only routes requests to the 5 services and handles CORS in one place.
// It does not validate tokens: each service does.

const string CorrelationHeader = "X-Correlation-Id";
const string CorsPolicyName = "Frontend";

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Gateway")
    .WriteTo.Console(new CompactJsonFormatter()));

// Origins from Cors:AllowedOrigins (as environment variables: Cors__AllowedOrigins__0, __1, ...).
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

// If the request has no X-Correlation-Id, one is generated here and forwarded to the service,
// so the same id shows up in the gateway and service logs.
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

// Bloquea cualquier acceso externo a endpoints internos
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Gateway.Middleware;
using Gateway.Services;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;

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

builder.Services.AddSingleton<IServiceTokenFactory, ServiceTokenFactory>();

var identidadBaseUrl = builder.Configuration["Services:Identidad:BaseUrl"] ?? "http://identidad:8080";
builder.Services.AddHttpClient<ISessionValidationClient, SessionValidationClient>(client =>
{
    client.BaseAddress = new Uri(identidadBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddMemoryCache();

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var jwtKey = config["Jwt:Key"] ?? config["Jwt__Key"];
    var jwtIssuer = config["Jwt:Issuer"] ?? config["Jwt__Issuer"];
    var jwtAudience = config["Jwt:Audience"] ?? config["Jwt__Audience"];

    if (string.IsNullOrWhiteSpace(jwtKey))
    {
        throw new InvalidOperationException("Falta la variable de configuración obligatoria Jwt:Key (o Jwt__Key).");
    }

    if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    {
        throw new InvalidOperationException("Jwt:Key debe tener al menos 32 caracteres.");
    }

    if (string.IsNullOrWhiteSpace(jwtIssuer))
    {
        throw new InvalidOperationException("Falta la variable de configuración obligatoria Jwt:Issuer (o Jwt__Issuer).");
    }

    if (string.IsNullOrWhiteSpace(jwtAudience))
    {
        throw new InvalidOperationException("Falta la variable de configuración obligatoria Jwt:Audience (o Jwt__Audience).");
    }

    return new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "name",
        RoleClaimType = "role"
    };
});

var app = builder.Build();

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

// Middleware perimetral de seguridad: bloqueo de rutas internas, validación criptográfica, caché de sesión (60s) y Fail-Closed
app.UseMiddleware<PerimeterSecurityMiddleware>();

app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();

public partial class Program { }

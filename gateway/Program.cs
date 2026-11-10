using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
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

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var jwtKey = config["Jwt:Key"] ?? config["Jwt__Key"] ?? "clave_local_de_desarrollo_cambiar_32+";
    var jwtIssuer = config["Jwt:Issuer"] ?? config["Jwt__Issuer"];
    var jwtAudience = config["Jwt:Audience"] ?? config["Jwt__Audience"] ?? "protraining";

    return new TokenValidationParameters
    {
        ValidateIssuer = !string.IsNullOrWhiteSpace(jwtIssuer),
        ValidIssuer = jwtIssuer,
        ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
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

// 1. Bloquea cualquier acceso externo a endpoints internos
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.StartsWith("/api/v1/internal/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/v1/internal", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/identidad/api/v1/internal/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/identidad/api/v1/internal", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

// 2. Validación criptográfica perimetral y comprobación en tiempo real de sesión contra Identidad (Fail-Closed)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;

    // Permitir preflights CORS y endpoints públicos
    if (HttpMethods.IsOptions(context.Request.Method) ||
        path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/identidad/health", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/identidad/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/identidad/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
    {
        var authHeaderStr = authHeader.ToString();
        if (authHeaderStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var rawToken = authHeaderStr["Bearer ".Length..].Trim();
            var tokenValidationParameters = context.RequestServices.GetRequiredService<TokenValidationParameters>();

            // A. Validación criptográfica estricta de firma y expiración
            ClaimsPrincipal principal;
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
                principal = tokenHandler.ValidateToken(rawToken, tokenValidationParameters, out var securityToken);
            }
            catch (Exception)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"type\":\"https://httpstatuses.com/401\",\"title\":\"No autenticado.\",\"status\":401,\"detail\":\"El token JWT es inválido o su firma ha sido adulterada.\"}");
                return;
            }

            var userIdClaim = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var stampClaim = principal.FindFirst("security_stamp")?.Value;

            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"type\":\"https://httpstatuses.com/401\",\"title\":\"No autenticado.\",\"status\":401,\"detail\":\"Token sin identificador de usuario válido.\"}");
                return;
            }

            var sessionClient = context.RequestServices.GetRequiredService<ISessionValidationClient>();
            var validation = await sessionClient.ValidateSessionAsync(userId, stampClaim ?? string.Empty, context.RequestAborted);

            // B. Política Fail-Closed: si Identidad no responde, denegar acceso inmediatamente
            if (!validation.IsSuccess)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"type\":\"https://httpstatuses.com/503\",\"title\":\"Servicio no disponible.\",\"status\":503,\"detail\":\"No se pudo verificar la validez de la sesión de forma segura.\"}");
                return;
            }

            // C. Invalidez por SecurityStamp (token emitido antes de un reseteo o cambio de clave)
            if (!validation.IsValid)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"type\":\"https://httpstatuses.com/401\",\"title\":\"Sesión invalidada.\",\"status\":401,\"detail\":\"La sesión ha sido revocada por un cambio de credenciales.\"}");
                return;
            }

            // D. Bloqueo 403 basado en el estado MustChangePassword devuelto por Identidad
            if (validation.MustChangePassword)
            {
                var isAllowedForPasswordChange =
                    path.Equals("/identidad/api/v1/auth/change-password", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("/identidad/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("/identidad/api/v1/auth/logout", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("/identidad/health", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("/health", StringComparison.OrdinalIgnoreCase);

                if (!isAllowedForPasswordChange)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"type\":\"https://httpstatuses.com/403\",\"title\":\"Cambio de contraseña obligatorio.\",\"status\":403,\"detail\":\"Debe cambiar su contraseña antes de acceder a la aplicación.\"}");
                    return;
                }
            }
        }
    }

    await next();
});

app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();

public partial class Program { }

using System.Text;
using API.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authorization;

namespace API.Extensions;

public static class ServiceCollectionExtensions
{
    public const string CorsPolicyName = "Frontend";

    // Origins from Cors:AllowedOrigins (as environment variables: Cors__AllowedOrigins__0, __1, ...).
    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

        return services;
    }

    // Configura autenticacion JWT Bearer con FallbackPolicy: todo endpoint exige token salvo [AllowAnonymous].
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        string Required(string key) =>
            !string.IsNullOrWhiteSpace(configuration[key])
                ? configuration[key]!
                : throw new InvalidOperationException(
                    $"Falta la variable de configuración {key.Replace(":", "__")}.");

        var key = Required("Jwt:Key");
        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Jwt__Key debe tener al menos 32 caracteres.");

        var issuer = Required("Jwt:Issuer");
        var audience = Required("Jwt:Audience");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    // Claims are kept with their JWT names (MapInboundClaims = false),
                    // so tell ASP.NET Core which ones hold the user's name and role
                    // ([Authorize(Roles = "...")] and User.Identity.Name depend on this).
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;

    }

    // Swagger con el botón "Authorize": se pega el accessToken del login y Swagger lo manda en cada pedido.
    public static IServiceCollection AddSwaggerWithBearer(this IServiceCollection services, string serviceName)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = $"ProTraining · {serviceName}", Version = "v1" });

            var bearer = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Pegá el accessToken que devuelve el login (sin la palabra Bearer).",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            };
            options.AddSecurityDefinition("Bearer", bearer);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = new List<string>() });
        });

        return services;
    }
}

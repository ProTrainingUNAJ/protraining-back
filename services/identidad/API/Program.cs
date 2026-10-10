using System.Text.Json;
using API.Extensions;
using API.HealthChecks;
using API.Middleware;
using API.Services;
using Application.Interfaces.Services;
using Application.UseCases.Health.GetHealth;
using Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);
var servicio = builder.Configuration["Servicio"] ?? "API";
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Servicio", servicio)
    .WriteTo.Console(new CompactJsonFormatter()));
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                    entry => entry.Value!.Errors.Select(error =>
                        string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "El valor no es válido. Comprueba el JSON y los tipos de los campos."
                            : error.ErrorMessage).ToArray());
            return new BadRequestObjectResult(new ValidationProblemDetails(errors)
            {
                Status = 400,
                Title = "Solicitud no válida."
            });
        };
    });

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
// Reintentos, timeouts y circuit breaker para todos los HttpClient que se registren.
builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
builder.Services.AddScoped<GetHealthQueryHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUsuarioActual, UsuarioActual>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
        options.SwaggerDoc("v1", new() { Title = $"ProTraining · {servicio}", Version = "v1" }));
}

var app = builder.Build();
await app.Services.AplicarMigracionesAsync();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(ServiceCollectionExtensions.CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

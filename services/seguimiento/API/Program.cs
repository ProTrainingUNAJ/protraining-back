using System.Text.Json;
using System.Text.Json.Serialization;
using API.Extensions;
using API.HealthChecks;
using API.Middleware;
using API.Services;
using Application.Interfaces.Services;
using Application.UseCases.CheckIns.SubmitCheckIn;
using Application.UseCases.DailyLoads.RegisterDailyLoad;
using Application.UseCases.FitnessTests.ListFitnessTests;
using Application.UseCases.Health.GetHealth;
using Application.UseCases.Injuries.CreateAthleteInjury;
using Application.UseCases.Injuries.ListAthleteInjuries;
using Application.UseCases.Injuries.UpdateInjuryStatus;
using Application.UseCases.TrafficLight.GetCategoryTrafficLightToday;
using Domain.Services;
using Infrastructure;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);
var serviceName = builder.Configuration["Service"] ?? "API";
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", serviceName)
    .WriteTo.Console(new CompactJsonFormatter()));
builder.Services.AddControllers()
    // Los enums viajan como su nombre en el contrato (p. ej. "MedicallyCleared"), no como número;
    // sin este converter el front recibiría un 400 por no poder convertir el texto del contrato.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
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
// Resiliencia por defecto (reintentos, timeouts y circuit breaker) para todo HttpClient registrado (KAN-364).
// Regla: el semáforo se calcula solo con datos locales (check-ins y lesiones de esta base) y jamás llama
// a otros servicios, así funciona aunque el Asistente esté caído. Si algún día se agrega una llamada
// opcional al Asistente o a un módulo de IA, debe ser best-effort con timeouts cortos (1-2 s), un único
// reintento y caída siempre al motivo local del TrafficLightEngine (plan B), nunca propagando el error al cliente.
builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
builder.Services.AddScoped<GetHealthQueryHandler>();

// Motor puro del semáforo (KAN-363) y caso de uso del check-in diario (KAN-364/Demo 1):
// todo se calcula con datos locales de esta base, sin llamadas a otros servicios.
builder.Services.AddScoped<TrafficLightEngine>();
builder.Services.AddScoped<SubmitCheckInCommandHandler>();
builder.Services.AddScoped<RegisterDailyLoadCommandHandler>();
// Catálogo de tests físicos por capacidad motora (KAN-333).
builder.Services.AddScoped<ListFitnessTestsQueryHandler>();
// Lesiones de los atletas con su ciclo de vida (KAN-419): alta en Active + historial de estados.
builder.Services.AddScoped<CreateAthleteInjuryCommandHandler>();
builder.Services.AddScoped<ListAthleteInjuriesQueryHandler>();
// Máquina de estados estricta de las lesiones (KAN-420): Active → InRecovery → MedicallyCleared.
builder.Services.AddScoped<UpdateInjuryStatusCommandHandler>();
// Semáforo del día por categoría para el PF (KAN-364): consulta a Estructura best-effort
// (con la resiliencia del HttpClient) y degrada a los check-ins locales si no responde.
builder.Services.AddScoped<GetCategoryTrafficLightTodayQueryHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
// Estructura por HTTP, con el token del usuario (KAN-329): el controller compara el atleta
// del token con el athleteId de la ruta antes de registrar el check-in.
var structureBaseUrl = builder.Configuration["Services:Estructura:BaseUrl"];
if (!Uri.TryCreate(structureBaseUrl, UriKind.Absolute, out var structureUri))
    throw new InvalidOperationException("Falta la variable de configuración Services__Estructura__BaseUrl (URL absoluta).");
builder.Services.AddTransient<ForwardAuthorizationHandler>();
builder.Services.AddHttpClient<IStructureClient, StructureClient>(client => client.BaseAddress = structureUri)
    .AddHttpMessageHandler<ForwardAuthorizationHandler>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerWithBearer(serviceName);
}

var app = builder.Build();
await app.Services.ApplyMigrationsAsync();

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
app.MapHealthChecks("/health").AllowAnonymous();
app.Run();

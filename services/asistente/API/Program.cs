using System.Text.Json;
using API.Extensions;
using API.HealthChecks;
using API.Middleware;
using API.Services;
using Application.Interfaces.Services;
using Application.UseCases.Health.GetHealth;
using Application.UseCases.PlanDrafts.ApprovePlanDraft;
using Application.UseCases.PlanDrafts.CreatePlanDraft;
using Application.UseCases.PlanDrafts.DiscardPlanDraft;
using Application.UseCases.PlanDrafts.GetPlanDraft;
using Application.UseCases.PlanDrafts.UpdatePlanDraft;
using Infrastructure;
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
// NOTA (Resiliencia): No se aplica ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler())
// en este microservicio porque sus únicos clientes HTTP son los proveedores de IA (OllamaClient y GeminiClient).
// Dichos clientes cuentan con su propio manejo de tiempos límites configurables y su propia cadena de
// respaldo determinístico en ResilientAiService. Aplicar el manejador estándar de la plantilla provocaría
// 3 reintentos automáticos ante errores HTTP 429 (gastando cuota y demorando 8s) y cortaría a Ollama a los 10s.
// Entrenamiento por HTTP, con el token del preparador físico (#049 / KAN-344).
var trainingBaseUrl = builder.Configuration["Services:Entrenamiento:BaseUrl"];
if (!Uri.TryCreate(trainingBaseUrl, UriKind.Absolute, out var trainingUri))
    throw new InvalidOperationException("Falta la variable de configuración Services__Entrenamiento__BaseUrl (URL absoluta).");

builder.Services.AddTransient<ForwardAuthorizationHandler>();
builder.Services.AddHttpClient<IExerciseCatalogService, Infrastructure.Services.Catalog.ExerciseCatalogService>(client =>
{
    var uriString = trainingUri.ToString().TrimEnd('/') + "/";
    client.BaseAddress = new Uri(uriString);
    client.Timeout = TimeSpan.FromSeconds(5);
}).AddHttpMessageHandler<ForwardAuthorizationHandler>();

builder.Services.AddHttpClient<ITrainingPlanServiceClient, Infrastructure.Services.Training.TrainingPlanServiceClient>(client =>
{
    var uriString = trainingUri.ToString().TrimEnd('/') + "/";
    client.BaseAddress = new Uri(uriString);
    client.Timeout = TimeSpan.FromSeconds(5);
}).AddHttpMessageHandler<ForwardAuthorizationHandler>();

var structureBaseUrl = builder.Configuration["Services:Estructura:BaseUrl"];
if (!Uri.TryCreate(structureBaseUrl, UriKind.Absolute, out var structureUri))
{
    structureUri = new Uri("http://localhost:8082/");
}

builder.Services.AddHttpClient<IStructureCalendarClient, Infrastructure.Services.Structure.StructureCalendarClient>(client =>
{
    var uriString = structureUri.ToString().TrimEnd('/') + "/";
    client.BaseAddress = new Uri(uriString);
    client.Timeout = TimeSpan.FromSeconds(5);
})
.AddHttpMessageHandler<ForwardAuthorizationHandler>()
.AddStandardResilienceHandler();

builder.Services.AddScoped<GetHealthQueryHandler>();
builder.Services.AddScoped<Application.UseCases.Ai.SuggestRoutine.SuggestRoutineUseCase>();
builder.Services.AddScoped<CreatePlanDraftCommandHandler>();
builder.Services.AddScoped<GetPlanDraftQueryHandler>();
builder.Services.AddScoped<UpdatePlanDraftCommandHandler>();
builder.Services.AddScoped<ApprovePlanDraftCommandHandler>();
builder.Services.AddScoped<DiscardPlanDraftCommandHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
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

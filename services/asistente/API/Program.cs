using System.Text.Json;
using API.BackgroundServices;
using API.Extensions;
using API.HealthChecks;
using API.Middleware;
using API.Services;
using Application.Interfaces.Services;
using Application.UseCases.Alerts.CreatePreventiveAlert;
using Application.UseCases.Alerts.GetAlertById;
using Application.UseCases.Alerts.GetAlerts;
using Application.UseCases.Alerts.ProcessDailyOverloadAnalysis;
using Application.UseCases.Alerts.ReviewAlert;
using Application.UseCases.DayAdjustments.ApproveDayAdjustment;
using Application.UseCases.DayAdjustments.CreateDayAdjustment;
using Application.UseCases.DayAdjustments.RejectDayAdjustment;
using Application.UseCases.DayConflicts.GetDayConflicts;
using Application.UseCases.Adaptation.GetAthleteAdaptationContext;
using Application.UseCases.Health.GetHealth;
using Application.UseCases.PfDecisions.GetAthleteDecisions;
using Application.UseCases.PfDecisions.RecordDayAdjustmentDecision;
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
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problemDetails = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Uno o más errores de validación ocurrieron.",
            Detail = "Revise los errores para más detalles."
        };
        return new BadRequestObjectResult(problemDetails)
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});

builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddTransient<IServiceTokenFactory, ServiceTokenFactory>();
builder.Services.AddTransient<ForwardAuthorizationHandler>();

var trainingBaseUrl = builder.Configuration["Services:Entrenamiento:BaseUrl"];
if (!Uri.TryCreate(trainingBaseUrl, UriKind.Absolute, out var trainingUri))
{
    trainingUri = new Uri("http://localhost:8083/");
}

builder.Services.AddHttpClient<IExerciseCatalogService, Infrastructure.Services.Catalog.ExerciseCatalogService>(client =>
{
    var uriString = trainingUri.ToString().TrimEnd('/') + "/";
    client.BaseAddress = new Uri(uriString);
    // Timeout manejado por Polly (KAN-433)
})
.AddHttpMessageHandler<ForwardAuthorizationHandler>()
.AddProTrainingResilience();

builder.Services.AddHttpClient<ITrainingPlanServiceClient, Infrastructure.Services.Training.TrainingPlanServiceClient>(client =>
{
    var uriString = trainingUri.ToString().TrimEnd('/') + "/";
    client.BaseAddress = new Uri(uriString);
    // Timeout manejado por Polly (KAN-433)
})
.AddHttpMessageHandler<ForwardAuthorizationHandler>()
.AddProTrainingResilience();

var structureBaseUrl = builder.Configuration["Services:Estructura:BaseUrl"];
if (!Uri.TryCreate(structureBaseUrl, UriKind.Absolute, out var structureUri))
{
    structureUri = new Uri("http://localhost:8082/");
}

builder.Services.AddHttpClient<IStructureCalendarClient, Infrastructure.Services.Structure.StructureCalendarClient>(client =>
{
    var uriString = structureUri.ToString().TrimEnd('/') + "/";
    client.BaseAddress = new Uri(uriString);
    // Timeout manejado por Polly (KAN-433)
})
.AddHttpMessageHandler<ForwardAuthorizationHandler>()
.AddProTrainingResilience();

builder.Services.AddHttpClient<IStructureAthleteClient, Infrastructure.Services.Structure.StructureAthleteClient>(client =>
{
    var uriString = structureUri.ToString().TrimEnd('/') + "/";
    client.BaseAddress = new Uri(uriString);
    // Timeout manejado por Polly (KAN-433)
})
.AddHttpMessageHandler<ForwardAuthorizationHandler>()
.AddProTrainingResilience();

var seguimientoBaseUrl = builder.Configuration["Services:Seguimiento:BaseUrl"];
if (!Uri.TryCreate(seguimientoBaseUrl, UriKind.Absolute, out var seguimientoUri))
{
    seguimientoUri = new Uri("http://localhost:8084/");
}

builder.Services.AddHttpClient<ISeguimientoClient, Infrastructure.Services.Seguimiento.SeguimientoClient>(client =>
{
    var uriString = seguimientoUri.ToString().TrimEnd('/') + "/";
    client.BaseAddress = new Uri(uriString);
    // Timeout manejado por Polly (KAN-433)
})
.AddHttpMessageHandler<ForwardAuthorizationHandler>()
.AddProTrainingResilience();

builder.Services.AddScoped<GetHealthQueryHandler>();
builder.Services.AddScoped<Application.UseCases.Ai.SuggestRoutine.SuggestRoutineUseCase>();
builder.Services.AddScoped<CreatePlanDraftCommandHandler>();
builder.Services.AddScoped<GetPlanDraftQueryHandler>();
builder.Services.AddScoped<UpdatePlanDraftCommandHandler>();
builder.Services.AddScoped<ApprovePlanDraftCommandHandler>();
builder.Services.AddScoped<DiscardPlanDraftCommandHandler>();
builder.Services.AddScoped<GetAthleteDecisionsQueryHandler>();
builder.Services.AddScoped<GetDayConflictsQueryHandler>();
builder.Services.AddScoped<GetAthleteAdaptationContextQueryHandler>();
builder.Services.AddScoped<CreateDayAdjustmentCommandHandler>();
builder.Services.AddScoped<ApproveDayAdjustmentCommandHandler>();
builder.Services.AddScoped<RejectDayAdjustmentCommandHandler>();
builder.Services.AddScoped<RecordDayAdjustmentDecisionCommandHandler>();
builder.Services.AddScoped<GetAlertsQueryHandler>();
builder.Services.AddScoped<GetAlertByIdQueryHandler>();
builder.Services.AddScoped<ReviewAlertCommandHandler>();
builder.Services.AddScoped<CreatePreventiveAlertCommandHandler>();
builder.Services.AddScoped<ProcessDailyOverloadAnalysisCommandHandler>();
builder.Services.AddHostedService<DailyOverloadAnalysisWorker>();
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

// Visible para WebApplicationFactory<Program>: el test de punta a punta (KAN-414) levanta este mismo arranque.
public partial class Program;

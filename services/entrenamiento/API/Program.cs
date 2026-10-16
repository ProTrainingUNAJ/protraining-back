using API.Extensions;
using API.HealthChecks;
using API.Middleware;
using API.Services;
using Application.Interfaces.Services;
using Application.UseCases.Exercise.ListExercises;
using Application.UseCases.Health.GetHealth;
using Application.UseCases.Memberships.ResolveActiveMemberships;
using Application.UseCases.TrainingPlans.AssignPlan;
using Application.UseCases.TrainingPlans.CreatePlan;
using Application.UseCases.TrainingPlans.GetPlan;
using Application.UseCases.TrainingPlans.ListPlans;
using Application.UseCases.TrainingPlans.UpdatePlan;
using Application.UseCases.TrainingSessions.GetTrainingSession;
using Application.UseCases.TrainingSessions.UpdateCoachNotes;
using Application.UseCases.Routines.GetTodayRoutine;
using Domain.Services;
using Infrastructure;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Serilog.Formatting.Compact;
using System.Text.Json;

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
// Retries, timeouts and circuit breaker for every registered HttpClient.
builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
builder.Services.AddScoped<GetHealthQueryHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ListExercisesQueryHandler>();
builder.Services.AddScoped<AssignPlanCommandHandler>();
builder.Services.AddScoped<CreateTrainingPlanCommandHandler>();
builder.Services.AddScoped<UpdateTrainingPlanCommandHandler>();
builder.Services.AddScoped<GetTrainingPlanQueryHandler>();
builder.Services.AddScoped<ListTrainingPlansQueryHandler>();
builder.Services.AddScoped<UpdateCoachNotesCommandHandler>();
builder.Services.AddScoped<GetTrainingSessionQueryHandler>();
builder.Services.AddScoped<ILoadCalculatorEngine, LoadCalculatorEngine>();
builder.Services.AddScoped<GetTodayRoutineQueryHandler>();



// Estructura por HTTP, con el token del usuario (#062).
var structureBaseUrl = builder.Configuration["Services:Estructura:BaseUrl"];
if (!Uri.TryCreate(structureBaseUrl, UriKind.Absolute, out var structureUri))
    throw new InvalidOperationException("Falta la variable de configuración Services__Estructura__BaseUrl (URL absoluta).");
builder.Services.AddTransient<ForwardAuthorizationHandler>();
builder.Services.AddHttpClient<IStructureClient, StructureClient>(client => client.BaseAddress = structureUri)
    .AddHttpMessageHandler<ForwardAuthorizationHandler>();
builder.Services.AddScoped<ResolveActiveMembershipsQueryHandler>();
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
app.UseStaticFiles();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.Run();

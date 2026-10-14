using System.Text.Json;
using API.Extensions;
using API.HealthChecks;
using API.Middleware;
using API.Services;
using Application.Interfaces.Services;
using Application.UseCases.Athletes.AssignAthleteCategory;
using Application.UseCases.Athletes.CreateAthlete;
using Application.UseCases.Athletes.DeleteAthlete;
using Application.UseCases.Athletes.GetAthlete;
using Application.UseCases.Athletes.ListAthletes;
using Application.UseCases.Athletes.ListCategoriesByAthlete;
using Application.UseCases.Athletes.RemoveAthleteCategory;
using Application.UseCases.Athletes.UpdateAthlete;
using Application.UseCases.Athletes.UpdateHabitsPermission;
using Application.UseCases.Categories.CreateCategory;
using Application.UseCases.Categories.DeleteCategory;
using Application.UseCases.Categories.GetCategory;
using Application.UseCases.Categories.ListAthletesByCategory;
using Application.UseCases.Categories.ListCategories;
using Application.UseCases.Categories.UpdateCategory;
using Application.UseCases.Health.GetHealth;
using Application.UseCases.Organizations.CreateOrganization;
using Application.UseCases.Organizations.DeleteOrganization;
using Application.UseCases.Organizations.GetOrganization;
using Application.UseCases.Organizations.ListOrganizations;
using Application.UseCases.Organizations.UpdateOrganization;
using Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Serilog.Formatting.Compact;
using Application.UseCases.Export.ExportStructureCsv;

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
builder.Services.AddScoped<ListAthletesQueryHandler>();
builder.Services.AddScoped<GetAthleteQueryHandler>();
builder.Services.AddScoped<CreateAthleteCommandHandler>();
builder.Services.AddScoped<UpdateAthleteCommandHandler>();
builder.Services.AddScoped<UpdateHabitsPermissionCommandHandler>();
builder.Services.AddScoped<DeleteAthleteCommandHandler>();
builder.Services.AddScoped<AssignAthleteCategoryCommandHandler>();
builder.Services.AddScoped<RemoveAthleteCategoryCommandHandler>();
builder.Services.AddScoped<ListCategoriesByAthleteQueryHandler>();
builder.Services.AddScoped<ListOrganizationsQueryHandler>();
builder.Services.AddScoped<GetOrganizationQueryHandler>();
builder.Services.AddScoped<CreateOrganizationCommandHandler>();
builder.Services.AddScoped<UpdateOrganizationCommandHandler>();
builder.Services.AddScoped<DeleteOrganizationCommandHandler>();
builder.Services.AddScoped<ListCategoriesQueryHandler>();
builder.Services.AddScoped<GetCategoryQueryHandler>();
builder.Services.AddScoped<ListAthletesByCategoryQueryHandler>();
builder.Services.AddScoped<CreateCategoryCommandHandler>();
builder.Services.AddScoped<UpdateCategoryCommandHandler>();
builder.Services.AddScoped<DeleteCategoryCommandHandler>();
builder.Services.AddScoped<GetHealthQueryHandler>();
builder.Services.AddScoped<ExportStructureCsvQueryHandler>();
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


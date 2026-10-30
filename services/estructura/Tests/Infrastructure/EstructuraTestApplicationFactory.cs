using Application.UseCases.Categories.Invitations.GenerateCategoryInvitationToken;
using Application.UseCases.Categories.Invitations.ValidateCategoryInvitationToken;
using System.Net.Http.Headers;
using System.Text.Json;
using API.Extensions;
using API.HealthChecks;
using API.Middleware;
using API.Services;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Athletes.AssignAthleteCategory;
using Application.UseCases.Athletes.CreateAthlete;
using Application.UseCases.Athletes.DeleteAthlete;
using Application.UseCases.Athletes.GetAthlete;
using Application.UseCases.Athletes.ListAthletes;
using Application.UseCases.Athletes.ListCategoriesByAthlete;
using Application.UseCases.Athletes.ListMedicalRecordAuditLogs;
using Application.UseCases.Athletes.ListMyCategories;
using Application.UseCases.Athletes.ReadMedicalRecord;
using Application.UseCases.Athletes.RemoveAthleteCategory;
using Application.UseCases.Athletes.UpdateAthlete;
using Application.UseCases.Athletes.UpdateHabitsPermission;
using Application.UseCases.Athletes.UpdateMedicalRecord;
using Application.UseCases.Categories.CreateCategory;
using Application.UseCases.Categories.DeleteCategory;
using Application.UseCases.Categories.GetCategory;
using Application.UseCases.Categories.ListAthletesByCategory;
using Application.UseCases.Categories.ListCategories;
using Application.UseCases.Categories.UpdateCategory;
using Application.UseCases.Export.ExportStructureCsv;
using Application.UseCases.Health.GetHealth;
using Application.UseCases.Organizations.CreateOrganization;
using Application.UseCases.Organizations.DeleteOrganization;
using Application.UseCases.Organizations.GetOrganization;
using Application.UseCases.Organizations.ListOrganizations;
using Application.UseCases.Organizations.UpdateOrganization;
using Application.Services;
using Application.UseCases.Categories.Events.CreateCompetitiveEvent;
using Application.UseCases.Categories.Events.DeleteCompetitiveEvent;
using Application.UseCases.Categories.Events.ListCategoryEvents;
using Application.UseCases.Categories.Events.UpdateCompetitiveEvent;
using Application.UseCases.Categories.SeasonPhases.CreateSeasonPhase;
using Application.UseCases.Categories.SeasonPhases.DeleteSeasonPhase;
using Application.UseCases.Categories.SeasonPhases.ListCategorySeasonPhases;
using Application.UseCases.Categories.SeasonPhases.UpdateSeasonPhase;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Estructura.Tests.Infrastructure;

public sealed class EstructuraTestApplicationFactory : IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly TestServer _server;
    private readonly IWebPushClientWrapper? _customWebPushClient;
    private readonly TimeProvider _timeProvider;

    public EstructuraTestApplicationFactory(
        IWebPushClientWrapper? customWebPushClient = null,
        TimeProvider? customTimeProvider = null)
    {
        _customWebPushClient = customWebPushClient;
        _timeProvider = customTimeProvider ?? TimeProvider.System;
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestJwtGenerator.DefaultKey,
                ["Jwt:Issuer"] = TestJwtGenerator.DefaultIssuer,
                ["Jwt:Audience"] = TestJwtGenerator.DefaultAudience,
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
                ["Cors:AllowedOrigins:1"] = "http://localhost:3000"
            })
            .Build();

        var builder = new WebHostBuilder()
            .UseEnvironment("Development")
            .UseConfiguration(configuration)
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration);

                // Configure in-memory SQLite database
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite(_sqliteConnection));

                // Scoped repositories and unit of work
                services.AddScoped<IAthleteRepository, AthleteRepository>();
                services.AddScoped<IOrganizationRepository, OrganizationRepository>();
                services.AddScoped<ICategoryRepository, CategoryRepository>();
                services.AddScoped<IAthleteCategoryRepository, AthleteCategoryRepository>();
                services.AddScoped<IPendingPurgeRepository, PendingPurgeRepository>();
                services.AddScoped<IMedicalRecordAuditLogRepository, MedicalRecordAuditLogRepository>();
                services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();
                services.AddScoped<ICompetitiveEventRepository, CompetitiveEventRepository>();
                services.AddScoped<ISeasonPhaseRepository, SeasonPhaseRepository>();
                services.AddScoped<ICategoryInvitationRepository, CategoryInvitationRepository>();
                services.AddScoped<ISeasonPhaseValidationService, SeasonPhaseValidationService>();
                services.AddScoped<ICategoryAccessValidator, CategoryAccessValidator>();
                services.AddSingleton<TimeProvider>(_timeProvider);
                services.AddScoped<IUnitOfWork, UnitOfWork>();
                services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();

                // Web Push and VAPID setup for testing
                services.Configure<VapidOptions>(options =>
                {
                    options.Subject = "mailto:test@protraining.app";
                    options.PublicKey = "BKdJWX-W5-ybxqXlLV85tGKQ2pwGcbj_L9RkLRoDcbBfISRe-qYuFHV1qz5qLIGga7lriZEBRYWGaKJzxogdU6s";
                    options.PrivateKey = "9N3cywi7eeq5wpLmHZYPucIGuP2_1jNSm6auEJuLHIc";
                });

                if (_customWebPushClient is not null)
                {
                    services.AddSingleton(_customWebPushClient);
                }
                else
                {
                    services.AddScoped<IWebPushClientWrapper, DefaultWebPushClientWrapper>();
                }
                services.AddScoped<IPushNotificationService, PushNotificationService>();

                // Handlers
                services.AddScoped<ListAthletesQueryHandler>();
                services.AddScoped<GetAthleteQueryHandler>();
                services.AddScoped<CreateAthleteCommandHandler>();
                services.AddScoped<UpdateAthleteCommandHandler>();
                services.AddScoped<UpdateHabitsPermissionCommandHandler>();
                services.AddScoped<DeleteAthleteCommandHandler>();
                services.AddScoped<AssignAthleteCategoryCommandHandler>();
                services.AddScoped<RemoveAthleteCategoryCommandHandler>();
                services.AddScoped<ListCategoriesByAthleteQueryHandler>();
                services.AddScoped<ReadMedicalRecordCommandHandler>();
                services.AddScoped<UpdateMedicalRecordCommandHandler>();
                services.AddScoped<ListMedicalRecordAuditLogsQueryHandler>();
                services.AddScoped<Application.UseCases.Athletes.GetMe.GetMeQueryHandler>();
                services.AddScoped<ListMyCategoriesQueryHandler>();
                services.AddScoped<ListOrganizationsQueryHandler>();
                services.AddScoped<GetOrganizationQueryHandler>();
                services.AddScoped<CreateOrganizationCommandHandler>();
                services.AddScoped<UpdateOrganizationCommandHandler>();
                services.AddScoped<DeleteOrganizationCommandHandler>();
                services.AddScoped<ListCategoriesQueryHandler>();
                services.AddScoped<GetCategoryQueryHandler>();
                services.AddScoped<ListAthletesByCategoryQueryHandler>();
                services.AddScoped<CreateCategoryCommandHandler>();
                services.AddScoped<UpdateCategoryCommandHandler>();
                services.AddScoped<DeleteCategoryCommandHandler>();
                services.AddScoped<GetHealthQueryHandler>();
                services.AddScoped<ExportStructureCsvQueryHandler>();
                services.AddScoped<Application.UseCases.Notifications.RegisterPushSubscription.RegisterPushSubscriptionCommandHandler>();
                services.AddScoped<Application.UseCases.Notifications.UnsubscribePush.UnsubscribePushCommandHandler>();
                services.AddScoped<ListCategoryEventsQueryHandler>();
                services.AddScoped<CreateCompetitiveEventCommandHandler>();
                services.AddScoped<UpdateCompetitiveEventCommandHandler>();
                services.AddScoped<DeleteCompetitiveEventCommandHandler>();
                services.AddScoped<ListCategorySeasonPhasesQueryHandler>();
                services.AddScoped<CreateSeasonPhaseCommandHandler>();
                services.AddScoped<UpdateSeasonPhaseCommandHandler>();
                services.AddScoped<DeleteSeasonPhaseCommandHandler>();
                services.AddScoped<GenerateCategoryInvitationTokenCommandHandler>();
                services.AddScoped<ValidateCategoryInvitationTokenQueryHandler>();

                services.AddHttpContextAccessor();
                services.AddScoped<ICurrentUser, CurrentUser>();

                // Frontend CORS & JWT Auth with FallbackPolicy
                services.AddFrontendCors(configuration);
                services.AddJwtAuthentication(configuration);

                // Register Controllers from the API assembly
                services.AddControllers()
                    .AddApplicationPart(typeof(API.Controllers.AthletesController).Assembly)
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
            })
            .Configure(app =>
            {
                app.UseMiddleware<CorrelationIdMiddleware>();
                app.UseMiddleware<ExceptionMiddleware>();

                app.UseRouting();
                app.UseCors(ServiceCollectionExtensions.CorsPolicyName);
                app.UseAuthentication();
                app.UseAuthorization();

                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });

        _server = new TestServer(builder);

        // Ensure database schema is created
        using var scope = _server.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();
    }

    public HttpClient CreateClient() => _server.CreateClient();

    public HttpClient CreateAuthenticatedClient(Guid pfId, string role = "PreparadorFisico", Guid? userId = null)
    {
        var client = _server.CreateClient();
        var token = TestJwtGenerator.GenerateToken(pfId, userId, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public AppDbContext CreateDbContext(Guid? overridePfId = null)
    {
        if (overridePfId.HasValue)
        {
            var currentUserMock = new Moq.Mock<ICurrentUser>();
            currentUserMock.Setup(u => u.PfId).Returns(overridePfId.Value);
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_sqliteConnection)
                .Options;
            return new AppDbContext(options, currentUserMock.Object);
        }

        var scope = _server.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return context;
    }

    public IServiceScope CreateScope() => _server.Services.CreateScope();

    public async ValueTask DisposeAsync()
    {
        _server.Dispose();
        await _sqliteConnection.DisposeAsync();
    }

    public void Dispose()
    {
        _server.Dispose();
        _sqliteConnection.Dispose();
    }
}

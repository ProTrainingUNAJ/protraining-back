using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using API.Controllers;
using API.Extensions;
using API.Middleware;
using API.Services;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Services;
using Application.UseCases.AthleteRecord.CompleteOnboarding;
using Application.UseCases.AthleteRecord.ConfirmRecordsImport;
using Application.UseCases.AthleteRecord.GetRecordsReliability;
using Application.UseCases.AthleteRecord.ListAthleteRecords;
using Application.UseCases.AthleteRecord.PreviewRecordsImport;
using Application.UseCases.AthleteRecord.RegisterAthleteRecord;
using Application.UseCases.Exercise.AddExercise;
using Application.UseCases.Exercise.CheckRestrictions;
using Application.UseCases.Exercise.GetSubstitutes;
using Application.UseCases.Exercise.ListExercises;
using Application.UseCases.Exercise.UpdateVideoUrl;
using Application.UseCases.Health.GetHealth;
using Application.UseCases.Memberships.ResolveActiveMemberships;
using Application.UseCases.RecordProposals.ApproveRecordProposal;
using Application.UseCases.RecordProposals.DiscardRecordProposal;
using Application.UseCases.RecordProposals.ListRecordProposals;
using Application.UseCases.Routines.GetTodayRoutine;
using Application.UseCases.Routines.RecordPerformedSets;
using Application.UseCases.TrainingPlans.ApplyTemplate;
using Application.UseCases.TrainingPlans.AssignPlan;
using Application.UseCases.TrainingPlans.CreatePlan;
using Application.UseCases.TrainingPlans.GetPlan;
using Application.UseCases.TrainingPlans.GetSchedule;
using Application.UseCases.TrainingPlans.ListAssignments;
using Application.UseCases.TrainingPlans.ListPlans;
using Application.UseCases.TrainingPlans.ListTemplates;
using Application.UseCases.TrainingPlans.SavePlanAsTemplate;
using Application.UseCases.TrainingPlans.UpdatePlan;
using Application.UseCases.TrainingSessions.ApplyAthleteAdjustment;
using Application.UseCases.TrainingSessions.GetAthletesWithScheduledSession;
using Application.UseCases.TrainingSessions.GetTrainingSession;
using Application.UseCases.TrainingSessions.UpdateCoachNotes;
using Domain.Entities;
using Domain.Services;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Entrenamiento.Tests.Infrastructure;

public sealed class EntrenamientoTestApplicationFactory : IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly TestServer _server;

    public FakeStructureHandler Structure { get; }

    public EntrenamientoTestApplicationFactory(FakeStructureHandler? structureHandler = null)
    {
        Structure = structureHandler ?? new FakeStructureHandler();

        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestJwtGenerator.DefaultKey,
                ["Jwt:Issuer"] = TestJwtGenerator.DefaultIssuer,
                ["Jwt:Audience"] = TestJwtGenerator.DefaultAudience,
                ["Services:Estructura:BaseUrl"] = "http://estructura.test/"
            })
            .Build();

        var builder = new WebHostBuilder()
            .UseEnvironment("Development")
            .UseConfiguration(configuration)
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration);
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_sqliteConnection));

                services.AddScoped<IUnitOfWork, UnitOfWork>();
                services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();
                services.AddScoped<IExerciseRepository, ExerciseRepository>();
                services.AddScoped<IExerciseRestrictionRepository, ExerciseRestrictionRepository>();
                services.AddScoped<IAthleteRecordRepository, AthleteRecordRepository>();
                services.AddScoped<ITrainingPlanRepository, TrainingPlanRepository>();
                services.AddScoped<ITrainingSessionRepository, TrainingSessionRepository>();
                services.AddScoped<IPerformedSetRepository, PerformedSetRepository>();
                services.AddScoped<IRecordProposalRepository, RecordProposalRepository>();
                services.AddScoped<IExerciseVideoRepository, ExerciseVideoRepository>();
                services.AddScoped<IAthleteSessionAdjustmentRepository, AthleteSessionAdjustmentRepository>();
                services.AddSingleton<IExerciseMediaCatalog>(ExerciseMediaCatalog.Empty);

                services.AddHttpContextAccessor();
                services.AddScoped<ICurrentUser, CurrentUser>();
                services.AddScoped<IAthleteAccessGuard, AthleteAccessGuard>();

                services.AddScoped<GetHealthQueryHandler>();
                services.AddScoped<ListExercisesQueryHandler>();
                services.AddScoped<AssignPlanCommandHandler>();
                services.AddScoped<CreateTrainingPlanCommandHandler>();
                services.AddScoped<UpdateTrainingPlanCommandHandler>();
                services.AddScoped<GetTrainingPlanQueryHandler>();
                services.AddScoped<ListTrainingPlansQueryHandler>();
                services.AddScoped<GetPlanScheduleQueryHandler>();
                services.AddScoped<ListPlanAssignmentsQueryHandler>();
                services.AddScoped<GetAthletesWithScheduledSessionQueryHandler>();
                services.AddScoped<UpdateCoachNotesCommandHandler>();
                services.AddScoped<GetTrainingSessionQueryHandler>();
                services.AddScoped<ApplyAthleteSessionAdjustmentCommandHandler>();
                services.AddScoped<ILoadCalculatorEngine, LoadCalculatorEngine>();
                services.AddScoped<IExerciseRestrictionChecker, ExerciseRestrictionChecker>();
                services.AddScoped<CheckExerciseRestrictionsQueryHandler>();
                services.AddScoped<GetExerciseSubstitutesQueryHandler>();
                services.AddScoped<GetTodayRoutineQueryHandler>();
                services.AddScoped<RecordPerformedSetsCommandHandler>();
                services.AddScoped<ListAthleteRecordsQueryHandler>();
                services.AddScoped<GetRecordsReliabilityQueryHandler>();
                services.AddScoped<RegisterAthleteRecordCommandHandler>();
                services.AddScoped<CompleteOnboardingCommandHandler>();
                services.AddScoped<PreviewRecordsImportQueryHandler>();
                services.AddScoped<ConfirmRecordsImportCommandHandler>();
                services.AddScoped<CreateExerciseCommandHandler>();
                services.AddScoped<ListRecordProposalsQueryHandler>();
                services.AddScoped<ApproveRecordProposalCommandHandler>();
                services.AddScoped<DiscardRecordProposalCommandHandler>();
                services.AddScoped<SavePlanAsTemplateCommandHandler>();
                services.AddScoped<ListPlanTemplatesQueryHandler>();
                services.AddScoped<ApplyPlanTemplateCommandHandler>();
                services.AddScoped<UpdateExerciseVideoCommandHandler>();
                services.AddScoped<ResolveActiveMembershipsQueryHandler>();

                services.AddTransient<ForwardAuthorizationHandler>();
                services.AddHttpClient<IStructureClient, StructureClient>(client =>
                        client.BaseAddress = new Uri("http://estructura.test/"))
                    .ConfigurePrimaryHttpMessageHandler(() => Structure)
                    .AddHttpMessageHandler<ForwardAuthorizationHandler>();

                services.AddJwtAuthentication(configuration);
                services.AddControllers()
                    .AddJsonOptions(options =>
                        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
                    .AddApplicationPart(typeof(TrainingPlanController).Assembly);
            })
            .Configure(app =>
            {
                app.UseMiddleware<CorrelationIdMiddleware>();
                app.UseMiddleware<ExceptionMiddleware>();

                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();

                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });

        _server = new TestServer(builder);

        using var scope = _server.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();
    }

    public HttpClient CreateClient() => _server.CreateClient();

    public HttpClient CreateAuthenticatedClient(Guid pfId, string role = "PreparadorFisico", Guid? userId = null)
    {
        var client = _server.CreateClient();
        var token = TestJwtGenerator.GenerateToken(pfId, role, userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public AppDbContext CreateDbContext(Guid? overridePfId = null)
    {
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(overridePfId);
        currentUserMock.Setup(u => u.IsInRole(It.IsAny<string>()))
            .Returns<string>(role => overridePfId.HasValue && role == "PreparadorFisico");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        return new AppDbContext(options, currentUserMock.Object);
    }

    public async Task<PfSeedData> SeedPfDataAsync(
        Guid pfId,
        Guid? categoryId = null,
        Guid? athleteId = null)
    {
        var catId = categoryId ?? Guid.NewGuid();
        var athId = athleteId ?? Guid.NewGuid();

        // 1. Catálogo compartido (PfId = Guid.Empty)
        Exercise catalogExercise;
        using (var sharedContext = CreateDbContext(overridePfId: null))
        {
            catalogExercise = await sharedContext.Exercises
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Origin == Origin.Catalog)
                ?? Exercise.Create(
                    name: "Press de Banca Catálogo",
                    muscleGroup: "Pecho",
                    origin: Origin.Catalog);

            if (catalogExercise.Id == Guid.Empty || !await sharedContext.Exercises.IgnoreQueryFilters().AnyAsync(e => e.Id == catalogExercise.Id))
            {
                sharedContext.Exercises.Add(catalogExercise);
                await sharedContext.SaveChangesAsync();
            }
        }

        // 2. Datos del PF
        using var pfContext = CreateDbContext(overridePfId: pfId);

        var customExercise = Exercise.Create(
            name: $"Ejercicio Custom PF {pfId:N}",
            muscleGroup: "Piernas",
            origin: Origin.Coach);
        pfContext.Exercises.Add(customExercise);

        var plan = TrainingPlan.Create($"Plan de PF {pfId:N}", "Objetivo de fuerza");
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(
            week.Id,
            $"Sesión 1 - PF {pfId:N}",
            order: 1,
            dayOfWeek: DayOfWeek.Monday,
            coachNotes: "Notas iniciales del entrenador");

        var prescribed = PrescribedExercise.Create(
            order: 1,
            sets: 3,
            reps: 10,
            targetPercentage: 75m,
            trainingSessionId: session.Id,
            exerciseId: catalogExercise.Id);

        session.AddPrescribedExercise(prescribed);
        week.AddSession(session);
        plan.AddWeek(week);

        var assignment = PlanAssignment.Create(
            trainingPlanId: plan.Id,
            startDate: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-7)),
            categoryId: catId);

        var athleteRecord = AthleteRecord.Create(
            athleteId: athId,
            exerciseId: catalogExercise.Id,
            weight: 100m,
            source: RecordSource.Tested,
            date: DateTime.UtcNow);

        pfContext.TrainingPlans.Add(plan);
        pfContext.PlanAssignments.Add(assignment);
        pfContext.AthleteRecords.Add(athleteRecord);

        await pfContext.SaveChangesAsync();

        // Registrar en Structure handler para el PF correspondiente
        Structure.RegisterAthlete(athId, catId, pfId, $"Atleta de PF {pfId:N}");

        return new PfSeedData(
            PfId: pfId,
            CategoryId: catId,
            AthleteId: athId,
            Plan: plan,
            Week: week,
            Session: session,
            PrescribedExercise: prescribed,
            Assignment: assignment,
            Record: athleteRecord,
            CustomExercise: customExercise,
            CatalogExercise: catalogExercise);
    }

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

public sealed record PfSeedData(
    Guid PfId,
    Guid CategoryId,
    Guid AthleteId,
    TrainingPlan Plan,
    TrainingWeek Week,
    TrainingSession Session,
    PrescribedExercise PrescribedExercise,
    PlanAssignment Assignment,
    AthleteRecord Record,
    Exercise CustomExercise,
    Exercise CatalogExercise);
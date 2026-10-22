using Application.DTOs.TrainingPlans;
using Application.Interfaces.Services;
using Application.UseCases.TrainingPlans.UpdatePlan;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests;

public class TrainingPlanReorderTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _contextOptions;
    private readonly Guid _pfId = Guid.NewGuid();

    private sealed class TestCurrentUser(Guid pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    public TrainingPlanReorderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _contextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private AppDbContext CreateContext() => new(_contextOptions, new TestCurrentUser(_pfId));

    [Fact]
    public async Task UpdatePlan_DadoPlanConDosSesiones_CuandoSeIntercambianOrdenes_EntoncesResponde200YConservaIds()
    {
        // ARRANGE: Crear plan inicial con 2 sesiones en orden 1 y 2
        var exercise = Exercise.Create("Sentadilla", "Piernas");
        var plan = TrainingPlan.Create("Plan Reordenamiento 2 Sesiones", "Objetivo");
        var week = TrainingWeek.Create(1, plan.Id);

        var session1 = TrainingSession.Create(week.Id, "Sesion Lunes", 1, DayOfWeek.Monday, "Nota 1");
        session1.AddPrescribedExercise(PrescribedExercise.Create(1, 3, 10, 70m, session1.Id, exercise.Id));

        var session2 = TrainingSession.Create(week.Id, "Sesion Miercoles", 2, DayOfWeek.Wednesday, "Nota 2");
        session2.AddPrescribedExercise(PrescribedExercise.Create(1, 4, 8, 80m, session2.Id, exercise.Id));

        week.AddSession(session1);
        week.AddSession(session2);
        plan.AddWeek(week);

        await using (var seedContext = CreateContext())
        {
            await seedContext.Exercises.AddAsync(exercise);
            await seedContext.TrainingPlans.AddAsync(plan);
            await seedContext.SaveChangesAsync();
        }

        var originalSession1Id = session1.Id;
        var originalSession2Id = session2.Id;
        var originalWeekId = week.Id;

        // ACT: Intercambiar orden de S1 (1 -> 2) y S2 (2 -> 1) enviando sus IDs
        var updateRequest = new SaveTrainingPlanRequest(
            "Plan Reordenamiento 2 Sesiones",
            "Objetivo",
            new List<TrainingWeekRequest>
            {
                new(
                    1,
                    new List<TrainingSessionRequest>
                    {
                        new("Sesion Lunes", 2, DayOfWeek.Monday, "Nota 1",
                            new List<PrescribedExerciseRequest> { new(exercise.Id, 1, 3, 10, 70m) },
                            originalSession1Id),
                        new("Sesion Miercoles", 1, DayOfWeek.Wednesday, "Nota 2",
                            new List<PrescribedExerciseRequest> { new(exercise.Id, 1, 4, 8, 80m) },
                            originalSession2Id)
                    },
                    originalWeekId
                )
            }
        );

        await using (var actContext = CreateContext())
        {
            var repo = new TrainingPlanRepository(actContext);
            var uow = new UnitOfWork(actContext);
            var handler = new UpdateTrainingPlanCommandHandler(repo, uow);

            var result = await handler.Handle(new UpdateTrainingPlanCommand(plan.Id, updateRequest));

            // ASSERT sobre DTO retornado
            Assert.NotNull(result);
            Assert.Single(result.Weeks);
            var resultWeek = result.Weeks[0];
            Assert.Equal(2, resultWeek.Sessions.Count);

            var s1Result = resultWeek.Sessions.First(s => s.Id == originalSession1Id);
            var s2Result = resultWeek.Sessions.First(s => s.Id == originalSession2Id);

            Assert.Equal(2, s1Result.Order);
            Assert.Equal(1, s2Result.Order);
        }

        // ASSERT persistencia en base de datos SQLite con índice único
        await using (var verifyContext = CreateContext())
        {
            var persistedPlan = await verifyContext.TrainingPlans
                .Include(p => p.Weeks)
                .ThenInclude(w => w.Sessions)
                .FirstAsync(p => p.Id == plan.Id);

            var pSession1 = persistedPlan.Weeks.First().Sessions.First(s => s.Id == originalSession1Id);
            var pSession2 = persistedPlan.Weeks.First().Sessions.First(s => s.Id == originalSession2Id);

            Assert.Equal(2, pSession1.Order);
            Assert.Equal(1, pSession2.Order);
        }
    }

    [Fact]
    public async Task UpdatePlan_DadoPlanConTresSesiones_CuandoRotanOrdenes_EntoncesResponde200YConservaIds()
    {
        // ARRANGE: Crear plan inicial con 3 sesiones en orden 1, 2 y 3
        var exercise = Exercise.Create("Press Banca", "Pecho");
        var plan = TrainingPlan.Create("Plan Rotación 3 Sesiones", "Rotación");
        var week = TrainingWeek.Create(1, plan.Id);

        var session1 = TrainingSession.Create(week.Id, "S1", 1, DayOfWeek.Monday);
        var session2 = TrainingSession.Create(week.Id, "S2", 2, DayOfWeek.Wednesday);
        var session3 = TrainingSession.Create(week.Id, "S3", 3, DayOfWeek.Friday);

        week.AddSession(session1);
        week.AddSession(session2);
        week.AddSession(session3);
        plan.AddWeek(week);

        await using (var seedContext = CreateContext())
        {
            await seedContext.Exercises.AddAsync(exercise);
            await seedContext.TrainingPlans.AddAsync(plan);
            await seedContext.SaveChangesAsync();
        }

        var originalS1Id = session1.Id;
        var originalS2Id = session2.Id;
        var originalS3Id = session3.Id;
        var originalWeekId = week.Id;

        // ACT: Rotar órdenes (1->2, 2->3, 3->1) enviando sus IDs
        var updateRequest = new SaveTrainingPlanRequest(
            "Plan Rotación 3 Sesiones",
            "Rotación",
            new List<TrainingWeekRequest>
            {
                new(
                    1,
                    new List<TrainingSessionRequest>
                    {
                        new("S1", 2, DayOfWeek.Monday, null, null, originalS1Id),
                        new("S2", 3, DayOfWeek.Wednesday, null, null, originalS2Id),
                        new("S3", 1, DayOfWeek.Friday, null, null, originalS3Id)
                    },
                    originalWeekId
                )
            }
        );

        await using (var actContext = CreateContext())
        {
            var repo = new TrainingPlanRepository(actContext);
            var uow = new UnitOfWork(actContext);
            var handler = new UpdateTrainingPlanCommandHandler(repo, uow);

            var result = await handler.Handle(new UpdateTrainingPlanCommand(plan.Id, updateRequest));

            Assert.NotNull(result);
            var resWeek = result.Weeks[0];
            Assert.Equal(3, resWeek.Sessions.Count);

            Assert.Equal(2, resWeek.Sessions.First(s => s.Id == originalS1Id).Order);
            Assert.Equal(3, resWeek.Sessions.First(s => s.Id == originalS2Id).Order);
            Assert.Equal(1, resWeek.Sessions.First(s => s.Id == originalS3Id).Order);
        }

        // ASSERT persistencia en SQLite
        await using (var verifyContext = CreateContext())
        {
            var persistedPlan = await verifyContext.TrainingPlans
                .Include(p => p.Weeks)
                .ThenInclude(w => w.Sessions)
                .FirstAsync(p => p.Id == plan.Id);

            var sessions = persistedPlan.Weeks.First().Sessions;
            Assert.Equal(2, sessions.First(s => s.Id == originalS1Id).Order);
            Assert.Equal(3, sessions.First(s => s.Id == originalS2Id).Order);
            Assert.Equal(1, sessions.First(s => s.Id == originalS3Id).Order);
        }
    }

    [Fact]
    public async Task UpdatePlan_DadaSemanaEmparejadaPorId_CuandoCambiaWeekNumber_EntoncesActualizaWeekNumberYConservaId()
    {
        // ARRANGE: Crear plan inicial con 2 semanas (semana 1 y semana 2)
        var plan = TrainingPlan.Create("Plan Multi-Semana", "Objetivo");
        var week1 = TrainingWeek.Create(1, plan.Id);
        var week2 = TrainingWeek.Create(2, plan.Id);

        plan.AddWeek(week1);
        plan.AddWeek(week2);

        await using (var seedContext = CreateContext())
        {
            await seedContext.TrainingPlans.AddAsync(plan);
            await seedContext.SaveChangesAsync();
        }

        var originalWeek1Id = week1.Id;
        var originalWeek2Id = week2.Id;

        // ACT: Intercambiar números de semana (W1 -> semana 2, W2 -> semana 1) con sus IDs
        var updateRequest = new SaveTrainingPlanRequest(
            "Plan Multi-Semana",
            "Objetivo",
            new List<TrainingWeekRequest>
            {
                new(2, null, originalWeek1Id),
                new(1, null, originalWeek2Id)
            }
        );

        await using (var actContext = CreateContext())
        {
            var repo = new TrainingPlanRepository(actContext);
            var uow = new UnitOfWork(actContext);
            var handler = new UpdateTrainingPlanCommandHandler(repo, uow);

            var result = await handler.Handle(new UpdateTrainingPlanCommand(plan.Id, updateRequest));

            Assert.NotNull(result);
            Assert.Equal(2, result.Weeks.Count);

            var w1Result = result.Weeks.First(w => w.Id == originalWeek1Id);
            var w2Result = result.Weeks.First(w => w.Id == originalWeek2Id);

            Assert.Equal(2, w1Result.WeekNumber);
            Assert.Equal(1, w2Result.WeekNumber);
        }

        // ASSERT persistencia en SQLite
        await using (var verifyContext = CreateContext())
        {
            var persistedPlan = await verifyContext.TrainingPlans
                .Include(p => p.Weeks)
                .FirstAsync(p => p.Id == plan.Id);

            var pWeek1 = persistedPlan.Weeks.First(w => w.Id == originalWeek1Id);
            var pWeek2 = persistedPlan.Weeks.First(w => w.Id == originalWeek2Id);

            Assert.Equal(2, pWeek1.WeekNumber);
            Assert.Equal(1, pWeek2.WeekNumber);
        }
    }
}

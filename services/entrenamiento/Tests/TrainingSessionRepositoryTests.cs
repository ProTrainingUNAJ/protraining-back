using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests;

public class TrainingSessionRepositoryTests
{
    private sealed class TestCurrentUser(Guid pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private static AppDbContext CreateInMemoryDbContext(Guid pfId, string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new AppDbContext(options, new TestCurrentUser(pfId));
    }

    [Fact]
    public async Task GetByAthleteIdAsync_DadoPlanAsignadoPorCategoria_CuandoAtletaPerteneceACategoria_EntoncesResuelveSesionDeHoy()
    {
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var plan = TrainingPlan.Create("Plan Plantel Superior", "Fuerza");
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(week.Id, "Sesión de Hoy", 1, ArgentinaTime.DayOfWeek, "Buen calentamiento");
        week.AddSession(session);
        plan.AddWeek(week);

        var assignment = PlanAssignment.Create(plan.Id, ArgentinaTime.Today, categoryId: categoryId);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var repository = new TrainingSessionRepository(context);
            var result = await repository.GetByAthleteIdAsync(athleteId, [categoryId]);

            Assert.NotNull(result);
            Assert.Equal(session.Id, result.Id);
            Assert.Equal("Sesión de Hoy", result.Name);
        }
    }

    [Fact]
    public async Task GetByAthleteIdAsync_DadoPlanDeVariasSemanas_CuandoTranscurrenSieteDias_EntoncesResuelveSesionDeSemanaDos()
    {
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var plan = TrainingPlan.Create("Plan Progresión", "Hipertrofia");

        var week1 = TrainingWeek.Create(1, plan.Id);
        var sessionWeek1 = TrainingSession.Create(week1.Id, "Sesión Semana 1", 1, ArgentinaTime.DayOfWeek);
        week1.AddSession(sessionWeek1);
        plan.AddWeek(week1);

        var week2 = TrainingWeek.Create(2, plan.Id);
        var sessionWeek2 = TrainingSession.Create(week2.Id, "Sesión Semana 2", 1, ArgentinaTime.DayOfWeek);
        week2.AddSession(sessionWeek2);
        plan.AddWeek(week2);

        // Empezó hace 7 días exactos: (7 / 7) + 1 = semana 2
        var startDate = ArgentinaTime.Today.AddDays(-7);
        var assignment = PlanAssignment.Create(plan.Id, startDate, athleteId: athleteId);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var repository = new TrainingSessionRepository(context);
            var result = await repository.GetByAthleteIdAsync(athleteId, []);

            Assert.NotNull(result);
            Assert.Equal(sessionWeek2.Id, result.Id);
            Assert.Equal("Sesión Semana 2", result.Name);
        }
    }

    [Fact]
    public async Task GetByAthleteIdAsync_DadoPlanActivoSinSesionHoy_CuandoEsDiaDeDescanso_EntoncesRetornaNull()
    {
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var plan = TrainingPlan.Create("Plan con días específicos", "Fuerza");
        var week = TrainingWeek.Create(1, plan.Id);

        // Elegimos un día de la semana que NO sea hoy
        var otherDay = (DayOfWeek)(((int)ArgentinaTime.DayOfWeek + 1) % 7);
        var session = TrainingSession.Create(week.Id, "Sesión Otro Día", 1, otherDay);
        week.AddSession(session);
        plan.AddWeek(week);

        var assignment = PlanAssignment.Create(plan.Id, ArgentinaTime.Today, athleteId: athleteId);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var repository = new TrainingSessionRepository(context);
            var result = await repository.GetByAthleteIdAsync(athleteId, []);

            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetByAthleteIdAsync_DadoPlanConFechaInicioFutura_CuandoPlanNoEmpezo_EntoncesRetornaNull()
    {
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var plan = TrainingPlan.Create("Plan Futuro", "Potencia");
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(week.Id, "Sesión Futura", 1, ArgentinaTime.DayOfWeek);
        week.AddSession(session);
        plan.AddWeek(week);

        // Inicia mañana (plan no ha comenzado)
        var futureStartDate = ArgentinaTime.Today.AddDays(1);
        var assignment = PlanAssignment.Create(plan.Id, futureStartDate, athleteId: athleteId);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var repository = new TrainingSessionRepository(context);
            var result = await repository.GetByAthleteIdAsync(athleteId, []);

            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetByAthleteIdAsync_DadoPlanQueYaTermino_CuandoTranscurrenMasSemanasDeLasConfiguradas_EntoncesRetornaNull()
    {
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        // Plan de solo 1 semana
        var plan = TrainingPlan.Create("Plan Terminado", "Descarga");
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(week.Id, "Sesión Semana 1", 1, ArgentinaTime.DayOfWeek);
        week.AddSession(session);
        plan.AddWeek(week);

        // Empezó hace 14 días (estaría en semana 3 de un plan de 1 semana -> ya terminó)
        var pastStartDate = ArgentinaTime.Today.AddDays(-14);
        var assignment = PlanAssignment.Create(plan.Id, pastStartDate, athleteId: athleteId);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var repository = new TrainingSessionRepository(context);
            var result = await repository.GetByAthleteIdAsync(athleteId, []);

            Assert.Null(result);
        }
    }
}

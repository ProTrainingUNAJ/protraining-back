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
            var result = await repository.GetByAthleteIdAsync(athleteId, [categoryId], ArgentinaTime.Today);

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
            var result = await repository.GetByAthleteIdAsync(athleteId, [], ArgentinaTime.Today);

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
            var result = await repository.GetByAthleteIdAsync(athleteId, [], ArgentinaTime.Today);

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
            var result = await repository.GetByAthleteIdAsync(athleteId, [], ArgentinaTime.Today);

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
            var result = await repository.GetByAthleteIdAsync(athleteId, [], ArgentinaTime.Today);

            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetByAthleteIdAsync_DadaUnaFechaDistintaDeHoy_EntoncesResuelveLaSesionDeEseDia()
    {
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        // Plan con sesión el jueves y el viernes, asignado desde el lunes 19/10/2026
        var plan = TrainingPlan.Create("Fuerza base", "Pretemporada");
        var week = TrainingWeek.Create(1, plan.Id);
        var thursday = TrainingSession.Create(week.Id, "Día B", 1, DayOfWeek.Thursday);
        var friday = TrainingSession.Create(week.Id, "Día C", 2, DayOfWeek.Friday);
        week.AddSession(thursday);
        week.AddSession(friday);
        plan.AddWeek(week);

        var assignment = PlanAssignment.Create(plan.Id, new DateOnly(2026, 10, 19), athleteId: athleteId);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var repository = new TrainingSessionRepository(context);

            var fridayResult = await repository.GetByAthleteIdAsync(athleteId, [], new DateOnly(2026, 10, 23));
            var thursdayResult = await repository.GetByAthleteIdAsync(athleteId, [], new DateOnly(2026, 10, 22));
            var saturdayResult = await repository.GetByAthleteIdAsync(athleteId, [], new DateOnly(2026, 10, 24));

            Assert.Equal(friday.Id, fridayResult?.Id);
            Assert.Equal(thursday.Id, thursdayResult?.Id);
            Assert.Null(saturdayResult);
        }
    }

    [Fact]
    public async Task GetByAthleteIdAsync_DadoPlanConMultiplesBloquesMismoDia_ConsolidaTodosLosEjerciciosPrescritos()
    {
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var plan = TrainingPlan.Create("Plan Con Bloques", "Fuerza y movilidad");
        var week = TrainingWeek.Create(1, plan.Id);

        // Bloque 1: Entrada en calor (1 ejercicio)
        var block1 = TrainingSession.Create(week.Id, "Entrada en calor", 1, DayOfWeek.Tuesday);
        var ex1 = Exercise.Create("Movilidad de cadera", "General", "Core");
        var pe1 = PrescribedExercise.Create(1, 3, 10, null, block1.Id, ex1.Id);
        block1.AddPrescribedExercise(pe1);

        // Bloque 2: Fuerza A (2 ejercicios)
        var block2 = TrainingSession.Create(week.Id, "Fuerza A", 2, DayOfWeek.Tuesday);
        var ex2 = Exercise.Create("Sentadilla trasera", "General", "Piernas");
        var pe2 = PrescribedExercise.Create(1, 4, 8, 80m, block2.Id, ex2.Id);
        var ex3 = Exercise.Create("Press de banca", "General", "Pecho");
        var pe3 = PrescribedExercise.Create(2, 4, 8, 75m, block2.Id, ex3.Id);
        block2.AddPrescribedExercise(pe2);
        block2.AddPrescribedExercise(pe3);

        week.AddSession(block1);
        week.AddSession(block2);
        plan.AddWeek(week);

        // Martes 17/11/2026
        var tuesdayDate = new DateOnly(2026, 11, 17);
        var assignment = PlanAssignment.Create(plan.Id, tuesdayDate, athleteId: athleteId);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.Exercises.AddRangeAsync(ex1, ex2, ex3);
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var repository = new TrainingSessionRepository(context);
            var result = await repository.GetByAthleteIdAsync(athleteId, [], tuesdayDate);

            Assert.NotNull(result);
            Assert.Equal(3, result.PrescribedExercises.Count);

            var exercisesList = result.PrescribedExercises.OrderBy(pe => pe.Order).ToList();
            Assert.Equal(1, exercisesList[0].Order);
            Assert.Equal(ex1.Id, exercisesList[0].ExerciseId);

            Assert.Equal(2, exercisesList[1].Order);
            Assert.Equal(ex2.Id, exercisesList[1].ExerciseId);

            Assert.Equal(3, exercisesList[2].Order);
            Assert.Equal(ex3.Id, exercisesList[2].ExerciseId);
        }
    }
}

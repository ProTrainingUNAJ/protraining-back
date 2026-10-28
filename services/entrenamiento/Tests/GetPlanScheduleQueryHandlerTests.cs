using Application.Interfaces.Services;
using Application.UseCases.TrainingPlans.GetSchedule;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests;

// Agenda del PF para el calendario: qué sesiones tocan en una fecha en todas sus asignaciones
public class GetPlanScheduleQueryHandlerTests
{
    private sealed class TestCurrentUser(Guid pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private static AppDbContext CreateContext(Guid pfId, string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options, new TestCurrentUser(pfId));

    private static readonly DateOnly Monday = new(2026, 10, 19);

    // Plan con una sesión el viernes (dos ejercicios) y otra el miércoles
    private static TrainingPlan CreatePlan(string name)
    {
        var plan = TrainingPlan.Create(name, null);
        var week = TrainingWeek.Create(1, plan.Id);
        var wednesday = TrainingSession.Create(week.Id, "Día A", 1, DayOfWeek.Wednesday);
        var friday = TrainingSession.Create(week.Id, "Día C", 2, DayOfWeek.Friday, "Controlar la técnica");
        friday.AddPrescribedExercise(PrescribedExercise.Create(1, 4, 5, 80, friday.Id, Guid.NewGuid()));
        friday.AddPrescribedExercise(PrescribedExercise.Create(2, 3, 8, 70, friday.Id, Guid.NewGuid()));
        week.AddSession(wednesday);
        week.AddSession(friday);
        plan.AddWeek(week);
        return plan;
    }

    private static async Task<List<Application.DTOs.TrainingPlans.ScheduledSessionDto>> ScheduleOn(Guid pfId, string dbName, DateOnly date)
    {
        await using var context = CreateContext(pfId, dbName);
        var handler = new GetPlanScheduleQueryHandler(new TrainingSessionRepository(context));
        return await handler.Handle(new GetPlanScheduleQuery(date));
    }

    [Fact]
    public async Task Handle_DadoUnPlanAsignadoAUnaCategoria_CuandoTocaSesionEseDia_EntoncesLaDevuelveConSuCategoria()
    {
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var plan = CreatePlan("Fuerza base");

        await using (var context = CreateContext(pfId, dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(PlanAssignment.Create(plan.Id, Monday, categoryId: categoryId));
            await context.SaveChangesAsync();
        }

        var schedule = await ScheduleOn(pfId, dbName, new DateOnly(2026, 10, 23));

        var item = Assert.Single(schedule);
        Assert.Equal("Fuerza base", item.PlanName);
        Assert.Equal("Día C", item.SessionName);
        Assert.Equal(1, item.WeekNumber);
        Assert.Equal(categoryId, item.CategoryId);
        Assert.Null(item.AthleteId);
        Assert.Equal(2, item.ExerciseCount);
        Assert.Equal("Controlar la técnica", item.CoachNotes);
    }

    [Fact]
    public async Task Handle_DadoUnDiaSinSesionOAntesDelInicio_EntoncesNoDevuelveNada()
    {
        var pfId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var plan = CreatePlan("Fuerza base");

        await using (var context = CreateContext(pfId, dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(PlanAssignment.Create(plan.Id, Monday, athleteId: Guid.NewGuid()));
            await context.SaveChangesAsync();
        }

        Assert.Empty(await ScheduleOn(pfId, dbName, new DateOnly(2026, 10, 24))); // sábado: descanso
        Assert.Empty(await ScheduleOn(pfId, dbName, new DateOnly(2026, 10, 16))); // viernes anterior al inicio
        Assert.Empty(await ScheduleOn(pfId, dbName, new DateOnly(2026, 10, 30))); // semana 2: el plan tiene solo una
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Handle_DadoOtroPf_EntoncesNoVeSusAsignaciones()
    {
        var pfId = Guid.NewGuid();
        var otherPfId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var plan = CreatePlan("Plan de otro PF");

        await using (var context = CreateContext(otherPfId, dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(PlanAssignment.Create(plan.Id, Monday, athleteId: Guid.NewGuid()));
            await context.SaveChangesAsync();
        }

        Assert.Empty(await ScheduleOn(pfId, dbName, new DateOnly(2026, 10, 23)));
    }
}

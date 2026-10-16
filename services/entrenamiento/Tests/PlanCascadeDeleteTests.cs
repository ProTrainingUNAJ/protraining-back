using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests;

public class PlanCascadeDeleteTests
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
    public async Task DadoPlanCon4SemanasY12Sesiones_CuandoSeEliminaElPlan_EntoncesConsultaDeSesionesRetorna0Filas()
    {
        var pfId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var plan = TrainingPlan.Create("Plan Hipertrofia 4 Semanas", "Volumen y fuerza");

        // Creamos 4 semanas, cada una con 3 sesiones (total 12 sesiones)
        for (int weekNum = 1; weekNum <= 4; weekNum++)
        {
            var week = TrainingWeek.Create(weekNum, plan.Id);

            week.AddSession(TrainingSession.Create(week.Id, $"Semana {weekNum} - Día 1", 1, DayOfWeek.Monday, "Foco tren superior"));
            week.AddSession(TrainingSession.Create(week.Id, $"Semana {weekNum} - Día 2", 2, DayOfWeek.Wednesday, "Foco pierna"));
            week.AddSession(TrainingSession.Create(week.Id, $"Semana {weekNum} - Día 3", 3, DayOfWeek.Friday, "Foco full body"));

            plan.AddWeek(week);
        }

        // Persistimos en la base de datos
        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.SaveChangesAsync();
        }

        // Verificamos estado previo: 1 plan, 4 semanas, 12 sesiones
        await using (var verifyContext = CreateInMemoryDbContext(pfId, dbName))
        {
            var savedPlan = await verifyContext.TrainingPlans
                .Include(p => p.Weeks)
                .ThenInclude(w => w.Sessions)
                .FirstOrDefaultAsync(p => p.Id == plan.Id);

            Assert.NotNull(savedPlan);
            Assert.Equal(4, savedPlan.Weeks.Count);
            Assert.Equal(12, savedPlan.Weeks.SelectMany(w => w.Sessions).Count());

            var totalSessionsBefore = await verifyContext.TrainingSessions.CountAsync();
            Assert.Equal(12, totalSessionsBefore);
        }

        await using (var deleteContext = CreateInMemoryDbContext(pfId, dbName))
        {
            var planToDelete = await deleteContext.TrainingPlans
                .Include(p => p.Weeks)
                .ThenInclude(w => w.Sessions)
                .FirstAsync(p => p.Id == plan.Id);

            deleteContext.TrainingPlans.Remove(planToDelete);
            await deleteContext.SaveChangesAsync();
        }

        await using (var assertContext = CreateInMemoryDbContext(pfId, dbName))
        {
            var associatedSessions = await assertContext.TrainingSessions
                .Where(s => s.Week.TrainingPlanId == plan.Id)
                .ToListAsync();

            Assert.Empty(associatedSessions);

            var totalSessionsInDb = await assertContext.TrainingSessions.CountAsync();
            Assert.Equal(0, totalSessionsInDb);

            var totalWeeksInDb = await assertContext.TrainingWeeks.CountAsync();
            Assert.Equal(0, totalWeeksInDb);

            var planInDb = await assertContext.TrainingPlans.FirstOrDefaultAsync(p => p.Id == plan.Id);
            Assert.Null(planInDb);
        }
    }
}

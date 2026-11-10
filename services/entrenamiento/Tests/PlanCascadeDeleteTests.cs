using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Tests;

[Trait("Category", "RequiresDocker")]
public class PlanCascadeDeleteTests : IAsyncLifetime
{
    private const string PostgresImage = "postgres:16-alpine";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage(PostgresImage)
        .Build();

    private sealed class TestCurrentUser(Guid pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext CreatePostgresDbContext(Guid pfId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new AppDbContext(options, new TestCurrentUser(pfId));
    }

    [Fact]
    public async Task DadoPlanCon4SemanasY12Sesiones_CuandoSeEliminaElPlan_EntoncesConsultaDeSesionesRetorna0Filas()
    {
        var pfId = Guid.NewGuid();

        await using (var migrationContext = CreatePostgresDbContext(pfId))
        {
            await migrationContext.Database.MigrateAsync();
        }

        // ARRANGE: Criterio de aceptación:
        // DADO un plan con 4 semanas y 12 sesiones
        // CUANDO se ejecuta la eliminación del plan
        // ENTONCES la consulta de sesiones asociadas en base de datos retorna 0 filas
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
        await using (var context = CreatePostgresDbContext(pfId))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.SaveChangesAsync();
        }

        // Verificamos estado previo: 1 plan, 4 semanas, 12 sesiones
        await using (var verifyContext = CreatePostgresDbContext(pfId))
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

        // ACT: CUANDO se ejecuta la eliminación del plan
        await using (var deleteContext = CreatePostgresDbContext(pfId))
        {
            var planToDelete = await deleteContext.TrainingPlans
                .Include(p => p.Weeks)
                .ThenInclude(w => w.Sessions)
                .FirstAsync(p => p.Id == plan.Id);

            deleteContext.TrainingPlans.Remove(planToDelete);
            await deleteContext.SaveChangesAsync();
        }

        // ASSERT: ENTONCES la consulta de sesiones asociadas en base de datos retorna 0 filas
        await using (var assertContext = CreatePostgresDbContext(pfId))
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

            // Verificamos en Postgres real que las filas no fueron borradas físicamente con DELETE
            // sino que sufrieron el borrado lógico (deleted_at IS NOT NULL)
            var physicallyExistingSessions = await assertContext.TrainingSessions
                .IgnoreQueryFilters()
                .Where(s => s.Week.TrainingPlanId == plan.Id)
                .ToListAsync();

            Assert.Equal(12, physicallyExistingSessions.Count);
            Assert.All(physicallyExistingSessions, s => Assert.NotNull(s.DeletedAt));
        }
    }
}

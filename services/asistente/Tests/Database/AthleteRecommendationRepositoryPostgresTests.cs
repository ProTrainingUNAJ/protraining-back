namespace Asistente.Tests.Database;

using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Pruebas contra PostgreSQL real (Testcontainers) para AthleteRecommendationRepository (#197 · KAN-492).
/// Verifica tracking real de GetByIdForUpdateAsync, visibilidad de ListApprovedByAthleteAsync y aislamiento multi-tenant.
/// </summary>
[Trait("Category", "RequiresDocker")]
[Trait("Category", "Isolation")]
public sealed class AthleteRecommendationRepositoryPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly Guid _pfAId = Guid.NewGuid();
    private readonly Guid _pfBId = Guid.NewGuid();
    private readonly Guid _athleteAId = Guid.NewGuid();
    private readonly Guid _athleteBId = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext(_pfAId);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext CreateContext(Guid pfId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.PfId).Returns(pfId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new AppDbContext(options, currentUser.Object);
    }

    [Fact]
    public async Task Given_PendingRecommendationInPostgres_When_PfEditsAndApproves_Then_AthleteSeesApprovedAndOtherTenantsAreIsolated()
    {
        // 1. Insertar recomendación pendiente para Atleta A (PF A)
        var pending = AthleteRecommendation.Create(
            athleteId: _athleteAId,
            title: "Título inicial",
            reason: "salto por debajo del promedio",
            recommendationText: "Texto inicial generado por el motor.",
            categoryId: _categoryId,
            categoryName: "Primera División");

        await using (var insertContext = CreateContext(_pfAId))
        {
            var repo = new AthleteRecommendationRepository(insertContext);
            await repo.AddAsync(pending);
            await insertContext.SaveChangesAsync();
        }

        // 2. Antes de aprobar: Atleta A no ve nada aprobado
        await using (var athleteBeforeContext = CreateContext(_pfAId))
        {
            var repo = new AthleteRecommendationRepository(athleteBeforeContext);
            Assert.Empty(await repo.ListApprovedByAthleteAsync(_athleteAId));
        }

        // 3. PF B no puede obtener para edición la recomendación de PF A (aislamiento global)
        await using (var pfBContext = CreateContext(_pfBId))
        {
            var repo = new AthleteRecommendationRepository(pfBContext);
            Assert.Null(await repo.GetByIdForUpdateAsync(pending.Id));
        }

        // 4. PF A edita y aprueba usando GetByIdForUpdateAsync (con tracking real de EF Core)
        await using (var updateContext = CreateContext(_pfAId))
        {
            var repo = new AthleteRecommendationRepository(updateContext);
            var tracked = await repo.GetByIdForUpdateAsync(pending.Id);
            Assert.NotNull(tracked);

            tracked!.UpdateContent("Foco semanal en salto", "Sumá 2 bloques de saltos controlados esta semana.");
            tracked.Approve();
            await updateContext.SaveChangesAsync();
        }

        // 5. Después de aprobar: Atleta A ve su recomendación aprobada; Atleta B del mismo PF no la ve
        await using (var athleteAfterContext = CreateContext(_pfAId))
        {
            var repo = new AthleteRecommendationRepository(athleteAfterContext);
            var visibleToA = await repo.ListApprovedByAthleteAsync(_athleteAId);
            var item = Assert.Single(visibleToA);
            Assert.Equal(pending.Id, item.Id);
            Assert.Equal(RecommendationStatus.Approved, item.Status);
            Assert.Equal("Foco semanal en salto", item.Title);
            Assert.Equal("salto por debajo del promedio", item.Reason);
            Assert.Equal("Sumá 2 bloques de saltos controlados esta semana.", item.RecommendationText);
            Assert.NotNull(item.ReviewedAtUtc);

            Assert.Empty(await repo.ListApprovedByAthleteAsync(_athleteBId));
        }
    }
}

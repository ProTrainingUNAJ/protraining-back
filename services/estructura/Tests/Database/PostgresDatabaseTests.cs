using Application.Interfaces.Services;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Testcontainers.PostgreSql;

namespace Estructura.Tests.Database;

// Pruebas contra la base real (KAN-356). El resto de los tests usa SQLite en memoria con EnsureCreated(),
// que arma las tablas desde el modelo y nunca aplica las migraciones: estos dos tests cubren ese hueco.
public sealed class PostgresDatabaseTests
{
    // Misma imagen que el docker-compose.
    private const string PostgresImage = "postgres:16-alpine";

    // No necesita Docker: compara el modelo del código con el snapshot de la última migración.
    // Si alguien cambia una entidad y se olvida de crear la migración, falla acá y no en runtime.
    [Fact]
    public void Given_CurrentModel_When_ComparedWithLastMigration_Then_ThereAreNoPendingChanges()
    {
        // arrange: el contexto con Npgsql, sin conectarse (solo se usa el modelo)
        using var context = CreateContext("Host=localhost;Database=model_only");
        var snapshotModel = context.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        Assert.NotNull(snapshotModel);

        if (snapshotModel is IMutableModel mutableModel)
            snapshotModel = mutableModel.FinalizeModel();
        snapshotModel = context.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel);
        var currentModel = context.GetService<IDesignTimeModel>().Model;

        // act
        var differences = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotModel.GetRelationalModel(), currentModel.GetRelationalModel());

        // assert
        Assert.True(
            differences.Count == 0,
            "El modelo tiene cambios sin migración. Creala con 'dotnet ef migrations add <Nombre>'. Cambios: "
            + string.Join(", ", differences.Select(difference => difference.GetType().Name)));
    }

    // Levanta Postgres 16 real, aplica todas las migraciones y consulta cada tabla del modelo:
    // si falta una columna o una tabla en las migraciones, la consulta falla.
    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgreSql_When_ApplyingAllMigrations_Then_EveryEntityCanBeQueried()
    {
        // arrange
        await using var postgresContainer = new PostgreSqlBuilder()
            .WithImage(PostgresImage)
            .Build();
        await postgresContainer.StartAsync();
        await using var context = CreateContext(postgresContainer.GetConnectionString());

        // act
        await context.Database.MigrateAsync();

        // assert: no quedan migraciones sin aplicar y cada entidad se puede consultar
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        var queryMethod = typeof(PostgresDatabaseTests).GetMethod(nameof(QueryFirstAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var entityTypes = context.Model.GetEntityTypes()
            .Where(entityType => !entityType.IsOwned() && !entityType.HasSharedClrType && entityType.GetTableName() is not null);

        foreach (var entityType in entityTypes)
            await (Task)queryMethod.MakeGenericMethod(entityType.ClrType).Invoke(null, [context])!;
    }

    private static Task QueryFirstAsync<TEntity>(AppDbContext context) where TEntity : class =>
        context.Set<TEntity>().Take(1).ToListAsync();

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AppDbContext(options, new Mock<ICurrentUser>().Object);
    }
}

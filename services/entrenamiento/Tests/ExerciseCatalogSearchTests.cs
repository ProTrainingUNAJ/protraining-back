using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Entrenamiento.Tests;

// E51: el catalogo que consume MCP nunca debe mezclar ejercicios privados de un PF.
public sealed class ExerciseCatalogSearchTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly Guid _pfId = Guid.NewGuid();

    private sealed class TestCurrentUser(Guid? pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    public ExerciseCatalogSearchTests()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var context = CreateContext(null);
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AppDbContext CreateContext(Guid? pfId) => new(_options, new TestCurrentUser(pfId));

    [Fact]
    public async Task Given_CatalogAndPfExercises_When_SearchingCatalog_Then_OnlySharedCatalogIsReturned()
    {
        // ARRANGE: un ejercicio del catalogo compartido y uno privado del PF.
        await using (var catalogContext = CreateContext(null))
        {
            await catalogContext.Exercises.AddAsync(Exercise.Create("Sentadilla compartida", "Piernas"));
            await catalogContext.SaveChangesAsync();
        }

        await using (var pfContext = CreateContext(_pfId))
        {
            await pfContext.Exercises.AddAsync(Exercise.Create("Sentadilla privada del PF", "Piernas"));
            await pfContext.SaveChangesAsync();
        }

        // ACT: consultamos con el PF autenticado, que si no fuera por el filtro explicito
        // tambien veria su ejercicio privado.
        await using var queryContext = CreateContext(_pfId);
        var repository = new ExerciseRepository(queryContext);
        var (items, total) = await repository.SearchCatalogAsync(null, null, null, 1, 20);

        // ASSERT
        Assert.Equal(1, total);
        var item = Assert.Single(items);
        Assert.Equal("Sentadilla compartida", item.Name);
        Assert.Equal(Guid.Empty, item.PfId);
    }

    [Fact]
    public async Task Given_EFilteredPrivateExercise_When_SearchingCatalog_Then_PrivateIsNotReturned()
    {
        // ARRANGE: el mismo nombre en el catalogo y en el PF.
        await using (var catalogContext = CreateContext(null))
        {
            await catalogContext.Exercises.AddAsync(Exercise.Create("Press de banca", "Pecho"));
            await catalogContext.SaveChangesAsync();
        }

        await using (var pfContext = CreateContext(_pfId))
        {
            await pfContext.Exercises.AddAsync(Exercise.Create("Press de banca", "Pecho"));
            await pfContext.SaveChangesAsync();
        }

        // ACT
        await using var queryContext = CreateContext(_pfId);
        var repository = new ExerciseRepository(queryContext);
        var (items, total) = await repository.SearchCatalogAsync(null, null, null, 1, 20);

        // ASSERT
        Assert.Equal(1, total);
        Assert.All(items, exercise => Assert.Equal(Guid.Empty, exercise.PfId));
    }
}

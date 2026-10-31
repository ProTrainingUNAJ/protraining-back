using Application.Interfaces.Services;
using Application.UseCases.Exercise.ListExercises;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;

namespace Entrenamiento.Tests.Database;

// KAN-554: el listado agrupado contra PostgreSQL real (búsqueda trigram, migraciones reales).
public sealed class ExerciseVariantsPostgresTests : IAsyncLifetime
{
    private const string PostgresImage = "postgres:16-alpine";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage(PostgresImage).Build();
    private DbContextOptions<AppDbContext> _options = null!;
    private readonly Mock<ICurrentUser> _currentUser = new();

    private Exercise _bench = null!;
    private Exercise _closeGrip = null!;
    private Exercise _dumbbell = null!;
    private Exercise _squat = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        _currentUser.Setup(u => u.PfId).Returns((Guid?)null);

        await using var context = new AppDbContext(_options, _currentUser.Object);
        await context.Database.MigrateAsync();

        _bench = Exercise.Create("Press de banca con barra - Agarre medio", "Pecho");
        _closeGrip = Exercise.Create("Press de banca con barra con agarre cerrado", "Pecho");
        _dumbbell = Exercise.Create("Press de banca con mancuernas", "Pecho");
        _squat = Exercise.Create("Sentadilla con barra", "Piernas");
        var zercher = Exercise.Create("Sentadillas Zercher", "Piernas");
        // Un "press de banca" suelto, sin variantes, que alfabéticamente va antes que la familia.
        var guillotine = Exercise.Create("Press de banca con agarre cerrado en máquina Smith", "Pecho");

        await context.Exercises.AddRangeAsync(_bench, _closeGrip, _dumbbell, _squat, zercher, guillotine);
        await context.SaveChangesAsync();

        _closeGrip.AssignAsVariantOf(_bench, "Agarre cerrado");
        _dumbbell.AssignAsVariantOf(_bench, "Con mancuernas");
        zercher.AssignAsVariantOf(_squat, "Zercher");
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private ListExercisesQueryHandler CreateHandler(AppDbContext context) => new(new ExerciseRepository(context));

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgreSql_When_SearchingPressDeBancaGrouped_Then_ReturnsTheFamilyOnceAndFirstWithItsVariants()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        var result = await CreateHandler(context).Handle(new ListExercisesQuery { Search = "press de banca", GroupVariants = true });

        // La familia aparece una sola vez y primero, antes que el press de banca suelto.
        Assert.Equal(2, result.TotalCount);
        var item = result.Items[0];
        Assert.Equal(_bench.Id, item.Id);
        Assert.Single(result.Items, i => i.Id == _bench.Id);
        Assert.DoesNotContain(result.Items, i => i.Id == _closeGrip.Id || i.Id == _dumbbell.Id);
        Assert.NotNull(item.Variants);
        Assert.Equal(2, item.Variants!.Count);
        Assert.Contains(item.Variants, v => v.Id == _closeGrip.Id && v.VariantLabel == "Agarre cerrado");
        Assert.Contains(item.Variants, v => v.Id == _dumbbell.Id && v.VariantLabel == "Con mancuernas");
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_SearchMatchingOnlyAVariant_When_ListingGrouped_Then_ReturnsItsBase()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        var result = await CreateHandler(context).Handle(new ListExercisesQuery { Search = "zercher", GroupVariants = true });

        var item = Assert.Single(result.Items);
        Assert.Equal(_squat.Id, item.Id);
        Assert.Contains(item.Variants!, v => v.VariantLabel == "Zercher");
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_NoSearch_When_ListingGrouped_Then_TotalCountsOnlyBases()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        var result = await CreateHandler(context).Handle(new ListExercisesQuery { GroupVariants = true });

        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, i => Assert.Null(i.ParentExerciseId));
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_GroupVariantsFalse_When_Listing_Then_ListStaysFlatAsBefore()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        var result = await CreateHandler(context).Handle(new ListExercisesQuery { Search = "press de banca" });

        Assert.Equal(4, result.TotalCount);
        Assert.Contains(result.Items, i => i.Id == _closeGrip.Id && i.ParentExerciseId == _bench.Id);
        Assert.All(result.Items, i => Assert.Null(i.Variants));
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_Ids_When_Listing_Then_ReturnsExactlyThoseExercisesIgnoringOtherFilters()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        var result = await CreateHandler(context).Handle(new ListExercisesQuery
        {
            Ids = [_closeGrip.Id, _squat.Id, Guid.NewGuid()],
            Search = "algo que no coincide",
            GroupVariants = true,
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, i => i.Id == _closeGrip.Id && i.ParentExerciseId == _bench.Id);
        Assert.Contains(result.Items, i => i.Id == _squat.Id);
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_ABaseWithVariants_When_DeletingTheBaseRow_Then_DatabaseRejectsIt()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM exercises WHERE id = {_bench.Id}"));

        Assert.Contains("FK_exercises_exercises_parent_exercise_id", ex.ToString());
    }
}

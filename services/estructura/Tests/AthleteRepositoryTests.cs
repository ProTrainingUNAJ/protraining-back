using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Estructura.Tests;

public sealed class AthleteRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _context;
    private readonly AthleteRepository _repository;
    private readonly Guid _pfId = Guid.NewGuid();

    public AthleteRepositoryTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(_pfId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _context = new AppDbContext(options, currentUserMock.Object);
        _context.Database.EnsureCreated();

        _repository = new AthleteRepository(_context);
    }

    [Fact]
    public async Task GetByAccountIdAsync_WhenNoAthleteMatches_ReturnsNull()
    {
        var result = await _repository.GetByAccountIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByAccountIdAsync_WhenSingleAthleteMatches_ReturnsThatAthlete()
    {
        var accountId = Guid.NewGuid();
        var athlete = Athlete.CreateAdult("Lucas", "Gómez", "lucas@example.com", "123456");
        athlete.LinkAccount(accountId);

        await _repository.AddAsync(athlete);
        await _context.SaveChangesAsync();

        var result = await _repository.GetByAccountIdAsync(accountId);

        Assert.NotNull(result);
        Assert.Equal(athlete.Id, result.Id);
    }

    [Fact]
    public async Task GetByAccountIdAsync_WhenMultipleAthletesWithSameAccountId_PrioritizesEnrolledAthlete()
    {
        var accountId = Guid.NewGuid();

        // Crear organización y categoría válida para la foreign key
        var org = Organization.Create("Club Central");
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();

        var category = Category.Create(org.Id, "Primera División");
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        // Atleta 1: huérfano (sin categoría asignada)
        var orphanAthlete = Athlete.CreateAdult("Lucas", "Gómez Orphan", "lucas.orphan@example.com", "123456");
        orphanAthlete.LinkAccount(accountId);

        // Atleta 2: enrolado en una categoría
        var enrolledAthlete = Athlete.CreateAdult("Lucas", "Gómez Enrolled", "lucas.enrolled@example.com", "123456");
        enrolledAthlete.LinkAccount(accountId);

        await _repository.AddAsync(orphanAthlete);
        await _repository.AddAsync(enrolledAthlete);
        await _context.SaveChangesAsync();

        // Enrolar el segundo atleta en la categoría creada
        var athleteCategory = AthleteCategory.Assign(enrolledAthlete.Id, category.Id);
        _context.AthleteCategories.Add(athleteCategory);
        await _context.SaveChangesAsync();

        var result = await _repository.GetByAccountIdAsync(accountId);

        Assert.NotNull(result);
        Assert.Equal(enrolledAthlete.Id, result.Id);
        Assert.Equal("Lucas Gómez Enrolled", result.FullName);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _sqliteConnection.DisposeAsync();
    }
}

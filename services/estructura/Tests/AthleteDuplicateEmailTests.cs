using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.Athletes.CreateAthlete;
using Application.UseCases.Athletes.UpdateAthleteEmail;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Estructura.Tests;

/// <summary>
/// Alta manual de atletas con un correo que ya usa otro atleta del mismo PF (KAN-660, hallazgo 2).
/// Corre sobre SQLite real para que el filtro global por PF y por borrado lógico se aplique de verdad.
/// </summary>
public sealed class AthleteDuplicateEmailTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _otherPfId = Guid.NewGuid();

    public AthleteDuplicateEmailTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = CreateContext(_pfId);
        context.Database.EnsureCreated();
    }

    private AppDbContext CreateContext(Guid pfId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.PfId).Returns(pfId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        return new AppDbContext(options, currentUser.Object);
    }

    private static CreateAthleteCommand Command(string email) =>
        new("Lucas", "Gómez", email, "1150000000", IsMinor: false);

    private async Task CreateAsync(Guid pfId, string email)
    {
        await using var context = CreateContext(pfId);
        var handler = new CreateAthleteCommandHandler(new AthleteRepository(context), new UnitOfWork(context));
        await handler.HandleAsync(Command(email));
    }

    [Fact]
    public async Task Create_WithoutDuplicate_SavesEmailNormalized()
    {
        await using var context = CreateContext(_pfId);
        var handler = new CreateAthleteCommandHandler(new AthleteRepository(context), new UnitOfWork(context));

        var dto = await handler.HandleAsync(Command("  Lucas.Gomez@Club.COM "));

        Assert.Equal("lucas.gomez@club.com", dto.Email);
    }

    [Fact]
    public async Task Create_WithSameEmail_ThrowsConflict()
    {
        await CreateAsync(_pfId, "lucas@club.com");

        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(_pfId, "lucas@club.com"));

        Assert.Equal("Ya existe un usuario registrado con este correo electrónico.", ex.Message);
        await using var context = CreateContext(_pfId);
        Assert.Equal(1, await context.Athletes.CountAsync());
    }

    [Fact]
    public async Task Create_WithDifferentCasingAndSpaces_ThrowsConflict()
    {
        await CreateAsync(_pfId, "lucas@club.com");

        await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(_pfId, "  LUCAS@Club.com "));
    }

    [Fact]
    public async Task Create_WithEmailStoredInMixedCaseBeforeTheFix_ThrowsConflict()
    {
        // Atletas viejos quedaron guardados solo con Trim: se simula uno escribiendo directo en la base
        await CreateAsync(_pfId, "lucas@club.com");
        await using (var context = CreateContext(_pfId))
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE athletes SET email = 'Lucas@Club.com'");
        }

        await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(_pfId, "lucas@club.com"));
    }

    [Fact]
    public async Task Create_WithSameEmailForAnotherPf_IsAllowed()
    {
        await CreateAsync(_otherPfId, "lucas@club.com");

        await CreateAsync(_pfId, "lucas@club.com");

        await using var context = CreateContext(_pfId);
        Assert.Equal(1, await context.Athletes.CountAsync());
    }

    [Fact]
    public async Task UpdateEmail_ToAnotherAthletesEmail_ThrowsConflict_ButKeepingOwnEmailWorks()
    {
        await CreateAsync(_pfId, "lucas@club.com");
        await CreateAsync(_pfId, "mateo@club.com");

        await using var context = CreateContext(_pfId);
        var mateo = await context.Athletes.SingleAsync(a => a.Email == "mateo@club.com");
        var handler = new UpdateAthleteEmailCommandHandler(new AthleteRepository(context), new UnitOfWork(context));

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.HandleAsync(new UpdateAthleteEmailCommand(mateo.Id, "Lucas@club.com")));

        // Guardar su mismo correo (con otro casing) no choca consigo mismo
        await using var fresh = CreateContext(_pfId);
        var freshHandler = new UpdateAthleteEmailCommandHandler(new AthleteRepository(fresh), new UnitOfWork(fresh));
        var dto = await freshHandler.HandleAsync(new UpdateAthleteEmailCommand(mateo.Id, "MATEO@club.com"));
        Assert.Equal("mateo@club.com", dto.Email);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}

using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Identidad.Tests.Integration;

public sealed class IdentidadPurgeRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IdentidadPurgeRepository _repository;
    private readonly Guid _pfId = Guid.NewGuid();

    public IdentidadPurgeRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(_pfId);

        _context = new AppDbContext(options, currentUserMock.Object);
        _context.Database.EnsureCreated();

        _repository = new IdentidadPurgeRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Given_AthleteUserWithTokensAndPhoto_When_PurgingAthleteWithAccountId_Then_DeletesAllDataAndPreservesOtherUsers()
    {
        // Arrange
        // Estructura tiene un athleteId para el atleta, pero en Identidad la cuenta de usuario se guarda con su propio ID (AccountId) (#204 · KAN-499)
        var estructuraAthleteId = Guid.NewGuid();
        var targetAccountId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        Assert.NotEqual(estructuraAthleteId, targetAccountId);

        var targetUser = new User
        {
            Id = targetAccountId,
            UserName = "atleta@protraining.test",
            Email = "atleta@protraining.test",
            FullName = "Atleta Test Purga",
            PfId = _pfId
        };
        var otherUser = new User
        {
            Id = otherUserId,
            UserName = "otro@protraining.test",
            Email = "otro@protraining.test",
            FullName = "Otro Usuario",
            PfId = _pfId
        };
        _context.Users.AddRange(targetUser, otherUser);

        var targetToken = new RefreshToken
        {
            UserId = targetAccountId,
            Token = "target-token-123",
            DeviceId = "device-1",
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        var otherToken = new RefreshToken
        {
            UserId = otherUserId,
            Token = "other-token-456",
            DeviceId = "device-2",
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        _context.RefreshTokens.AddRange(targetToken, otherToken);

        var targetPhoto = UserPhoto.Create(targetAccountId, [1, 2, 3], "image/webp", 3);
        var otherPhoto = UserPhoto.Create(otherUserId, [4, 5, 6], "image/webp", 3);
        _context.UserPhotos.AddRange(targetPhoto, otherPhoto);

        await _context.SaveChangesAsync();

        // Act: La purga se ejecuta con el AccountId enviado por el job
        var result = await _repository.PurgeAthleteDataAsync(targetAccountId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(targetAccountId, result.AccountId);
        Assert.Equal(1, result.UsersCount);
        Assert.Equal(1, result.RefreshTokensCount);
        Assert.Equal(1, result.PhotosCount);
        Assert.Equal(3, result.TotalRecordsPurged);

        // Verificar que en la base no queda nada del atleta
        Assert.False(await _context.Users.AnyAsync(u => u.Id == targetAccountId));
        Assert.False(await _context.RefreshTokens.AnyAsync(r => r.UserId == targetAccountId));
        Assert.False(await _context.UserPhotos.AnyAsync(p => p.UserId == targetAccountId));

        // Verificar que el otro usuario permanece intacto
        Assert.True(await _context.Users.AnyAsync(u => u.Id == otherUserId));
        Assert.True(await _context.RefreshTokens.AnyAsync(r => r.UserId == otherUserId));
        Assert.True(await _context.UserPhotos.AnyAsync(p => p.UserId == otherUserId));
    }

    [Fact]
    public async Task Given_MismatchedAthleteIdSentInsteadOfAccountId_When_Purging_Then_ReturnsZeroAndPreservesUser()
    {
        // Arrange: Demuestra el bug corregido en KAN-499 donde mandar athleteId de Estructura no borraba la cuenta en Identidad
        var estructuraAthleteId = Guid.NewGuid();
        var targetAccountId = Guid.NewGuid();

        var targetUser = new User
        {
            Id = targetAccountId,
            UserName = "atleta.bug@protraining.test",
            Email = "atleta.bug@protraining.test",
            FullName = "Atleta Bug Purga",
            PfId = _pfId
        };
        _context.Users.Add(targetUser);
        await _context.SaveChangesAsync();

        // Act: Intentar purgar usando el athleteId de Estructura (distinto a targetAccountId)
        var result = await _repository.PurgeAthleteDataAsync(estructuraAthleteId);

        // Assert: Devuelve ceros y NO borra la cuenta
        Assert.Equal(0, result.TotalRecordsPurged);
        Assert.True(await _context.Users.AnyAsync(u => u.Id == targetAccountId));
    }

    [Fact]
    public async Task Given_PurgedAthlete_When_PurgingAgain_Then_ReturnsZeroAndDoesNotThrow()
    {
        // Arrange
        var athleteId = Guid.NewGuid();

        // Act
        var result = await _repository.PurgeAthleteDataAsync(athleteId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(athleteId, result.AccountId);
        Assert.Equal(0, result.UsersCount);
        Assert.Equal(0, result.RefreshTokensCount);
        Assert.Equal(0, result.PhotosCount);
        Assert.Equal(0, result.TotalRecordsPurged);
    }

    [Fact]
    public async Task Given_PfUserWithoutPfId_When_Purging_Then_PfUserIsNotDeleted()
    {
        // Arrange: un usuario PF no tiene PfId asignado
        var pfUserId = Guid.NewGuid();
        var pfUser = new User
        {
            Id = pfUserId,
            UserName = "coach@protraining.test",
            Email = "coach@protraining.test",
            FullName = "Profesor Principal",
            PfId = null
        };
        _context.Users.Add(pfUser);

        var pfPhoto = UserPhoto.Create(pfUserId, [9, 9, 9], "image/webp", 3);
        _context.UserPhotos.Add(pfPhoto);

        var pfToken = new RefreshToken
        {
            UserId = pfUserId,
            Token = "pf-token-123",
            DeviceId = "device-pf",
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        _context.RefreshTokens.Add(pfToken);

        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.PurgeAthleteDataAsync(pfUserId);

        // Assert: no se borra el PF ni sus fotos ni sus tokens
        Assert.Equal(0, result.UsersCount);
        Assert.Equal(0, result.PhotosCount);
        Assert.Equal(0, result.RefreshTokensCount);
        Assert.Equal(0, result.TotalRecordsPurged);
        Assert.True(await _context.Users.AnyAsync(u => u.Id == pfUserId));
        Assert.True(await _context.UserPhotos.AnyAsync(p => p.UserId == pfUserId));
        Assert.True(await _context.RefreshTokens.AnyAsync(r => r.UserId == pfUserId));
    }
}

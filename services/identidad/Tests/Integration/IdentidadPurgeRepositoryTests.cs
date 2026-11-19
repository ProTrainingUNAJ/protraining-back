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
    public async Task Given_AthleteUserWithTokensAndPhoto_When_PurgingAthlete_Then_DeletesAllDataAndPreservesOtherUsers()
    {
        // Arrange
        var targetAthleteId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var targetUser = new User
        {
            Id = targetAthleteId,
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
            UserId = targetAthleteId,
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

        var targetPhoto = UserPhoto.Create(targetAthleteId, [1, 2, 3], "image/webp", 3);
        var otherPhoto = UserPhoto.Create(otherUserId, [4, 5, 6], "image/webp", 3);
        _context.UserPhotos.AddRange(targetPhoto, otherPhoto);

        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.PurgeAthleteDataAsync(targetAthleteId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(targetAthleteId, result.AthleteId);
        Assert.Equal(1, result.UsersCount);
        Assert.Equal(1, result.RefreshTokensCount);
        Assert.Equal(1, result.PhotosCount);
        Assert.Equal(3, result.TotalRecordsPurged);

        // Verificar que en la base no queda nada del atleta
        Assert.False(await _context.Users.AnyAsync(u => u.Id == targetAthleteId));
        Assert.False(await _context.RefreshTokens.AnyAsync(r => r.UserId == targetAthleteId));
        Assert.False(await _context.UserPhotos.AnyAsync(p => p.UserId == targetAthleteId));

        // Verificar que el otro usuario permanece intacto
        Assert.True(await _context.Users.AnyAsync(u => u.Id == otherUserId));
        Assert.True(await _context.RefreshTokens.AnyAsync(r => r.UserId == otherUserId));
        Assert.True(await _context.UserPhotos.AnyAsync(p => p.UserId == otherUserId));
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
        Assert.Equal(athleteId, result.AthleteId);
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
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.PurgeAthleteDataAsync(pfUserId);

        // Assert: no se borra el PF
        Assert.Equal(0, result.UsersCount);
        Assert.Equal(0, result.TotalRecordsPurged);
        Assert.True(await _context.Users.AnyAsync(u => u.Id == pfUserId));
    }
}

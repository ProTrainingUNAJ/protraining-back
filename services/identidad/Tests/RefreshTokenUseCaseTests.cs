using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.DTOs.Auth;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Auth.RefreshToken;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace Identidad.Tests;

public class RefreshTokenUseCaseTests
{
    private readonly Mock<IRefreshTokenRepository> _mockRefreshTokenRepository;
    private readonly Mock<UserManager<User>> _mockUserManager;
    private readonly Mock<IJwtTokenGenerator> _mockJwtTokenGenerator;
    private readonly RefreshTokenUseCase _useCase;

    public RefreshTokenUseCaseTests()
    {
        _mockRefreshTokenRepository = new Mock<IRefreshTokenRepository>();
        _mockUserManager = CreateMockUserManager();
        _mockJwtTokenGenerator = new Mock<IJwtTokenGenerator>();

        _useCase = new RefreshTokenUseCase(
            _mockRefreshTokenRepository.Object,
            _mockUserManager.Object,
            _mockJwtTokenGenerator.Object
        );
    }

    private static Mock<UserManager<User>> CreateMockUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(
            store.Object,
            null!, null!, null!, null!, null!, null!, null!, null!
        );
    }

    [Fact]
    public async Task Given_ExpiredRefreshToken_When_Refresh_Then_Throws_InvalidRefreshTokenException()
    {
        // arrange
        var request = new RefreshRequestDto("token-expirado-123", "device-galaxy-s24");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "atleta@protraining.com",
            IsActive = true
        };

        var expiredToken = new RefreshToken
        {
            Token = request.RefreshToken,
            DeviceId = request.DeviceId,
            UserId = user.Id,
            User = user,
            IsRevoked = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10) // ya expiro
        };

        _mockRefreshTokenRepository.Setup(r => r.GetByTokenAsync(request.RefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredToken);

        // act & assert
        // valida criterio 1: token expirado lanza excepcion de refresh invalido (HTTP 401)
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => _useCase.ExecuteAsync(request));

        // no debe rotar ni guardar nada si el token ya vencio
        _mockRefreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_ValidRefreshToken_When_Refresh_Then_RotatesToken_And_Returns_New_Tokens()
    {
        // arrange
        var request = new RefreshRequestDto("token-valido-123", "device-galaxy-s24");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "atleta@protraining.com",
            IsActive = true
        };

        var existingToken = new RefreshToken
        {
            Token = request.RefreshToken,
            DeviceId = request.DeviceId,
            UserId = user.Id,
            User = user,
            IsRevoked = false,
            ExpiresAt = DateTime.UtcNow.AddDays(15) // todavia vigente
        };

        _mockRefreshTokenRepository.Setup(r => r.GetByTokenAsync(request.RefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _mockUserManager.Setup(m => m.IsLockedOutAsync(user))
            .ReturnsAsync(false);
        _mockUserManager.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Atleta" });
        _mockJwtTokenGenerator.Setup(j => j.GenerateToken(user, "Atleta"))
            .Returns("nuevo.jwt.access.token");

        RefreshToken? rotatedToken = null;
        _mockRefreshTokenRepository.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((rt, _) => rotatedToken = rt)
            .Returns(Task.CompletedTask);
        _mockRefreshTokenRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // act
        var result = await _useCase.ExecuteAsync(request);

        // assert
        Assert.NotNull(result);
        Assert.Equal("nuevo.jwt.access.token", result.AccessToken);
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.NotEqual(request.RefreshToken, result.RefreshToken);

        // rotacion del token requerida por Enzo: el viejo queda revocado
        Assert.True(existingToken.IsRevoked);
        Assert.NotNull(existingToken.RevokedAt);
        Assert.Equal(result.RefreshToken, existingToken.ReplacedByToken);

        // persiste el nuevo token con 30 dias de duracion y el mismo deviceId
        _mockRefreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRefreshTokenRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(rotatedToken);
        Assert.Equal(request.DeviceId, rotatedToken.DeviceId);
        Assert.Equal(user.Id, rotatedToken.UserId);
        Assert.True(rotatedToken.ExpiresAt > DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task Given_DeviceIdMismatch_When_Refresh_Then_Throws_InvalidRefreshTokenException()
    {
        // arrange
        var request = new RefreshRequestDto("token-valido-123", "device-galaxy-s24");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "atleta@protraining.com",
            IsActive = true
        };

        var existingToken = new RefreshToken
        {
            Token = request.RefreshToken,
            DeviceId = "otro-dispositivo-distinto", // device no coincide
            UserId = user.Id,
            User = user,
            IsRevoked = false,
            ExpiresAt = DateTime.UtcNow.AddDays(10)
        };

        _mockRefreshTokenRepository.Setup(r => r.GetByTokenAsync(request.RefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);

        // act & assert
        // no permite renovar sesion desde otro dispositivo
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => _useCase.ExecuteAsync(request));
        _mockRefreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_RevokedRefreshToken_When_Refresh_Then_Throws_InvalidRefreshTokenException()
    {
        // arrange
        var request = new RefreshRequestDto("token-ya-revocado", "device-galaxy-s24");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "atleta@protraining.com",
            IsActive = true
        };

        var existingToken = new RefreshToken
        {
            Token = request.RefreshToken,
            DeviceId = request.DeviceId,
            UserId = user.Id,
            User = user,
            IsRevoked = true, // ya habia sido revocado
            ExpiresAt = DateTime.UtcNow.AddDays(10)
        };

        _mockRefreshTokenRepository.Setup(r => r.GetByTokenAsync(request.RefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);

        // act & assert
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => _useCase.ExecuteAsync(request));
    }

    [Fact]
    public async Task Given_LockedOutUser_When_Refresh_Then_Throws_InvalidRefreshTokenException()
    {
        // arrange
        var request = new RefreshRequestDto("token-valido-123", "device-galaxy-s24");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "bloqueado@protraining.com",
            IsActive = true
        };

        var existingToken = new RefreshToken
        {
            Token = request.RefreshToken,
            DeviceId = request.DeviceId,
            UserId = user.Id,
            User = user,
            IsRevoked = false,
            ExpiresAt = DateTime.UtcNow.AddDays(10)
        };

        _mockRefreshTokenRepository.Setup(r => r.GetByTokenAsync(request.RefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _mockUserManager.Setup(m => m.IsLockedOutAsync(user))
            .ReturnsAsync(true); // usuario bloqueado por intentos fallidos

        // act & assert
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => _useCase.ExecuteAsync(request));
        _mockRefreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

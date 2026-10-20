using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.Security;
using Application.DTOs.Auth;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Auth.Login;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;
namespace Identidad.Tests;

public class LoginUseCaseTests
{
    private readonly Mock<UserManager<User>> _mockUserManager;
    private readonly Mock<IJwtTokenGenerator> _mockJwtTokenGenerator;
    private readonly Mock<IRefreshTokenRepository> _mockRefreshTokenRepository;
    private readonly LoginUseCase _useCase;
    public LoginUseCaseTests()
    {
        _mockUserManager = CreateMockUserManager();
        _mockJwtTokenGenerator = new Mock<IJwtTokenGenerator>();
        _mockRefreshTokenRepository = new Mock<IRefreshTokenRepository>();
        _useCase = new LoginUseCase(
            _mockUserManager.Object,
            _mockJwtTokenGenerator.Object,
            _mockRefreshTokenRepository.Object
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
    public async Task Given_ValidCredentials_When_Login_Then_Returns_Tokens_And_Persists_30Day_RefreshToken_With_DeviceId()
    {
        // arrange
        var request = new LoginRequestDto("atleta@protraining.com", "PasswordSegura123!", "device-galaxy-s24");
        var userId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "atleta@protraining.com",
            UserName = "atleta@protraining.com",
            FullName = "Atleta de Prueba",
            IsActive = true,
            PfId = pfId
        };
        _mockUserManager.Setup(m => m.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.IsLockedOutAsync(user))
            .ReturnsAsync(false);
        _mockUserManager.Setup(m => m.CheckPasswordAsync(user, request.Password))
            .ReturnsAsync(true);
        _mockUserManager.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(["Atleta"]);
        _mockUserManager.Setup(m => m.ResetAccessFailedCountAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _mockJwtTokenGenerator.Setup(j => j.GenerateToken(user, It.IsAny<IEnumerable<string>>()))
            .Returns(new GeneratedToken("fake.jwt.token", "Atleta", 3600));
        RefreshToken? savedToken = null;
        _mockRefreshTokenRepository.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((rt, _) => savedToken = rt)
            .Returns(Task.CompletedTask);
        _mockRefreshTokenRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // act
        var result = await _useCase.ExecuteAsync(request);
        // assert
        Assert.NotNull(result);
        Assert.Equal("fake.jwt.token", result.AccessToken);
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.Equal(3600, result.ExpiresIn);
        Assert.Equal("Atleta", result.User.Role);
        Assert.Equal(pfId, result.User.PfId);
        // resetea el contador de fallos
        _mockUserManager.Verify(m => m.ResetAccessFailedCountAsync(user), Times.Once);
        // guarda el hash del refresh token de 30 dias asociado al deviceId
        _mockRefreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRefreshTokenRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(savedToken);
        Assert.Equal(request.DeviceId, savedToken.DeviceId);
        Assert.Equal(userId, savedToken.UserId);
        Assert.Equal(TokenHasher.Hash(result.RefreshToken), savedToken.Token);
        Assert.True(savedToken.ExpiresAt > DateTime.UtcNow.AddDays(29));
    }
    [Fact]
    public async Task Given_InvalidPassword_When_Login_Then_Increments_AccessFailedCount_And_Throws_InvalidCredentialsException()
    {
        // arrange
        var request = new LoginRequestDto("atleta@protraining.com", "PasswordErronea!", "device-galaxy-s24");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "atleta@protraining.com",
            IsActive = true
        };
        _mockUserManager.Setup(m => m.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.IsLockedOutAsync(user))
            .ReturnsAsync(false);
        _mockUserManager.Setup(m => m.CheckPasswordAsync(user, request.Password))
            .ReturnsAsync(false);
        _mockUserManager.Setup(m => m.AccessFailedAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        // act & assert
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => _useCase.ExecuteAsync(request));
        // suma un intento fallido y no persiste ningun token
        _mockUserManager.Verify(m => m.AccessFailedAsync(user), Times.Once);
        _mockRefreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task Given_LockedOutUser_When_Login_Then_Throws_InvalidCredentialsException_Without_Checking_Password()
    {
        // arrange
        var request = new LoginRequestDto("bloqueado@protraining.com", "Password123!", "device-galaxy-s24");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "bloqueado@protraining.com",
            IsActive = true
        };
        _mockUserManager.Setup(m => m.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.IsLockedOutAsync(user))
            .ReturnsAsync(true);
        // act & assert
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => _useCase.ExecuteAsync(request));
        // si esta bloqueado, no chequea password ni guarda tokens
        _mockUserManager.Verify(m => m.CheckPasswordAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
        _mockRefreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task Given_InactiveOrNonExistentUser_When_Login_Then_Throws_InvalidCredentialsException()
    {
        // arrange
        var request = new LoginRequestDto("noexiste@protraining.com", "Password123!", "device-galaxy-s24");
        _mockUserManager.Setup(m => m.FindByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);
        // act & assert
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => _useCase.ExecuteAsync(request));
    }
}

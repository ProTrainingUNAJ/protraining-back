using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.Security;
using Application.DTOs.Auth;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Auth.RegisterByInvitation;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace Identidad.Tests;

public class RegisterByInvitationUseCaseTests
{
    private readonly Mock<UserManager<User>> _mockUserManager;
    private readonly Mock<IJwtTokenGenerator> _mockJwtTokenGenerator;
    private readonly Mock<IRefreshTokenRepository> _mockRefreshTokenRepository;
    private readonly Mock<IStructureClient> _mockStructureClient;
    private readonly RegisterByInvitationUseCase _useCase;

    public RegisterByInvitationUseCaseTests()
    {
        _mockUserManager = CreateMockUserManager();
        _mockJwtTokenGenerator = new Mock<IJwtTokenGenerator>();
        _mockRefreshTokenRepository = new Mock<IRefreshTokenRepository>();
        _mockStructureClient = new Mock<IStructureClient>();

        _useCase = new RegisterByInvitationUseCase(
            _mockUserManager.Object,
            _mockJwtTokenGenerator.Object,
            _mockRefreshTokenRepository.Object,
            _mockStructureClient.Object
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
    public async Task Given_ValidInvitationAndNewEmail_When_Register_Then_CreatesAthlete_AndReturnsTokens()
    {
        // Arrange
        var request = new RegisterByInvitationRequestDto(
            Email: "nuevo.atleta@protraining.com",
            Password: "PasswordSegura123!",
            FullName: "Mateo Fernández",
            Token: "token-valido-123",
            DeviceId: "device-web-01"
        );

        var categoryId = Guid.NewGuid();
        var invitationDto = new CategoryInvitationDto("token-valido-123", DateTime.UtcNow.AddDays(5), categoryId, "Sub-20");

        _mockStructureClient.Setup(s => s.ValidateInvitationTokenAsync(request.Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitationDto);

        // El usuario no existe previamente
        _mockUserManager.Setup(m => m.FindByEmailAsync(request.Email.ToLowerInvariant()))
            .ReturnsAsync((User?)null);

        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<User>(), request.Password))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(m => m.AddToRoleAsync(It.IsAny<User>(), "Atleta"))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(m => m.GetRolesAsync(It.IsAny<User>()))
            .ReturnsAsync(["Atleta"]);

        _mockJwtTokenGenerator.Setup(j => j.GenerateToken(It.IsAny<User>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new GeneratedToken("fake.jwt.token", "Atleta", 3600));

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("fake.jwt.token", result.AccessToken);
        Assert.Equal("Atleta", result.User.Role);
        Assert.Equal(request.FullName, result.User.FullName);
        _mockUserManager.Verify(m => m.CreateAsync(It.Is<User>(u => u.Email == request.Email.ToLowerInvariant()), request.Password), Times.Once);
        _mockUserManager.Verify(m => m.AddToRoleAsync(It.IsAny<User>(), "Atleta"), Times.Once);
        _mockRefreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_EmailAlreadyExistsWithPreparadorFisicoRole_When_Register_Then_ThrowsPfRoleForbiddenException_AndCreatesNothing()
    {
        // Arrange
        var request = new RegisterByInvitationRequestDto(
            Email: "pf.existente@protraining.com",
            Password: "PasswordSegura123!",
            FullName: "Profesor Existente",
            Token: "token-valido-123"
        );

        var existingPf = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            FullName = request.FullName
        };

        _mockStructureClient.Setup(s => s.ValidateInvitationTokenAsync(request.Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryInvitationDto(request.Token, DateTime.UtcNow.AddDays(5), Guid.NewGuid(), "Sub-20"));

        _mockUserManager.Setup(m => m.FindByEmailAsync(request.Email.ToLowerInvariant()))
            .ReturnsAsync(existingPf);

        _mockUserManager.Setup(m => m.GetRolesAsync(existingPf))
            .ReturnsAsync(["PreparadorFisico"]);

        // Act & Assert (Criterio KAN-397: 403 Forbidden y no se crea nada)
        await Assert.ThrowsAsync<PfRoleForbiddenException>(() => _useCase.ExecuteAsync(request));
        _mockUserManager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Given_EmailAlreadyExistsWithAtletaRole_When_Register_Then_ThrowsAthleteAlreadyExistsException_WithCustomMessage()
    {
        // Arrange
        var request = new RegisterByInvitationRequestDto(
            Email: "atleta.existente@protraining.com",
            Password: "PasswordSegura123!",
            FullName: "Atleta Existente",
            Token: "token-valido-123"
        );

        var existingAthlete = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            FullName = request.FullName
        };

        _mockStructureClient.Setup(s => s.ValidateInvitationTokenAsync(request.Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryInvitationDto(request.Token, DateTime.UtcNow.AddDays(5), Guid.NewGuid(), "Sub-20"));

        _mockUserManager.Setup(m => m.FindByEmailAsync(request.Email.ToLowerInvariant()))
            .ReturnsAsync(existingAthlete);

        _mockUserManager.Setup(m => m.GetRolesAsync(existingAthlete))
            .ReturnsAsync(["Atleta"]);

        // Act & Assert (Criterio KAN-397: 409 Conflict con mensaje "Ya tenés cuenta, iniciá sesión")
        var exception = await Assert.ThrowsAsync<AthleteAlreadyExistsException>(() => _useCase.ExecuteAsync(request));
        Assert.Equal("Ya tenés cuenta, iniciá sesión.", exception.Message);
        _mockUserManager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Given_ExpiredInvitationToken_When_Register_Then_ThrowsInvitationExpiredException()
    {
        // Arrange
        var request = new RegisterByInvitationRequestDto(
            Email: "nuevo@protraining.com",
            Password: "PasswordSegura123!",
            FullName: "Atleta Test",
            Token: "token-expirado"
        );

        _mockStructureClient.Setup(s => s.ValidateInvitationTokenAsync(request.Token, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvitationExpiredException());

        // Act & Assert (Criterio KAN-397: 410 Gone)
        await Assert.ThrowsAsync<InvitationExpiredException>(() => _useCase.ExecuteAsync(request));
        _mockUserManager.Verify(m => m.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        _mockUserManager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Given_InvalidInvitationToken_When_Register_Then_ThrowsInvitationNotFoundException()
    {
        // Arrange
        var request = new RegisterByInvitationRequestDto(
            Email: "nuevo@protraining.com",
            Password: "PasswordSegura123!",
            FullName: "Atleta Test",
            Token: "token-inexistente"
        );

        _mockStructureClient.Setup(s => s.ValidateInvitationTokenAsync(request.Token, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvitationNotFoundException());

        // Act & Assert (Criterio KAN-397: 400 Bad Request)
        await Assert.ThrowsAsync<InvitationNotFoundException>(() => _useCase.ExecuteAsync(request));
        _mockUserManager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }
}
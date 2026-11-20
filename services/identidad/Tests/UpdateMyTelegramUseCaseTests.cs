using Application.DTOs.Users;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.Users.UpdateMyTelegram;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace Identidad.Tests;

public class UpdateMyTelegramUseCaseTests
{
    private readonly Mock<UserManager<User>> _userManager;
    private readonly Mock<IStructureClient> _structureClient = new();

    public UpdateMyTelegramUseCaseTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManager = new Mock<UserManager<User>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    [Fact]
    public async Task Given_Pf_When_UpdatingTelegram_Then_UpdatesIdentityWithoutCallingStructure()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "pf@test.com",
            FullName = "Profe Test",
            IsActive = true,
        };
        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManager.Setup(m => m.IsInRoleAsync(user, "Atleta")).ReturnsAsync(false);
        _userManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var useCase = new UpdateMyTelegramUseCase(_userManager.Object, _structureClient.Object);
        var result = await useCase.ExecuteAsync(user.Id, new UpdateTelegramRequestDto("@profe_ok"), "jwt-pf");

        Assert.Equal("profe_ok", result.TelegramUsername);
        _structureClient.Verify(
            s => s.UpdateMyAthleteTelegramAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _userManager.Verify(m => m.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task Given_Athlete_When_UpdatingTelegram_Then_SyncsStructureAndUpdatesIdentity()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "atleta@test.com",
            FullName = "Atleta Test",
            IsActive = true,
        };
        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManager.Setup(m => m.IsInRoleAsync(user, "Atleta")).ReturnsAsync(true);
        _userManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var useCase = new UpdateMyTelegramUseCase(_userManager.Object, _structureClient.Object);
        var result = await useCase.ExecuteAsync(user.Id, new UpdateTelegramRequestDto("@lucas_atleta"), "jwt-athlete");

        Assert.Equal("lucas_atleta", result.TelegramUsername);
        _structureClient.Verify(
            s => s.UpdateMyAthleteTelegramAsync("lucas_atleta", "jwt-athlete", It.IsAny<CancellationToken>()),
            Times.Once);
        _userManager.Verify(m => m.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task Given_AthleteAndStructureDown_When_UpdatingTelegram_Then_Throws503WithoutUpdatingIdentity()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "atleta@test.com",
            FullName = "Atleta Test",
            TelegramUsername = "anterior_tg",
            IsActive = true,
        };
        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManager.Setup(m => m.IsInRoleAsync(user, "Atleta")).ReturnsAsync(true);
        _structureClient
            .Setup(s => s.UpdateMyAthleteTelegramAsync("nuevo_tg", "jwt-athlete", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StructureUnavailableException());

        var useCase = new UpdateMyTelegramUseCase(_userManager.Object, _structureClient.Object);

        await Assert.ThrowsAsync<StructureUnavailableException>(() =>
            useCase.ExecuteAsync(user.Id, new UpdateTelegramRequestDto("nuevo_tg"), "jwt-athlete"));

        Assert.Equal("anterior_tg", user.TelegramUsername);
        _userManager.Verify(m => m.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Given_InvalidUsername_When_UpdatingTelegram_Then_ThrowsDomainException()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "atleta@test.com", FullName = "Atleta", IsActive = true };
        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);

        var useCase = new UpdateMyTelegramUseCase(_userManager.Object, _structureClient.Object);

        await Assert.ThrowsAsync<DomainException>(() =>
            useCase.ExecuteAsync(user.Id, new UpdateTelegramRequestDto("ab"), "jwt-athlete"));
    }
}

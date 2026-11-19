using Application.DTOs.Users;
using Application.UseCases.Auth.GetPfContact;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace Identidad.Tests;

public class GetPfContactUseCaseTests
{
    private readonly Mock<UserManager<User>> _mockUserManager;
    private readonly GetPfContactUseCase _useCase;

    public GetPfContactUseCaseTests()
    {
        var userStoreMock = new Mock<IUserStore<User>>();
        _mockUserManager = new Mock<UserManager<User>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _useCase = new GetPfContactUseCase(_mockUserManager.Object);
    }

    [Fact]
    public async Task Given_PfWithPhoneOnly_When_Execute_Then_ReturnsPhoneAndNullTelegram()
    {
        var pfId = Guid.NewGuid();
        var pf = new User { Id = pfId, FullName = "Profesor Test", PhoneNumber = "5491123456789", TelegramUsername = null };
        _mockUserManager.Setup(m => m.FindByIdAsync(pfId.ToString())).ReturnsAsync(pf);

        var result = await _useCase.ExecuteAsync(pfId);

        Assert.NotNull(result);
        Assert.Equal("5491123456789", result.PhoneNumber);
        Assert.Null(result.TelegramUsername);
    }

    [Fact]
    public async Task Given_PfWithTelegramOnly_When_Execute_Then_ReturnsTelegramAndNullPhone()
    {
        var pfId = Guid.NewGuid();
        var pf = new User { Id = pfId, FullName = "Profesor Test", PhoneNumber = null, TelegramUsername = "profe_juan" };
        _mockUserManager.Setup(m => m.FindByIdAsync(pfId.ToString())).ReturnsAsync(pf);

        var result = await _useCase.ExecuteAsync(pfId);

        Assert.NotNull(result);
        Assert.Null(result.PhoneNumber);
        Assert.Equal("profe_juan", result.TelegramUsername);
    }

    [Fact]
    public async Task Given_PfWithBothChannels_When_Execute_Then_ReturnsBoth()
    {
        var pfId = Guid.NewGuid();
        var pf = new User { Id = pfId, FullName = "Profesor Test", PhoneNumber = "5491123456789", TelegramUsername = "profe_juan" };
        _mockUserManager.Setup(m => m.FindByIdAsync(pfId.ToString())).ReturnsAsync(pf);

        var result = await _useCase.ExecuteAsync(pfId);

        Assert.Equal(new PfContactDto("5491123456789", "profe_juan"), result);
    }

    [Fact]
    public async Task Given_PfWithoutChannels_When_Execute_Then_ReturnsNull()
    {
        var pfId = Guid.NewGuid();
        var pf = new User { Id = pfId, FullName = "Profesor Test", PhoneNumber = null, TelegramUsername = null };
        _mockUserManager.Setup(m => m.FindByIdAsync(pfId.ToString())).ReturnsAsync(pf);

        var result = await _useCase.ExecuteAsync(pfId);

        Assert.Null(result);
    }

    [Fact]
    public async Task Given_NonExistingPf_When_Execute_Then_ReturnsNull()
    {
        var pfId = Guid.NewGuid();
        _mockUserManager.Setup(m => m.FindByIdAsync(pfId.ToString())).ReturnsAsync((User?)null);

        var result = await _useCase.ExecuteAsync(pfId);

        Assert.Null(result);
    }
}
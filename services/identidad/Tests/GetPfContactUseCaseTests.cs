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
    public async Task Given_PfWithPhone_When_Execute_Then_ReturnsPhone()
    {
        var pfId = Guid.NewGuid();
        var pf = new User { Id = pfId, FullName = "Profesor Test", PhoneNumber = "5491123456789" };
        _mockUserManager.Setup(m => m.FindByIdAsync(pfId.ToString())).ReturnsAsync(pf);

        var result = await _useCase.ExecuteAsync(pfId);

        Assert.Equal("5491123456789", result);
    }

    [Fact]
    public async Task Given_PfWithoutPhone_When_Execute_Then_ReturnsNull()
    {
        var pfId = Guid.NewGuid();
        var pf = new User { Id = pfId, FullName = "Profesor Test", PhoneNumber = null };
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
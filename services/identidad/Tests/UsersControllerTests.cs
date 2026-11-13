using System.Reflection;
using API.Controllers;
using Application.DTOs.Auth;
using Application.DTOs.Users;
using Application.Interfaces.Services;
using Application.UseCases.Auth.GetPfContact;
using Application.UseCases.Auth.UpdatePhone;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Identidad.Tests;

public class UsersControllerTests
{
    private readonly Mock<UserManager<User>> _mockUserManager;
    private readonly Mock<ICurrentUser> _mockCurrentUser = new();
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        var userStoreMock = new Mock<IUserStore<User>>();
        _mockUserManager = new Mock<UserManager<User>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _controller = new UsersController(
            new UpdateWhatsAppPhoneUseCase(_mockUserManager.Object),
            new GetPfContactUseCase(_mockUserManager.Object),
            _mockCurrentUser.Object);
    }

    // Roles que exige el atributo [Authorize] puesto en el método del controller.
    private static string? RolesOf(string methodName) =>
        typeof(UsersController).GetMethod(methodName)!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Single()
            .Roles;

    [Fact]
    public void Given_UpdatePhoneEndpoint_When_CheckingAuthorization_Then_OnlyPreparadorFisicoCanCallIt()
    {
        // Con este rol, el token de un atleta recibe 403 antes de llegar al método.
        Assert.Equal("PreparadorFisico", RolesOf(nameof(UsersController.UpdatePhone)));
    }

    [Fact]
    public void Given_PfContactEndpoint_When_CheckingAuthorization_Then_OnlyAtletaCanCallIt()
    {
        Assert.Equal("Atleta", RolesOf(nameof(UsersController.GetPfContact)));
    }

    [Fact]
    public async Task Given_AthleteToken_When_UpdatePhone_Then_UsesSubAndNeverTouchesThePfAccount()
    {
        // En el token de un atleta el "sub" es su cuenta y el "pf_id" es el id de su preparador.
        var athleteId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        _mockCurrentUser.Setup(c => c.UserId).Returns(athleteId);
        _mockCurrentUser.Setup(c => c.PfId).Returns(pfId);

        var athlete = new User { Id = athleteId, FullName = "Lucas Atleta" };
        _mockUserManager.Setup(m => m.FindByIdAsync(athleteId.ToString())).ReturnsAsync(athlete);
        _mockUserManager.Setup(m => m.UpdateAsync(athlete)).ReturnsAsync(IdentityResult.Success);

        await _controller.UpdatePhone(new UpdatePhoneRequestDto("+54 9 11 2345-6789"));

        // Nunca se busca ni se modifica la cuenta del PF.
        _mockUserManager.Verify(m => m.FindByIdAsync(pfId.ToString()), Times.Never);
        _mockUserManager.Verify(m => m.FindByIdAsync(athleteId.ToString()), Times.Once);
    }

    [Fact]
    public async Task Given_TokenWithoutSub_When_UpdatePhone_Then_ReturnsUnauthorizedAndDoesNotSave()
    {
        _mockCurrentUser.Setup(c => c.UserId).Returns((Guid?)null);
        _mockCurrentUser.Setup(c => c.PfId).Returns(Guid.NewGuid());

        var result = await _controller.UpdatePhone(new UpdatePhoneRequestDto("+54 9 11 2345-6789"));

        Assert.IsType<UnauthorizedResult>(result);
        _mockUserManager.Verify(m => m.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Given_AthleteWhosePfHasPhone_When_GetPfContact_Then_ReturnsThePhoneOfThePfInTheToken()
    {
        var pfId = Guid.NewGuid();
        _mockCurrentUser.Setup(c => c.PfId).Returns(pfId);
        var pf = new User { Id = pfId, FullName = "Profesor Test", PhoneNumber = "5491123456789" };
        _mockUserManager.Setup(m => m.FindByIdAsync(pfId.ToString())).ReturnsAsync(pf);

        var result = await _controller.GetPfContact();

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<PfContactDto>(ok.Value);
        Assert.Equal("5491123456789", dto.PhoneNumber);
    }

    [Fact]
    public async Task Given_PfWithoutPhone_When_GetPfContact_Then_Returns404()
    {
        var pfId = Guid.NewGuid();
        _mockCurrentUser.Setup(c => c.PfId).Returns(pfId);
        var pf = new User { Id = pfId, FullName = "Profesor Test", PhoneNumber = null };
        _mockUserManager.Setup(m => m.FindByIdAsync(pfId.ToString())).ReturnsAsync(pf);

        var result = await _controller.GetPfContact();

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Given_TokenWithoutPfId_When_GetPfContact_Then_ReturnsUnauthorized()
    {
        _mockCurrentUser.Setup(c => c.PfId).Returns((Guid?)null);

        var result = await _controller.GetPfContact();

        Assert.IsType<UnauthorizedResult>(result);
    }
}

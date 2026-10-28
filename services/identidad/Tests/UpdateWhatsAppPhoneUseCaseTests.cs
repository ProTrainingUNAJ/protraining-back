using Application.DTOs.Auth;
using Application.UseCases.Auth.UpdatePhone;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace Identidad.Tests;

public class UpdateWhatsAppPhoneUseCaseTests
{
    private readonly Mock<UserManager<User>> _mockUserManager;
    private readonly UpdateWhatsAppPhoneUseCase _useCase;

    public UpdateWhatsAppPhoneUseCaseTests()
    {
        var userStoreMock = new Mock<IUserStore<User>>();
        _mockUserManager = new Mock<UserManager<User>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _useCase = new UpdateWhatsAppPhoneUseCase(_mockUserManager.Object);
    }


    [Fact]
    public async Task ExecuteAsync_InvalidPhoneNumber_ReturnsFalseWithSpanishErrorAndDoesNotSave()
    {
        var userId = Guid.NewGuid();
        var request = new UpdatePhoneRequestDto("54911abc6789"); // Invalido: contiene letras

        var (success, normalizedPhone, errorMessage) = await _useCase.ExecuteAsync(userId, request);

        Assert.False(success);
        Assert.Null(normalizedPhone);
        Assert.NotNull(errorMessage);
        Assert.Contains("no puede contener letras", errorMessage);

        // Verifica que NUNCA se intenti guardar en la base de datos
        _mockUserManager.Verify(m => m.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_TooShortPhoneNumber_ReturnsFalseAndDoesNotSave()
    {
        var userId = Guid.NewGuid();
        var request = new UpdatePhoneRequestDto("12345"); // Invalido: demasiado corto

        var (success, normalizedPhone, errorMessage) = await _useCase.ExecuteAsync(userId, request);

        Assert.False(success);
        Assert.Null(normalizedPhone);
        Assert.NotNull(errorMessage);
        Assert.Contains("entre 8 y 15 dígitos", errorMessage);

        _mockUserManager.Verify(m => m.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ValidPhoneNumber_NormalizesAndSavesUser()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, FullName = "Profesor Test" };
        var request = new UpdatePhoneRequestDto("+54 9 11 2345-6789");

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager.Setup(m => m.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        var (success, normalizedPhone, errorMessage) = await _useCase.ExecuteAsync(userId, request);

        Assert.True(success);
        Assert.Equal("5491123456789", normalizedPhone);
        Assert.Null(errorMessage);
        Assert.Equal("5491123456789", user.PhoneNumber);

        // Verifica que se llamo a guardar exactamente una vez
        _mockUserManager.Verify(m => m.UpdateAsync(user), Times.Once);
    }
}

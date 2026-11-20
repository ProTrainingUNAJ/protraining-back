using System.Reflection;
using API.Controllers;
using Application.DTOs.Internal;
using Application.Interfaces.Persistence;
using Application.UseCases.Users.PurgeAthlete;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Identidad.Tests;

public sealed class InternalAthletesControllerTests
{
    private readonly Mock<IIdentidadPurgeRepository> _purgeRepoMock = new();
    private readonly InternalAthletesController _controller;

    public InternalAthletesControllerTests()
    {
        var handler = new PurgeAthleteCommandHandler(_purgeRepoMock.Object);
        _controller = new InternalAthletesController(handler);
    }

    [Fact]
    public void Controller_Requires_Servicio_Role()
    {
        var authorizeAttr = typeof(InternalAthletesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.Equal("Servicio", authorizeAttr.Roles);
    }

    [Fact]
    public async Task Given_AccountId_When_PurgingAthlete_Then_InvokesPurgeRepositoryAndReturnsOkWithPurgedCounts()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var expectedDto = new PurgedAthleteDataDto(
            accountId,
            UsersCount: 1,
            RefreshTokensCount: 2,
            PhotosCount: 1,
            TotalRecordsPurged: 4);

        _purgeRepoMock
            .Setup(r => r.PurgeAthleteDataAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDto);

        // Act
        var result = await _controller.PurgeAthlete(accountId, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var actualDto = Assert.IsType<PurgedAthleteDataDto>(okResult.Value);
        Assert.Equal(expectedDto, actualDto);
        _purgeRepoMock.Verify(r => r.PurgeAthleteDataAsync(accountId, It.IsAny<CancellationToken>()), Times.Once);
    }
}

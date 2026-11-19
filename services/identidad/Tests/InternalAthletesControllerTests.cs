using System.Reflection;
using API.Controllers;
using Application.DTOs.Internal;
using Application.Interfaces.Persistence;
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
        _controller = new InternalAthletesController(_purgeRepoMock.Object);
    }

    [Fact]
    public void Controller_Requires_Servicio_Role()
    {
        var authorizeAttr = typeof(InternalAthletesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.Equal("Servicio", authorizeAttr.Roles);
    }

    [Fact]
    public async Task Given_AthleteId_When_PurgingAthlete_Then_InvokesPurgeRepositoryAndReturnsOkWithPurgedCounts()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var expectedDto = new PurgedAthleteDataDto(
            athleteId,
            UsersCount: 1,
            RefreshTokensCount: 2,
            PhotosCount: 1,
            TotalRecordsPurged: 4);

        _purgeRepoMock
            .Setup(r => r.PurgeAthleteDataAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDto);

        // Act
        var result = await _controller.PurgeAthlete(athleteId, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var actualDto = Assert.IsType<PurgedAthleteDataDto>(okResult.Value);
        Assert.Equal(expectedDto, actualDto);
        _purgeRepoMock.Verify(r => r.PurgeAthleteDataAsync(athleteId, It.IsAny<CancellationToken>()), Times.Once);
    }
}

using System.Reflection;
using API.Controllers;
using Application.DTOs.Athletes;
using Application.Interfaces.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Estructura.Tests;

public sealed class InternalAthletesControllerTests
{
    private readonly Mock<IAthleteRepository> _athleteRepoMock = new();
    private readonly Mock<IEstructuraPurgeRepository> _purgeRepoMock = new();
    private readonly InternalAthletesController _controller;

    public InternalAthletesControllerTests()
    {
        _controller = new InternalAthletesController(_athleteRepoMock.Object, _purgeRepoMock.Object);
    }

    [Fact]
    public void Controller_Requires_Servicio_Role()
    {
        var authorizeAttr = typeof(InternalAthletesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.Equal("Servicio", authorizeAttr.Roles);
    }

    [Fact]
    public async Task GetActiveAthletes_WhenCalled_ReturnsOkWithAthletesContainingPfId()
    {
        // Arrange
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var athletes = new List<InternalAthleteSummaryDto>
        {
            new(athleteId, pfId, categoryId, true)
        };

        _athleteRepoMock
            .Setup(r => r.GetActiveAthletesInternalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(athletes);

        // Act
        var result = await _controller.GetActiveAthletes(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedAthletes = Assert.IsAssignableFrom<IReadOnlyList<InternalAthleteSummaryDto>>(okResult.Value);
        Assert.Single(returnedAthletes);
        Assert.Equal(pfId, returnedAthletes[0].PfId);
        Assert.Equal(athleteId, returnedAthletes[0].Id);
        Assert.Equal(categoryId, returnedAthletes[0].CategoryId);
        Assert.True(returnedAthletes[0].Active);
    }
    [Fact]
    public async Task GetActiveAthletes_WhenAthleteHasMultipleCategories_ReturnsAllCategoryIds()
    {
        // Arrange
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var cat1 = Guid.NewGuid();
        var cat2 = Guid.NewGuid();
        var athletes = new List<InternalAthleteSummaryDto>
        {
            new(athleteId, pfId, cat1, true, [cat1, cat2])
        };

        _athleteRepoMock
            .Setup(r => r.GetActiveAthletesInternalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(athletes);

        // Act
        var result = await _controller.GetActiveAthletes(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedAthletes = Assert.IsAssignableFrom<IReadOnlyList<InternalAthleteSummaryDto>>(okResult.Value);
        Assert.Single(returnedAthletes);
        Assert.Equal(cat1, returnedAthletes[0].CategoryId);
        Assert.NotNull(returnedAthletes[0].CategoryIds);
        Assert.Equal(2, returnedAthletes[0].CategoryIds!.Count);
        Assert.Equal(cat1, returnedAthletes[0].CategoryIds![0]);
        Assert.Equal(cat2, returnedAthletes[0].CategoryIds![1]);
    }

    [Fact]
    public async Task Given_AthleteId_When_PurgingAthlete_Then_InvokesPurgeRepositoryAndReturnsNoContent()
    {
        // Arrange
        var athleteId = Guid.NewGuid();

        // Act
        var result = await _controller.PurgeAthlete(athleteId, CancellationToken.None);

        // Assert
        Assert.IsType<NoContentResult>(result);
        _purgeRepoMock.Verify(r => r.PurgeAthleteLocalDataAsync(athleteId, It.IsAny<CancellationToken>()), Times.Once);
    }
}

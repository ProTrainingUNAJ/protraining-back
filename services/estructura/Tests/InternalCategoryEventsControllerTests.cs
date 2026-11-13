using System.Reflection;
using API.Controllers;
using Application.DTOs.Categories;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Estructura.Tests;

public sealed class InternalCategoryEventsControllerTests
{
    private readonly Mock<ICompetitiveEventRepository> _repoMock = new();
    private readonly InternalCategoryEventsController _controller;

    public InternalCategoryEventsControllerTests()
    {
        _controller = new InternalCategoryEventsController(_repoMock.Object);
    }

    [Fact]
    public void Controller_Requires_Servicio_Role()
    {
        var authorizeAttr = typeof(InternalCategoryEventsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.Equal("Servicio", authorizeAttr.Roles);
    }

    [Fact]
    public async Task GetEvents_WhenCalled_ReturnsOkWithCompetitiveEventDtos()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var competitiveEvent = CompetitiveEvent.Create(
            categoryId,
            date,
            "Rival FC",
            CompetitiveEventVenue.Away,
            CompetitiveEventType.Official,
            new TimeOnly(15, 30),
            "Partido fecha 5");

        _repoMock
            .Setup(r => r.GetMatchesByCategoryAndDateRangeInternalAsync(
                categoryId,
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([competitiveEvent]);

        // Act
        var result = await _controller.GetEvents(
            categoryId,
            date.ToString("yyyy-MM-dd"),
            date.ToString("yyyy-MM-dd"),
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedEvents = Assert.IsAssignableFrom<IReadOnlyList<CompetitiveEventDto>>(okResult.Value);
        Assert.Single(returnedEvents);
        Assert.Equal(date, returnedEvents[0].Date);
        Assert.Equal("Rival FC", returnedEvents[0].Opponent);
        Assert.Equal(CompetitiveEventType.Official, returnedEvents[0].Type);
    }
}

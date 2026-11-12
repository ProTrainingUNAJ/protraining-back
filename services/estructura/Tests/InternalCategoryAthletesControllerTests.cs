using System.Reflection;
using API.Controllers;
using Application.DTOs.Athletes;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Estructura.Tests;

// Endpoint interno de atletas de una categoría para la comparación con el grupo (KAN-578)
public sealed class InternalCategoryAthletesControllerTests
{
    private readonly Mock<IAthleteCategoryRepository> _repoMock = new();
    private readonly InternalCategoryAthletesController _controller;

    public InternalCategoryAthletesControllerTests()
    {
        _controller = new InternalCategoryAthletesController(_repoMock.Object);
    }

    [Fact]
    public void Controller_Requires_Servicio_Role()
    {
        var authorizeAttr = typeof(InternalCategoryAthletesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.Equal("Servicio", authorizeAttr.Roles);
    }

    [Fact]
    public async Task GetAthletes_ReturnsOnlyIdAndFullName()
    {
        var categoryId = Guid.NewGuid();
        var athlete = Athlete.CreateAdult("Joaquín", "Díaz", "joaquin@protraining.com", "+5491100000000");
        _repoMock
            .Setup(r => r.ListAthletesByCategoryInternalAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Athlete> { athlete });

        var result = await _controller.GetAthletes(categoryId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var athletes = Assert.IsAssignableFrom<IReadOnlyList<InternalCategoryAthleteDto>>(ok.Value);
        var single = Assert.Single(athletes);
        Assert.Equal(athlete.Id, single.Id);
        Assert.Equal("Joaquín Díaz", single.FullName);
    }
}

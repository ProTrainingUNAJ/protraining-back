using System.Security.Claims;
using API.Controllers.FitnessTests;
using Application.DTOs.FitnessTests;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.FitnessTests.ListAthleteFitnessTests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Seguimiento.Tests;

public class AthleteFitnessTestsControllerTests
{
    private readonly Mock<IFieldTestResultRepository> _repository = new();
    private readonly Mock<IStructureClient> _estructura = new();

    private AthleteFitnessTestsController CreateController(string? role = null)
    {
        var controller = new AthleteFitnessTestsController(
            new ListAthleteFitnessTestsQueryHandler(_repository.Object),
            _estructura.Object);

        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(role))
            claims.Add(new Claim(ClaimTypes.Role, role));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };

        return controller;
    }

    [Fact]
    public async Task List_AtletaSinVincular_LanzaNoAthleteLinkedException()
    {
        var athleteId = Guid.NewGuid();
        _estructura
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteMeDto?)null);

        await Assert.ThrowsAsync<NoAthleteLinkedException>(() =>
            CreateController().List(athleteId, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task List_AtletaOtroId_LanzaAthleteNotFoundException()
    {
        var athleteId = Guid.NewGuid();
        _estructura
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto { AthleteId = Guid.NewGuid() });

        await Assert.ThrowsAsync<AthleteNotFoundException>(() =>
            CreateController().List(athleteId, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task List_AtletaPropio_DevuelveOk()
    {
        var athleteId = Guid.NewGuid();
        _estructura
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto { AthleteId = athleteId });
        _repository
            .Setup(r => r.ListByAthleteAndDateRangeAsync(athleteId, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteFitnessTestDto>());

        var result = await CreateController().List(athleteId, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task List_PfAtletaPropio_DevuelveOk()
    {
        var athleteId = Guid.NewGuid();
        _estructura
            .Setup(s => s.IsOwnAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repository
            .Setup(r => r.ListByAthleteAndDateRangeAsync(athleteId, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteFitnessTestDto>());

        var result = await CreateController("PreparadorFisico").List(athleteId, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task List_PfAtletaAjeno_LanzaAthleteNotFoundException()
    {
        var athleteId = Guid.NewGuid();
        _estructura
            .Setup(s => s.IsOwnAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<AthleteNotFoundException>(() =>
            CreateController("PreparadorFisico").List(athleteId, cancellationToken: CancellationToken.None));
    }
}


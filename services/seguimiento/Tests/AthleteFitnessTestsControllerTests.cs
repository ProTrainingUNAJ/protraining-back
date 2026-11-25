using System.Security.Claims;
using API.Controllers.FitnessTests;
using Application.DTOs.FitnessTests;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Services;
using Application.UseCases.FitnessTests.ListAthleteBestMarks;
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
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.IsInRole(It.IsAny<string>()))
            .Returns<string>(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
        var guard = new AthleteAccessGuard(currentUserMock.Object, _estructura.Object);

        var controller = new AthleteFitnessTestsController(
            new ListAthleteFitnessTestsQueryHandler(_repository.Object),
            new ListAthleteBestMarksQueryHandler(_repository.Object),
            guard);

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

    [Fact]
    public async Task ListBestMarks_AtletaPropio_DevuelveOk()
    {
        var athleteId = Guid.NewGuid();
        var expected = new List<AthleteBestMarkDto>
        {
            new(Guid.NewGuid(), "Carrera 100 m", "Pasadas", 14m, "s")
        };
        _estructura
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto { AthleteId = athleteId });
        _repository
            .Setup(r => r.ListBestMarksByAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await CreateController().ListBestMarks(athleteId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal(expected, ok.Value);
    }

    [Fact]
    public async Task ListBestMarks_PfAtletaPropio_DevuelveOk()
    {
        var athleteId = Guid.NewGuid();
        _estructura
            .Setup(s => s.IsOwnAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repository
            .Setup(r => r.ListBestMarksByAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteBestMarkDto>());

        var result = await CreateController("PreparadorFisico").ListBestMarks(athleteId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task ListBestMarks_PfAtletaAjeno_LanzaAthleteNotFoundException()
    {
        var athleteId = Guid.NewGuid();
        _estructura
            .Setup(s => s.IsOwnAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<AthleteNotFoundException>(() =>
            CreateController("PreparadorFisico").ListBestMarks(athleteId, CancellationToken.None));
    }
}


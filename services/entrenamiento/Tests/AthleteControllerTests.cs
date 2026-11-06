using System.Reflection;
using System.Security.Claims;
using API.Controllers;
using Application.DTOs.AthleteRecords;
using Application.DTOs.Memberships;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Services;
using Application.UseCases.AthleteRecord.ListAthleteRecords;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Tests;

public class AthleteControllerTests
{
    private readonly Mock<IAthleteRecordRepository> _repository = new();
    private readonly Mock<IStructureClient> _structureClient = new();

    private AthleteController CreateController(string? role = null)
    {
        var listHandler = new ListAthleteRecordsQueryHandler(_repository.Object);

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.IsInRole(It.IsAny<string>()))
            .Returns<string>(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
        var guard = new AthleteAccessGuard(currentUserMock.Object, _structureClient.Object);

        var controller = new AthleteController(
            null!,
            listHandler,
            null!,
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
    public async Task GetRecords_RolAtleta_SinPerfilVinculado_LanzaNotFoundException()
    {
        var athleteId = Guid.NewGuid();
        _structureClient
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteMeDto?)null);

        var controller = CreateController("Atleta");

        await Assert.ThrowsAnyAsync<NotFoundException>(() =>
            controller.GetRecords(athleteId, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task GetRecords_RolAtleta_OtroAthleteId_LanzaNotFoundException()
    {
        var athleteId = Guid.NewGuid();
        _structureClient
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto { AthleteId = Guid.NewGuid() });

        var controller = CreateController("Atleta");

        await Assert.ThrowsAnyAsync<NotFoundException>(() =>
            controller.GetRecords(athleteId, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task GetRecords_RolAtleta_MismoAthleteId_DevuelveOk()
    {
        var athleteId = Guid.NewGuid();
        _structureClient
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto { AthleteId = athleteId });

        _repository
            .Setup(r => r.ListAsync(athleteId, true, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteRecord>());

        var controller = CreateController("Atleta");

        var result = await controller.GetRecords(athleteId, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task GetRecords_RolPreparadorFisico_AtletaPropio_DevuelveOk()
    {
        var athleteId = Guid.NewGuid();
        _structureClient
            .Setup(s => s.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athleteId, "Lucas Gómez"));

        _repository
            .Setup(r => r.ListAsync(athleteId, true, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteRecord>());

        var controller = CreateController("PreparadorFisico");

        var result = await controller.GetRecords(athleteId, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task GetRecords_RolPreparadorFisico_AtletaAjeno_LanzaNotFoundException()
    {
        var athleteId = Guid.NewGuid();
        _structureClient
            .Setup(s => s.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StructureAthleteDto?)null);

        var controller = CreateController("PreparadorFisico");

        await Assert.ThrowsAnyAsync<NotFoundException>(() =>
            controller.GetRecords(athleteId, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public void AuthorizeAttributes_EstanConfiguradosCorrectamente()
    {
        // El controlador debe requerir autenticación general
        var controllerAuth = typeof(AthleteController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(controllerAuth);
        Assert.Null(controllerAuth.Roles);

        // GetRecords debe permitir tanto a PF como a Atleta
        var getRecordsAuth = typeof(AthleteController)
            .GetMethod(nameof(AthleteController.GetRecords))?
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(getRecordsAuth);
        Assert.Equal("PreparadorFisico,Atleta", getRecordsAuth.Roles);

        // GetTodayRoutine y SaveRecord deben ser exclusivos de PreparadorFisico
        var todayRoutineAuth = typeof(AthleteController)
            .GetMethod(nameof(AthleteController.GetTodayRoutine))?
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(todayRoutineAuth);
        Assert.Equal("PreparadorFisico", todayRoutineAuth.Roles);

        var saveRecordAuth = typeof(AthleteController)
            .GetMethod(nameof(AthleteController.SaveRecord))?
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(saveRecordAuth);
        Assert.Equal("PreparadorFisico", saveRecordAuth.Roles);
    }
}


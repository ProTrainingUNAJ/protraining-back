using Application.DTOs.FitnessTests;
using Application.Interfaces.Persistence;
using Application.UseCases.FitnessTests.ListFitnessTests;
using Domain.Entities;
using Moq;

namespace Seguimiento.Tests;

// KAN-333: el catálogo de tests se consulta por capacidad motora opcional y siempre
// responde con una lista (vacía si no hay resultados), nunca con un error.
public class ListFitnessTestsQueryHandlerTests
{
    private readonly Mock<ICatalogTestRepository> _catalog = new();

    private ListFitnessTestsQueryHandler CreateHandler() => new(_catalog.Object);

    private static CatalogTest TestDePower(string name = "Salto Vertical CMJ", bool active = true) =>
        new(name, MotorCapacity.Power, "cm", 3, active);

    [Fact]
    public async Task HandleAsync_WithMotorCapacity_PassesFilterToRepositoryAndMapsDtos()
    {
        var cmj = TestDePower();
        var abalakov = TestDePower("Salto Abalakov");
        _catalog
            .Setup(repo => repo.ListAsync(MotorCapacity.Power, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogTest> { cmj, abalakov });

        var result = await CreateHandler().HandleAsync(
            new ListFitnessTestsQuery(MotorCapacity.Power));

        Assert.Equal(2, result.Count);
        Assert.Equal(
            new FitnessTestDto(cmj.Id, "Salto Vertical CMJ", "Power", "cm", 3, true),
            result[0]);
        Assert.Equal(
            new FitnessTestDto(abalakov.Id, "Salto Abalakov", "Power", "cm", 3, true),
            result[1]);
        _catalog.Verify(repo => repo.ListAsync(MotorCapacity.Power, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithoutFilter_RequestsFullCatalog()
    {
        _catalog
            .Setup(repo => repo.ListAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogTest> { TestDePower() });

        var result = await CreateHandler().HandleAsync(new ListFitnessTestsQuery());

        Assert.Single(result);
        _catalog.Verify(repo => repo.ListAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenRepositoryReturnsEmpty_ReturnsEmptyList()
    {
        _catalog
            .Setup(repo => repo.ListAsync(MotorCapacity.Endurance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogTest>());

        var result = await CreateHandler().HandleAsync(
            new ListFitnessTestsQuery(MotorCapacity.Endurance));

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_MapsInactiveTestsKeepingTheirState()
    {
        var desactivado = TestDePower("Test de Abalakov", active: false);
        _catalog
            .Setup(repo => repo.ListAsync(MotorCapacity.Power, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogTest> { desactivado });

        var result = await CreateHandler().HandleAsync(
            new ListFitnessTestsQuery(MotorCapacity.Power));

        var dto = Assert.Single(result);
        Assert.Equal("Power", dto.MotorCapacity);
        Assert.False(dto.Active);
    }
}

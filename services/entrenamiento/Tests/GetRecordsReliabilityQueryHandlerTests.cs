using Application.DTOs.Memberships;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.AthleteRecord.GetRecordsReliability;
using Domain.Entities;
using Moq;

namespace Entrenamiento.Tests;

public class GetRecordsReliabilityQueryHandlerTests
{
    private readonly Mock<IStructureClient> _structureClientMock = new();
    private readonly Mock<IAthleteRecordRepository> _recordRepositoryMock = new();

    private GetRecordsReliabilityQueryHandler CreateHandler() =>
        new(_structureClientMock.Object, _recordRepositoryMock.Object);

    [Fact]
    public async Task Given_AtletaConRecordsDeVariosOrigenesYAtletaSinDatos_When_ConsultaConfiabilidad_Then_RepiteAtletaEnCadaGrupo()
    {
        var categoryId = Guid.NewGuid();
        var martinId = Guid.NewGuid();
        var jesicaId = Guid.NewGuid();

        SetupCategory(categoryId, (martinId, "Martín"), (jesicaId, "Jesica"));

        // Martín: 1 testeado, 2 reportados y 5 estimados.
        SetupRecords(martinId, [
            CreateRecord(martinId, RecordSource.Tested),
            CreateRecord(martinId, RecordSource.Reported),
            CreateRecord(martinId, RecordSource.Reported),
            CreateRecord(martinId, RecordSource.Estimated),
            CreateRecord(martinId, RecordSource.Estimated),
            CreateRecord(martinId, RecordSource.Estimated),
            CreateRecord(martinId, RecordSource.Estimated),
            CreateRecord(martinId, RecordSource.Estimated),
        ]);

        // Jesica no tiene ningún récord vigente.
        SetupRecords(jesicaId, []);

        var result = await CreateHandler().Handle(new GetRecordsReliabilityQuery(categoryId));

        Assert.Equal(2, result.TotalAthletes);
        Assert.Equal(8, result.TotalRecords);

        var tested = Group(result, "Tested");
        Assert.Equal(1, tested.AthleteCount);
        Assert.Equal(1, tested.RecordCount);
        var testedMartin = Assert.Single(tested.Athletes);
        Assert.Equal("Martín", testedMartin.Name);
        Assert.Equal(1, testedMartin.RecordCount);
        Assert.Equal(12.5, tested.Percentage, 3);

        var reported = Group(result, "Reported");
        Assert.Equal(1, reported.AthleteCount);
        Assert.Equal(2, reported.RecordCount);
        var reportedMartin = Assert.Single(reported.Athletes);
        Assert.Equal("Martín", reportedMartin.Name);
        Assert.Equal(2, reportedMartin.RecordCount);
        Assert.Equal(25, reported.Percentage, 3);

        var estimated = Group(result, "Estimated");
        Assert.Equal(1, estimated.AthleteCount);
        Assert.Equal(5, estimated.RecordCount);
        var estimatedMartin = Assert.Single(estimated.Athletes);
        Assert.Equal("Martín", estimatedMartin.Name);
        Assert.Equal(5, estimatedMartin.RecordCount);
        Assert.Equal(62.5, estimated.Percentage, 3);

        var noData = Group(result, "NoData");
        Assert.Equal(1, noData.AthleteCount);
        Assert.Equal(0, noData.RecordCount);
        var noDataJesica = Assert.Single(noData.Athletes);
        Assert.Equal("Jesica", noDataJesica.Name);
        Assert.Equal(0, noDataJesica.RecordCount);
        Assert.Equal(50, noData.Percentage, 3);
    }

    [Fact]
    public async Task Given_AtletaConRecordEpley_When_ConsultaConfiabilidad_Then_LoCueentaComoTesteado()
    {
        var categoryId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();

        SetupCategory(categoryId, (athleteId, "Ana"));
        SetupRecords(athleteId, [CreateRecord(athleteId, RecordSource.Epley)]);

        var result = await CreateHandler().Handle(new GetRecordsReliabilityQuery(categoryId));

        var tested = Group(result, "Tested");
        Assert.Equal(1, tested.AthleteCount);
        Assert.Equal(1, tested.RecordCount);
        Assert.Equal(1, Assert.Single(tested.Athletes).RecordCount);
        Assert.Equal(0, Group(result, "Reported").AthleteCount);
        Assert.Equal(0, Group(result, "Estimated").AthleteCount);
        Assert.Equal(0, Group(result, "NoData").AthleteCount);
    }

    [Fact]
    public async Task Given_FiltroPorEjercicio_When_ConsultaConfiabilidad_Then_SoloCuentaElRecordDelEjercicio()
    {
        var categoryId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var otherExerciseId = Guid.NewGuid();

        SetupCategory(categoryId, (athleteId, "Ana"));
        SetupRecords(athleteId, [
            CreateRecord(athleteId, RecordSource.Tested, otherExerciseId),
            CreateRecord(athleteId, RecordSource.Estimated, exerciseId),
        ]);

        var result = await CreateHandler().Handle(
            new GetRecordsReliabilityQuery(categoryId, exerciseId));

        Assert.Equal(1, result.TotalRecords);
        Assert.Equal(0, Group(result, "Tested").AthleteCount);
        Assert.Equal(1, Group(result, "Estimated").AthleteCount);
        Assert.Equal(1, Group(result, "Estimated").RecordCount);
        Assert.Equal(0, Group(result, "NoData").AthleteCount);
    }

    [Fact]
    public async Task Given_FiltroPorEjercicioSinRecord_When_ConsultaConfiabilidad_Then_AtletaQuedaEnSinDato()
    {
        var categoryId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var exerciseWithoutRecordId = Guid.NewGuid();

        SetupCategory(categoryId, (athleteId, "Ana"));
        SetupRecords(athleteId, [CreateRecord(athleteId, RecordSource.Tested)]);

        var result = await CreateHandler().Handle(
            new GetRecordsReliabilityQuery(categoryId, exerciseWithoutRecordId));

        Assert.Equal(0, result.TotalRecords);
        Assert.Equal(0, Group(result, "Tested").AthleteCount);
        Assert.Equal(1, Group(result, "NoData").AthleteCount);
        Assert.Equal(0, Assert.Single(Group(result, "NoData").Athletes).RecordCount);
    }

    [Fact]
    public async Task Given_CategoriaCon12Testeados3Reportados4Estimados1SinDato_When_ConsultaConfiabilidad_Then_SumaGruposEs20YPorcentajesCoinciden()
    {
        var categoryId = Guid.NewGuid();

        var athletes = new List<(Guid Id, string Name)>();
        for (var i = 1; i <= 20; i++)
            athletes.Add((Guid.NewGuid(), $"Atleta {i}"));

        SetupCategory(categoryId, athletes.ToArray());

        // 12 atletas testeados (un récord testeado cada uno).
        for (var i = 0; i < 12; i++)
            SetupRecords(athletes[i].Id, [CreateRecord(athletes[i].Id, RecordSource.Tested)]);

        // 3 atletas reportados.
        for (var i = 12; i < 15; i++)
            SetupRecords(athletes[i].Id, [CreateRecord(athletes[i].Id, RecordSource.Reported)]);

        // 4 atletas estimados.
        for (var i = 15; i < 19; i++)
            SetupRecords(athletes[i].Id, [CreateRecord(athletes[i].Id, RecordSource.Estimated)]);

        // 1 atleta sin dato.
        SetupRecords(athletes[19].Id, []);

        var result = await CreateHandler().Handle(new GetRecordsReliabilityQuery(categoryId));

        Assert.Equal(20, result.TotalAthletes);
        Assert.Equal(19, result.TotalRecords);

        var tested = Group(result, "Tested");
        Assert.Equal(12, tested.AthleteCount);
        Assert.Equal(12, tested.RecordCount);
        Assert.Equal(63.158, tested.Percentage, 3);

        var reported = Group(result, "Reported");
        Assert.Equal(3, reported.AthleteCount);
        Assert.Equal(3, reported.RecordCount);
        Assert.Equal(15.789, reported.Percentage, 3);

        var estimated = Group(result, "Estimated");
        Assert.Equal(4, estimated.AthleteCount);
        Assert.Equal(4, estimated.RecordCount);
        Assert.Equal(21.053, estimated.Percentage, 3);

        var noData = Group(result, "NoData");
        Assert.Equal(1, noData.AthleteCount);
        Assert.Equal(0, noData.RecordCount);
        Assert.Equal(5.0, noData.Percentage, 3);

        var totalAthletesInGroups = result.Groups.Sum(g => g.AthleteCount);
        Assert.Equal(20, totalAthletesInGroups);
    }

    private void SetupCategory(Guid categoryId, params (Guid Id, string Name)[] athletes)
    {
        _structureClientMock
            .Setup(c => c.IsOwnCategoryAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _structureClientMock
            .Setup(c => c.GetCategoryAthletesAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athletes.Select(a => a.Id).ToList());

        _structureClientMock
            .Setup(c => c.GetAthleteByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new StructureAthleteDto(id, athletes.First(a => a.Id == id).Name));
    }

    private void SetupRecords(Guid athleteId, List<Domain.Entities.AthleteRecord> records)
    {
        _recordRepositoryMock
            .Setup(r => r.ListAsync(athleteId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);
    }

    private static Domain.Entities.AthleteRecord CreateRecord(
        Guid athleteId,
        RecordSource source,
        Guid? exerciseId = null) =>
        Domain.Entities.AthleteRecord.Create(
            athleteId,
            exerciseId ?? Guid.NewGuid(),
            100m,
            source,
            DateTime.UtcNow);

    private static Application.DTOs.AthleteRecords.RecordReliabilityGroupDto Group(
        Application.DTOs.AthleteRecords.RecordReliabilitySummaryDto summary,
        string group) =>
        summary.Groups.Single(g => g.Group == group);
}

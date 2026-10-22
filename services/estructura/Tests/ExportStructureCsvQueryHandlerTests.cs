using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using Application.UseCases.Export.ExportStructureCsv;
using Domain.Entities;
using Moq;
using Xunit;

namespace Estructura.Tests;

public class ExportStructureCsvQueryHandlerTests
{
    private readonly Mock<IOrganizationRepository> _mockOrgRepo;
    private readonly Mock<ICategoryRepository> _mockCatRepo;
    private readonly Mock<IAthleteRepository> _mockAthleteRepo;
    private readonly Mock<IAthleteCategoryRepository> _mockAthleteCategoryRepo;
    private readonly ExportStructureCsvQueryHandler _handler;

    public ExportStructureCsvQueryHandlerTests()
    {
        _mockOrgRepo = new Mock<IOrganizationRepository>();
        _mockCatRepo = new Mock<ICategoryRepository>();
        _mockAthleteRepo = new Mock<IAthleteRepository>();
        _mockAthleteCategoryRepo = new Mock<IAthleteCategoryRepository>();

        // por defecto devuelve lista vacia de categorias por atleta
        _mockAthleteCategoryRepo.Setup(r => r.ListByAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteCategory>());

        _handler = new ExportStructureCsvQueryHandler(
            _mockOrgRepo.Object,
            _mockCatRepo.Object,
            _mockAthleteRepo.Object,
            _mockAthleteCategoryRepo.Object
        );
    }

    [Fact]
    public async Task Given_ExistingData_When_ExportCsv_Then_Returns_File_With_All_Sections_And_Data()
    {
        // arrange
        var org = Organization.Create("Sede Central", "Sede principal deportiva");
        var cat = Category.Create(org.Id, "Sub-20", "Division juvenil");
        var athlete = Athlete.CreateAdult("Lionel", "Messi", "lio@messi.com", "+5491100000000");
        var athleteCategory = AthleteCategory.Assign(athlete.Id, cat.Id);

        _mockOrgRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization> { org });

        _mockCatRepo.Setup(r => r.ListByOrganizationAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Category> { cat });

        _mockAthleteRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Athlete> { athlete });

        _mockAthleteCategoryRepo.Setup(r => r.ListByAthleteAsync(athlete.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteCategory> { athleteCategory });

        // act
        var result = await _handler.HandleAsync(new ExportStructureCsvQuery());

        // assert
        Assert.NotNull(result);
        Assert.Equal("text/csv; charset=utf-8", result.ContentType);
        Assert.StartsWith("estructura_export_", result.FileName);
        Assert.EndsWith(".csv", result.FileName);

        // decodifica los bytes para verificar las secciones generadas
        var csvContent = Encoding.UTF8.GetString(result.FileBytes);

        // verifica presencia de organizaciones
        Assert.Contains("# ORGANIZACIONES", csvContent);
        Assert.Contains("Sede Central", csvContent);

        // verifica presencia de categorias
        Assert.Contains("# CATEGORIAS", csvContent);
        Assert.Contains("Sub-20", csvContent);

        // verifica presencia de atletas y ausencia de datos médicos (medical_conditions)
        Assert.Contains("# ATLETAS", csvContent);
        Assert.DoesNotContain("medical_conditions", csvContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lionel", csvContent);
        Assert.Contains("Messi", csvContent);
        Assert.Contains("lio@messi.com", csvContent);

        // verifica presencia de seccion y relacion de categorias (#017)
        Assert.Contains("# ATLETAS_CATEGORIAS", csvContent);
        Assert.Contains($"{athlete.Id},{cat.Id}", csvContent);
    }

    [Fact]
    public async Task Given_NoData_When_ExportCsv_Then_Returns_File_With_Headers_Only()
    {
        // arrange
        _mockOrgRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization>());

        _mockAthleteRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Athlete>());

        // act
        var result = await _handler.HandleAsync(new ExportStructureCsvQuery());

        // assert
        Assert.NotNull(result);
        var csvContent = Encoding.UTF8.GetString(result.FileBytes);

        // deben estar los encabezados aunque no haya filas
        Assert.Contains("# ORGANIZACIONES", csvContent);
        Assert.Contains("# CATEGORIAS", csvContent);
        Assert.Contains("# ATLETAS", csvContent);
        Assert.Contains("# ATLETAS_CATEGORIAS", csvContent);
    }

    [Fact]
    public async Task Given_TextsWithCommas_When_ExportCsv_Then_Escapes_With_Quotes()
    {
        // arrange
        // prueba que los textos con comas se encierren entre comillas para no romper el csv
        var org = Organization.Create("Club Social, Deportivo y Cultural", "Descripcion con, comas");

        _mockOrgRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization> { org });

        _mockCatRepo.Setup(r => r.ListByOrganizationAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Category>());

        _mockAthleteRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Athlete>());

        // act
        var result = await _handler.HandleAsync(new ExportStructureCsvQuery());

        // assert
        var csvContent = Encoding.UTF8.GetString(result.FileBytes);
        Assert.Contains("\"Club Social, Deportivo y Cultural\"", csvContent);
        Assert.Contains("\"Descripcion con, comas\"", csvContent);
    }

    [Fact]
    public async Task Given_PhoneStartingWithPlus_When_ExportCsv_Then_Neutralizes_Formula_With_SingleQuote()
    {
        // arrange
        // prueba que los telefonos con +54 se neutralicen con una comilla simple para evitar formula injection en excel
        var athlete = Athlete.CreateAdult("Lionel", "Messi", "lio@messi.com", "+5491100000000");

        _mockOrgRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization>());

        _mockAthleteRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Athlete> { athlete });

        // act
        var result = await _handler.HandleAsync(new ExportStructureCsvQuery());

        // assert
        var csvContent = Encoding.UTF8.GetString(result.FileBytes);
        Assert.Contains("'+5491100000000", csvContent);
    }
}

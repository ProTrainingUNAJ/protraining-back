using Application.Interfaces.Persistence;
using Application.UseCases.Exercise.ListExercises;
using Domain.Entities;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Tests;

// KAN-549: ilustraciones de terceros para los ejercicios del catálogo.
public class ExerciseMediaTests
{
    private const string CatalogImagesBase = "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/";

    private const string MediaJson = """
        [
          {
            "exerciseId": "Barbell_Squat",
            "repdb": { "frames": ["https://repdb/squat-start.webp", "https://repdb/squat-peak.webp"] },
            "everkinetic": { "frames": ["https://ek/0001-relaxation.png", "https://ek/0001-tension.png"] }
          },
          {
            "exerciseId": "Leg_Press",
            "everkinetic": { "frames": ["https://ek/0002-relaxation.png", "https://ek/0002-tension.png"] }
          },
          { "exerciseId": "Sin_Nada" }
        ]
        """;

    private static List<string> CatalogImages(string catalogId) =>
        [$"{CatalogImagesBase}{catalogId}/0.jpg", $"{CatalogImagesBase}{catalogId}/1.jpg"];

    // Arrange: un ejercicio con las dos fuentes. Act: se buscan sus ilustraciones por sus fotos.
    // Assert: vienen en orden de preferencia (RepDB → Everkinetic), con su crédito.
    [Fact]
    public void GetForCatalogImages_ConLasDosFuentes_LasDevuelveEnOrdenDePreferencia()
    {
        var catalog = ExerciseMediaCatalog.FromJson(MediaJson);

        var media = catalog.GetForCatalogImages(CatalogImages("Barbell_Squat"));

        Assert.Equal(["repdb", "everkinetic"], media.Select(m => m.Source));
        Assert.All(media, m => Assert.Equal("frames", m.Kind));
        Assert.Equal(["https://repdb/squat-start.webp", "https://repdb/squat-peak.webp"], media[0].Urls);
        Assert.Equal("Exercise data by RepDB (repdb.co)", media[0].Credit);
        Assert.Equal("CC BY-SA 4.0", media[1].License);
    }

    [Fact]
    public void GetForCatalogImages_ConUnaSolaFuente_DevuelveSoloEsa()
    {
        var catalog = ExerciseMediaCatalog.FromJson(MediaJson);

        var media = catalog.GetForCatalogImages(CatalogImages("Leg_Press"));

        var only = Assert.Single(media);
        Assert.Equal("everkinetic", only.Source);
    }

    // Un ejercicio sin par, una entrada sin fuentes o un ejercicio sin fotos: lista vacía, sin errores.
    [Theory]
    [InlineData("Ejercicio_Que_No_Esta")]
    [InlineData("Sin_Nada")]
    public void GetForCatalogImages_SinPar_DevuelveVacio(string catalogId)
    {
        var catalog = ExerciseMediaCatalog.FromJson(MediaJson);

        Assert.Empty(catalog.GetForCatalogImages(CatalogImages(catalogId)));
    }

    [Fact]
    public void GetForCatalogImages_SinFotos_DevuelveVacio()
    {
        var catalog = ExerciseMediaCatalog.FromJson(MediaJson);

        Assert.Empty(catalog.GetForCatalogImages([]));
    }

    // Si falta el archivo, el servicio arranca igual y los ejercicios se ven con sus fotos.
    [Fact]
    public void FromFile_SiNoExisteElArchivo_QuedaVacioSinFallar()
    {
        var catalog = ExerciseMediaCatalog.FromFile(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json"));

        Assert.Empty(catalog.GetForCatalogImages(CatalogImages("Barbell_Squat")));
    }

    // El archivo real que se publica con el servicio: tiene que leerse y cubrir los ejercicios de la demo.
    [Theory]
    [InlineData("Barbell_Squat")]
    [InlineData("Barbell_Bench_Press_-_Medium_Grip")]
    [InlineData("Romanian_Deadlift")]
    public void FromFile_ArchivoDelServicio_CubreLosEjerciciosDeLaDemo(string catalogId)
    {
        var catalog = ExerciseMediaCatalog.FromFile(Path.Combine(AppContext.BaseDirectory, "Data", "exercise-media.json"));

        Assert.NotEmpty(catalog.GetForCatalogImages(CatalogImages(catalogId)));
    }

    // Arrange: un ejercicio del catálogo y uno cargado por el PF. Act: se lista el catálogo.
    // Assert: solo el del catálogo trae ilustraciones; el del PF sigue con sus fotos.
    [Fact]
    public async Task ListExercises_SoloElCatalogoGlobalTraeIlustraciones()
    {
        var catalogExercise = Exercise.Create("Sentadilla con barra", "Piernas",
            images: CatalogImages("Barbell_Squat"), origin: Origin.Catalog);
        var coachExercise = Exercise.Create("Sentadilla del PF", "Piernas",
            images: CatalogImages("Barbell_Squat"), origin: Origin.Coach);

        var repoMock = new Mock<IExerciseRepository>();
        repoMock.Setup(r => r.ListAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ExerciseType?>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(([catalogExercise, coachExercise], 2));
        var videoRepoMock = new Mock<IExerciseVideoRepository>();
        videoRepoMock.Setup(r => r.GetVideosByExerciseIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var handler = new ListExercisesQueryHandler(repoMock.Object, videoRepoMock.Object, ExerciseMediaCatalog.FromJson(MediaJson));

        var result = await handler.Handle(new ListExercisesQuery { Page = 1, PageSize = 20 });

        Assert.Equal(2, result.Items.Single(e => e.Id == catalogExercise.Id).Media.Count);
        Assert.Empty(result.Items.Single(e => e.Id == coachExercise.Id).Media);
    }
}

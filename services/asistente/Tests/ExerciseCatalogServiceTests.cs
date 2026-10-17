using System.Net;
using System.Text.Json;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Infrastructure.Services.Catalog;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas unitarias y de integración para ExerciseCatalogService (KAN-530):
/// 1. Verificación del funcionamiento real de la caché desde el contenedor de DI.
/// 2. Eliminación de la llamada inexistente con ?ids= en FindByIdAsync.
/// 3. Aislamiento estricto de claves de caché por preparador físico (ICurrentUser.PfId).
/// </summary>
public sealed class ExerciseCatalogServiceTests
{
    private static readonly Guid SentadillaId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string CreatePaginatedResponseJson(Guid id, string name, string muscleGroup, string equipment)
    {
        return JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    id,
                    name,
                    muscleGroup,
                    equipment
                }
            },
            page = 1,
            pageSize = 50,
            totalCount = 1,
            totalPages = 1
        });
    }

    /// <summary>
    /// Criterio de aceptación 1 (KAN-530):
    /// DADO el Asistente armado como en Program.cs (ServiceCollection con AddMemoryCache y AddHttpClient)
    /// CUANDO se consulta dos veces el mismo ejercicio
    /// ENTONCES Entrenamiento recibe una sola llamada HTTP (el caché en memoria funciona de verdad).
    /// </summary>
    [Fact]
    public async Task Given_DiContainer_AsInProgram_When_Validating_Same_Exercise_Twice_Then_Entrenamiento_Called_Only_Once()
    {
        // Arrange
        var fakeHandler = new TrackingHttpMessageHandler(
            CreatePaginatedResponseJson(SentadillaId, "Sentadilla trasera con barra", "Piernas", "Barra"));

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(c => c.PfId).Returns(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton(currentUserMock.Object);

        // Registro idéntico a Program.cs
        services.AddHttpClient<IExerciseCatalogService, ExerciseCatalogService>(client =>
        {
            client.BaseAddress = new Uri("http://entrenamiento:8080/");
        }).ConfigurePrimaryHttpMessageHandler(() => fakeHandler);

        var provider = services.BuildServiceProvider();
        var catalogService = provider.GetRequiredService<IExerciseCatalogService>();

        // Act
        // Primera consulta: debe consultar por HTTP a Entrenamiento y poblar el caché
        var firstResult = await catalogService.FindByNameAsync("Sentadilla trasera con barra");

        // Segunda consulta idéntica: debe responder directamente desde IMemoryCache
        var secondResult = await catalogService.FindByNameAsync("Sentadilla trasera con barra");

        // Assert
        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);
        Assert.Equal(SentadillaId, firstResult.Id);
        Assert.Equal(SentadillaId, secondResult.Id);

        // Clave del criterio de aceptación: Entrenamiento recibió UNA SOLA llamada
        Assert.Equal(1, fakeHandler.CallCount);
    }

    /// <summary>
    /// Criterio de aceptación 2 (KAN-530):
    /// DADO dos preparadores físicos con el mismo nombre de ejercicio
    /// CUANDO se valida
    /// ENTONCES cada uno consulta su propio catálogo (no comparten el caché globalmente).
    /// </summary>
    [Fact]
    public async Task Given_Two_Different_Pfs_When_Validating_Same_Exercise_Name_Then_Both_Query_Entrenamiento_Independently()
    {
        // Arrange
        var fakeHandler = new TrackingHttpMessageHandler(
            CreatePaginatedResponseJson(SentadillaId, "Sentadilla búlgara", "Piernas", "Mancuernas"));

        var pfId1 = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var pfId2 = Guid.Parse("99999999-8888-7777-6666-555555555555");

        var currentUserMock1 = new Mock<ICurrentUser>();
        currentUserMock1.Setup(c => c.PfId).Returns(pfId1);

        var currentUserMock2 = new Mock<ICurrentUser>();
        currentUserMock2.Setup(c => c.PfId).Returns(pfId2);

        var sharedMemoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpClient = new HttpClient(fakeHandler) { BaseAddress = new Uri("http://entrenamiento:8080/") };

        var servicePf1 = new ExerciseCatalogService(
            httpClient,
            sharedMemoryCache,
            currentUserMock1.Object,
            NullLogger<ExerciseCatalogService>.Instance);

        var servicePf2 = new ExerciseCatalogService(
            httpClient,
            sharedMemoryCache,
            currentUserMock2.Object,
            NullLogger<ExerciseCatalogService>.Instance);

        // Act
        // 1. PF 1 consulta "Sentadilla búlgara" -> Genera 1 llamada y cachea bajo cat_ex_name_{pfId1}_...
        var resultPf1 = await servicePf1.FindByNameAsync("Sentadilla búlgara");

        // 2. PF 2 consulta "Sentadilla búlgara" -> NO debe tomar la entrada de PF 1, debe hacer su propia llamada HTTP
        var resultPf2 = await servicePf2.FindByNameAsync("Sentadilla búlgara");

        // Assert
        Assert.NotNull(resultPf1);
        Assert.NotNull(resultPf2);

        // Ambas consultas se ejecutaron contra Entrenamiento porque las claves están aisladas por PF
        Assert.Equal(2, fakeHandler.CallCount);

        // Una tercera consulta para PF 1 usa su propio caché y NO incrementa las llamadas
        var resultPf1Second = await servicePf1.FindByNameAsync("Sentadilla búlgara");
        Assert.NotNull(resultPf1Second);
        Assert.Equal(2, fakeHandler.CallCount);
    }

    /// <summary>
    /// Criterio de aceptación 3 (KAN-530):
    /// No queda ninguna llamada con ?ids=. FindByIdAsync devuelve null si no está en memoria
    /// y devuelve el elemento si ya fue cargado por nombre, sin realizar peticiones HTTP no soportadas.
    /// </summary>
    [Fact]
    public async Task Given_FindByIdAsync_When_Not_In_Cache_Then_ReturnsNull_And_Makes_Zero_Http_Calls()
    {
        // Arrange
        var fakeHandler = new TrackingHttpMessageHandler(
            CreatePaginatedResponseJson(SentadillaId, "Sentadilla con barra", "Piernas", "Barra"));

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(c => c.PfId).Returns(Guid.NewGuid());

        var cache = new MemoryCache(new MemoryCacheOptions());
        var httpClient = new HttpClient(fakeHandler) { BaseAddress = new Uri("http://entrenamiento:8080/") };

        var service = new ExerciseCatalogService(
            httpClient,
            cache,
            currentUserMock.Object,
            NullLogger<ExerciseCatalogService>.Instance);

        var randomExerciseId = Guid.NewGuid();

        // Act
        var result = await service.FindByIdAsync(randomExerciseId);

        // Assert
        // Debe devolver null inmediatamente sin realizar ninguna petición HTTP a Entrenamiento
        Assert.Null(result);
        Assert.Equal(0, fakeHandler.CallCount);
        Assert.DoesNotContain(fakeHandler.RequestedUrls, url => url.Contains("?ids="));
    }

    /// <summary>
    /// Criterio de aceptación 3 (Variante Con Caché):
    /// Si el ejercicio ya fue cacheado por una búsqueda previa por nombre, FindByIdAsync lo recupera
    /// de memoria instantáneamente con 0 llamadas HTTP adicionales.
    /// </summary>
    [Fact]
    public async Task Given_Exercise_Previously_Cached_When_FindByIdAsync_Then_Returns_Cached_Item_Without_Http_Call()
    {
        // Arrange
        var fakeHandler = new TrackingHttpMessageHandler(
            CreatePaginatedResponseJson(SentadillaId, "Sentadilla trasera con barra", "Piernas", "Barra"));

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(c => c.PfId).Returns(Guid.NewGuid());

        var cache = new MemoryCache(new MemoryCacheOptions());
        var httpClient = new HttpClient(fakeHandler) { BaseAddress = new Uri("http://entrenamiento:8080/") };

        var service = new ExerciseCatalogService(
            httpClient,
            cache,
            currentUserMock.Object,
            NullLogger<ExerciseCatalogService>.Instance);

        // 1. Poblamos el catálogo en memoria buscando por nombre
        var item = await service.FindByNameAsync("Sentadilla trasera con barra");
        Assert.NotNull(item);
        Assert.Equal(1, fakeHandler.CallCount);

        // 2. Act: Consultamos por ID
        var byIdResult = await service.FindByIdAsync(SentadillaId);

        // Assert
        Assert.NotNull(byIdResult);
        Assert.Equal(SentadillaId, byIdResult.Id);
        Assert.Equal("Sentadilla trasera con barra", byIdResult.Name);

        // No se ejecutaron más llamadas HTTP
        Assert.Equal(1, fakeHandler.CallCount);
        Assert.DoesNotContain(fakeHandler.RequestedUrls, url => url.Contains("?ids="));
    }

    /// <summary>
    /// HttpMessageHandler de prueba para registrar la cantidad y URLs de las peticiones despachadas.
    /// </summary>
    private sealed class TrackingHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => _callCount;
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            RequestedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}

using System.Net;
using System.Text;
using Domain.Entities;
using Entrenamiento.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Entrenamiento.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class TrainingPlanPdfIntegrationTests : IAsyncLifetime
{
    private readonly Guid _pfAId = Guid.NewGuid();
    private readonly Guid _pfBId = Guid.NewGuid();

    private EntrenamientoTestApplicationFactory _factory = null!;
    private HttpClient _clientPfA = null!;
    private HttpClient _clientAthlete = null!;
    private HttpClient _clientAnon = null!;

    private PfSeedData _dataA = null!;
    private PfSeedData _dataB = null!;
    private TrainingPlan _emptyPlanA = null!;

    public async Task InitializeAsync()
    {
        _factory = new EntrenamientoTestApplicationFactory();
        _clientPfA = _factory.CreateAuthenticatedClient(_pfAId);
        _clientAthlete = _factory.CreateAuthenticatedClient(Guid.NewGuid(), role: "Atleta");
        _clientAnon = _factory.CreateClient();

        // 1. Sembrar datos completos de PF A con semanas, sesiones y ejercicios adicionales
        _dataA = await _factory.SeedPfDataAsync(_pfAId);

        // Agregar contenido adicional al plan de PF A para asegurar que exceda 10 KB
        using (var pfContext = _factory.CreateDbContext(overridePfId: _pfAId))
        {
            var planDb = await pfContext.TrainingPlans
                .Include(p => p.Weeks)
                    .ThenInclude(w => w.Sessions)
                        .ThenInclude(s => s.PrescribedExercises)
                .FirstAsync(p => p.Id == _dataA.Plan.Id);

            var ex2 = Exercise.Create("Sentadilla Profunda", "Piernas", origin: Origin.Coach);
            var ex3 = Exercise.Create("Peso Muerto Rumano", "Espalda", origin: Origin.Coach);
            pfContext.Exercises.AddRange(ex2, ex3);

            // Semana 2
            var week2 = TrainingWeek.Create(2, planDb.Id);
            var session2 = TrainingSession.Create(
                week2.Id,
                "Fuerza Máxima y Potencia",
                order: 1,
                dayOfWeek: DayOfWeek.Wednesday,
                coachNotes: "Descanso completo entre series pesadas (3 min)");

            var prescribed1 = PrescribedExercise.Create(1, 4, 6, 85m, session2.Id, _dataA.CatalogExercise.Id);
            var prescribed2 = PrescribedExercise.Create(2, 4, 8, 80m, session2.Id, ex2.Id);
            var prescribed3 = PrescribedExercise.Create(3, 3, 10, 70m, session2.Id, ex3.Id);

            session2.AddPrescribedExercise(prescribed1);
            session2.AddPrescribedExercise(prescribed2);
            session2.AddPrescribedExercise(prescribed3);
            week2.AddSession(session2);

            // Semana 3
            var week3 = TrainingWeek.Create(3, planDb.Id);
            var session3 = TrainingSession.Create(
                week3.Id,
                "Transferencia y Velocidad",
                order: 1,
                dayOfWeek: DayOfWeek.Friday,
                coachNotes: "Máxima aceleración concéntrica");

            var prescribed4 = PrescribedExercise.Create(1, 5, 3, 90m, session3.Id, ex2.Id);
            session3.AddPrescribedExercise(prescribed4);
            week3.AddSession(session3);

            planDb.AddWeek(week2);
            planDb.AddWeek(week3);

            // Plan vacío perteneciente al mismo PF A
            _emptyPlanA = TrainingPlan.Create("Planificación Inicial Vacía", "Borrador sin sesiones aún");
            pfContext.TrainingPlans.Add(_emptyPlanA);

            await pfContext.SaveChangesAsync();
        }

        // 2. Sembrar datos de PF B (para pruebas de aislamiento multi-tenant)
        _dataB = await _factory.SeedPfDataAsync(_pfBId);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GetTrainingPlanPdf_DadoPlanCompleto_CuandoPfAutenticadoConsulta_EntoncesRetorna200YPdfValidoMayorA10Kb()
    {
        // ACT: PF A solicita el PDF de su propia planificación completa
        var response = await _clientPfA.GetAsync($"/api/v1/training-plans/{_dataA.Plan.Id}/pdf");

        // ASSERT: Status 200 OK
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Content-Type: application/pdf
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        // Content-Disposition: attachment con nombre sanitizado .pdf
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition!.DispositionType);
        Assert.NotNull(disposition.FileName);
        Assert.EndsWith(".pdf", disposition.FileName);
        Assert.DoesNotContain("/", disposition.FileName);
        Assert.DoesNotContain(":", disposition.FileName);

        // Bytes: tamaño mayor a 10 KB y magic bytes %PDF
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 10 * 1024, $"El PDF generado debería ser mayor a 10 KB, pero midió {bytes.Length} bytes.");

        var magicBytes = Encoding.ASCII.GetString(bytes.Take(4).ToArray());
        Assert.Equal("%PDF", magicBytes);
    }

    [Fact]
    public async Task GetTrainingPlanPdf_DadoPlanVacio_CuandoPfAutenticadoConsulta_EntoncesRetorna200YPdfValidoConAvisoSinError500()
    {
        // ACT: PF A solicita el PDF de un plan sin sesiones ni ejercicios
        var response = await _clientPfA.GetAsync($"/api/v1/training-plans/{_emptyPlanA.Id}/pdf");

        // ASSERT: Retorna 200 OK sin lanzar error 500
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var magicBytes = Encoding.ASCII.GetString(bytes.Take(4).ToArray());
        Assert.Equal("%PDF", magicBytes);
    }

    [Fact]
    public async Task GetTrainingPlanPdf_DadoSinToken_CuandoConsulta_EntoncesRetorna401Unauthorized()
    {
        // ACT: Petición anónima sin header Authorization
        var response = await _clientAnon.GetAsync($"/api/v1/training-plans/{_dataA.Plan.Id}/pdf");

        // ASSERT: 401 Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTrainingPlanPdf_DadoRolAtleta_CuandoConsulta_EntoncesRetorna403Forbidden()
    {
        // ACT: Usuario con rol Atleta intenta descargar el PDF reservado al PF
        var response = await _clientAthlete.GetAsync($"/api/v1/training-plans/{_dataA.Plan.Id}/pdf");

        // ASSERT: 403 Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTrainingPlanPdf_DadoPlanInexistente_CuandoPfConsulta_EntoncesRetorna404NotFound()
    {
        // ACT: PF A consulta un ID inexistente
        var nonExistentId = Guid.NewGuid();
        var response = await _clientPfA.GetAsync($"/api/v1/training-plans/{nonExistentId}/pdf");

        // ASSERT: 404 Not Found
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTrainingPlanPdf_DadoPlanDeOtroPf_CuandoPfConsulta_EntoncesRetorna404NotFound()
    {
        // ACT: PF A intenta descargar el PDF perteneciente a PF B
        var response = await _clientPfA.GetAsync($"/api/v1/training-plans/{_dataB.Plan.Id}/pdf");

        // ASSERT: 404 Not Found (aislamiento estricto multi-tenant sin filtrar existencia)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

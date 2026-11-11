using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Seguimiento.Tests.Integration;

// KAN-424: sesiones de test de campo con sus tests del catálogo y los resultados por atleta.
// CA1: la sesión admite varios tests del catálogo y carga de resultados por atleta.
// CA2: la base rechaza cargar dos veces el mismo (sesión, test, atleta, intento).
// KAN-568 CA1: la base también rechaza dos veces el mismo (sesión, prueba del catálogo).
public sealed class FieldTestPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();
    private readonly Guid _otherAthleteId = Guid.NewGuid();

    public FieldTestPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

        // PF del pedido actual: AppDbContext lo lee en cada operación para el filtro global.
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(user => user.PfId).Returns(_pfId);
        services.AddSingleton(currentUser.Object);

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private AppDbContext CreateDbContext() =>
        _provider.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    private static async Task<CatalogTest> CrearTestCatalogoAsync(AppDbContext db, string name, string unit)
    {
        var catalogTest = new CatalogTest(name, MotorCapacity.Power, unit);
        db.CatalogTests.Add(catalogTest);
        await db.SaveChangesAsync();
        return catalogTest;
    }

    private static FieldTestSession NuevaSesion(Guid categoryId, string name, FieldTestSessionStatus status) =>
        new()
        {
            CategoryId = categoryId,
            Date = new DateTime(2026, 11, 4),
            Name = name,
            Status = status
        };

    // ---------- CA1: la sesión admite varios tests del catálogo y resultados por atleta ----------

    [Fact]
    public async Task Given_FieldTestSession_When_AssociatingCatalogTestsAndLoadingResults_Then_PersistsEverything()
    {
        CatalogTest salto;
        CatalogTest sprint;
        FieldTestSession session;

        using (var db = CreateDbContext())
        {
            salto = await CrearTestCatalogoAsync(db, "Salto vertical", "cm");
            sprint = await CrearTestCatalogoAsync(db, "Sprint 30 m", "s");

            session = NuevaSesion(_categoryId, "Evaluación de pretemporada", FieldTestSessionStatus.InProgress);
            db.FieldTestSessions.Add(session);
            db.FieldTestSessionTests.AddRange(
                new FieldTestSessionTest { SessionId = session.Id, CatalogTestId = salto.Id, Order = 1 },
                new FieldTestSessionTest { SessionId = session.Id, CatalogTestId = sprint.Id, Order = 2 });
            db.FieldTestResults.AddRange(
                new FieldTestResult
                {
                    SessionId = session.Id, CatalogTestId = salto.Id, AthleteId = _athleteId,
                    Value = 42.5m, Attempt = 1, RecordedAt = DateTime.UtcNow
                },
                new FieldTestResult
                {
                    SessionId = session.Id, CatalogTestId = salto.Id, AthleteId = _athleteId,
                    Value = 41.8m, Attempt = 2, RecordedAt = DateTime.UtcNow
                },
                new FieldTestResult
                {
                    SessionId = session.Id, CatalogTestId = sprint.Id, AthleteId = _otherAthleteId,
                    Value = 4.32m, Attempt = 1, RecordedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        using var read = CreateDbContext();

        // La sesión queda como se cargó, con su categoría y estado.
        var savedSession = await read.FieldTestSessions.SingleAsync(row => row.Id == session.Id);
        Assert.Equal("Evaluación de pretemporada", savedSession.Name);
        Assert.Equal(_categoryId, savedSession.CategoryId);
        Assert.Equal(FieldTestSessionStatus.InProgress, savedSession.Status);
        Assert.Equal(_pfId, savedSession.PfId);

        // Los dos tests del catálogo quedan asociados con su orden.
        var links = await read.FieldTestSessionTests
            .Where(link => link.SessionId == session.Id)
            .OrderBy(link => link.Order)
            .ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Equal(new[] { salto.Id, sprint.Id }, links.Select(link => link.CatalogTestId));
        Assert.All(links, link => Assert.Equal(_pfId, link.PfId));

        // Cada atleta tiene sus resultados: el mismo atleta puede tener varios intentos.
        var results = await read.FieldTestResults
            .Where(result => result.SessionId == session.Id)
            .ToListAsync();
        Assert.Equal(3, results.Count);
        Assert.Equal(2, results.Count(result => result.AthleteId == _athleteId));
        Assert.Equal(42.5m, results.Single(result => result.AthleteId == _athleteId && result.Attempt == 1).Value);
        Assert.Equal(41.8m, results.Single(result => result.AthleteId == _athleteId && result.Attempt == 2).Value);
        Assert.Equal(4.32m, results.Single(result => result.AthleteId == _otherAthleteId).Value);
        Assert.All(results, result => Assert.Equal(_pfId, result.PfId));
    }

    // ---------- CA2: la base rechaza el resultado duplicado ----------

    [Fact]
    public async Task Given_SameAthleteTestAndAttempt_When_ResultLoadedTwice_Then_DatabaseRejectsIt()
    {
        Guid sessionId;

        using (var db = CreateDbContext())
        {
            var salto = await CrearTestCatalogoAsync(db, "Salto vertical", "cm");
            var session = NuevaSesion(_categoryId, "Evaluación de pretemporada", FieldTestSessionStatus.Planned);
            db.FieldTestSessions.Add(session);
            db.FieldTestResults.Add(new FieldTestResult
            {
                SessionId = session.Id, CatalogTestId = salto.Id, AthleteId = _athleteId,
                Value = 42.5m, Attempt = 1, RecordedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        // El mismo (sesión, test, atleta, intento): la base lo rechaza.
        using (var db = CreateDbContext())
        {
            var saltoId = await db.FieldTestResults
                .Where(result => result.SessionId == sessionId)
                .Select(result => result.CatalogTestId)
                .SingleAsync();

            db.FieldTestResults.Add(new FieldTestResult
            {
                SessionId = sessionId, CatalogTestId = saltoId, AthleteId = _athleteId,
                Value = 41.0m, Attempt = 1, RecordedAt = DateTime.UtcNow
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Queda exactamente un resultado para esa combinación.
        using var read = CreateDbContext();
        var persisted = await read.FieldTestResults
            .Where(result => result.SessionId == sessionId && result.Attempt == 1)
            .ToListAsync();

        Assert.Single(persisted);
        Assert.Equal(42.5m, persisted[0].Value);
    }

    // ---------- KAN-568 CA1: la base rechaza el test repetido en la misma sesión ----------

    [Fact]
    public async Task Given_SameCatalogTestTwice_When_LinkLoadedTwice_Then_DatabaseRejectsIt()
    {
        Guid sessionId;

        using (var db = CreateDbContext())
        {
            var salto = await CrearTestCatalogoAsync(db, "Salto vertical", "cm");
            var session = NuevaSesion(_categoryId, "Evaluación de pretemporada", FieldTestSessionStatus.Planned);
            db.FieldTestSessions.Add(session);
            db.FieldTestSessionTests.Add(
                new FieldTestSessionTest { SessionId = session.Id, CatalogTestId = salto.Id, Order = 1 });
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        // El mismo (sesión, prueba del catálogo): la base lo rechaza en el SaveChanges.
        using (var db = CreateDbContext())
        {
            var testId = await db.FieldTestSessionTests
                .Where(link => link.SessionId == sessionId)
                .Select(link => link.CatalogTestId)
                .SingleAsync();

            db.FieldTestSessionTests.Add(
                new FieldTestSessionTest { SessionId = sessionId, CatalogTestId = testId, Order = 2 });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Queda exactamente un vínculo para esa combinación.
        using var read = CreateDbContext();
        var persisted = await read.FieldTestSessionTests
            .Where(link => link.SessionId == sessionId)
            .ToListAsync();

        Assert.Single(persisted);
        Assert.Equal(1, persisted[0].Order);
    }
}

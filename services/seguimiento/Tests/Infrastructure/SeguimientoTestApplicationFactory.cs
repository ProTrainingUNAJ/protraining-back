using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using API.Controllers.Categories;
using API.Extensions;
using API.Middleware;
using API.Services;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.DailyLoads.RegisterDailyLoad;
using Application.UseCases.Injuries.CreateAthleteInjury;
using Application.UseCases.Injuries.ListAthleteInjuries;
using Application.UseCases.Injuries.UpdateInjuryStatus;
using Application.UseCases.FieldTests.ConfirmFieldTestResult;
using Application.UseCases.FieldTests.CreateFieldTestResult;
using Application.UseCases.FieldTests.CreateFieldTestSession;
using Application.UseCases.FieldTests.GetFieldTestSession;
using Application.UseCases.FieldTests.ListFieldTestSessions;
using Application.UseCases.FieldTests.UpdateFieldTestResult;
using Application.UseCases.FieldTests.UpdateFieldTestSessionStatus;
using Application.UseCases.Attendance.GetCategoryAttendanceToday;
using Application.UseCases.Attendance.SetAttendance;
using Application.UseCases.TrafficLight.GetCategoryTrafficLightToday;
using Application.UseCases.Categories.GetCategoryAverages;
using Application.UseCases.Benchmark;
using Application.UseCases.Evolution.GetAthleteEvolution;
using Application.Services;
using Domain.Services;
using Infrastructure.Data;
using Infrastructure.Data.Seeders;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Infrastructure.Services.Entrenamiento;
using Infrastructure.Services.Structure;
using Infrastructure.Services.Training;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application.UseCases.FitnessTests.ListFitnessTests;

namespace Seguimiento.Tests.Infrastructure;

// Factory de integración de Seguimiento, siguiendo el patrón de
// EstructuraTestApplicationFactory: TestServer con SQLite en memoria en lugar de
// PostgreSQL, para probar el pipeline real de arranque (esquema + seeder) sin Docker.
// KAN-367: además del pipeline de datos, levanta el endpoint real del semáforo con
// autenticación JWT y un Estructura falso controlable por test (caída, timeout, breaker).
public sealed class SeguimientoTestApplicationFactory : IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly TestServer _server;

    // Estructura falsa: cada test define qué le pasa al llamado del semáforo.
    public FakeStructureHandler Structure { get; }

    // Entrenamiento falso: control del llamado de récords 1RM (#140 / KAN-435).
    public FakeTrainingHandler Training { get; }

    // Entrenamiento falso: cada test define qué le pasa a la confirmación.
    public FakeEntrenamientoHandler Entrenamiento { get; }

    private SeguimientoTestApplicationFactory(
        SqliteConnection sqliteConnection, TestServer server, FakeStructureHandler structure,
        FakeTrainingHandler training, FakeEntrenamientoHandler entrenamiento)
    {
        _sqliteConnection = sqliteConnection;
        _server = server;
        Structure = structure;
        Training = training;
        Entrenamiento = entrenamiento;
    }

    // Levanta el host con el mismo DI de infraestructura que el servicio y ejecuta
    // el arranque: crea el esquema (EnsureCreated en vez de migraciones, que son de
    // Npgsql) y siembra el catálogo, tal como hace ApplyMigrationsAsync en producción.
    public static async Task<SeguimientoTestApplicationFactory> CreateAsync(
        Guid? pfId = null,
        IFitnessTestMeasurementService? customMeasurementService = null,
        TimeProvider? customTimeProvider = null)
    {
        var sqliteConnection = new SqliteConnection("DataSource=:memory:");
        sqliteConnection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // JWT de prueba: mismas claves que exige AddJwtAuthentication (KAN-367).
                ["Jwt:Key"] = TestJwtGenerator.DefaultKey,
                ["Jwt:Issuer"] = TestJwtGenerator.DefaultIssuer,
                ["Jwt:Audience"] = TestJwtGenerator.DefaultAudience,
            })
            .Build();

        var structureHandler = new FakeStructureHandler();
        var trainingHandler = new FakeTrainingHandler();
        var entrenamientoHandler = new FakeEntrenamientoHandler();

        var builder = new WebHostBuilder()
            .UseEnvironment("Development")
            .UseConfiguration(configuration)
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration);

                services.AddDbContext<AppDbContext>(options => options.UseSqlite(sqliteConnection));

                services.AddScoped<IUnitOfWork, UnitOfWork>();
                services.AddScoped<ICheckInRepository, CheckInRepository>();
                services.AddScoped<IAthleteInjuryRepository, AthleteInjuryRepository>();
                services.AddScoped<IDailyLoadRepository, DailyLoadRepository>();
                services.AddScoped<IAttendanceRepository, AttendanceRepository>();
                services.AddScoped<ICatalogTestRepository, CatalogTestRepository>();
                services.AddScoped<IFieldTestRepository, FieldTestRepository>();
                services.AddScoped<IFieldTestResultRepository, FieldTestResultRepository>();
                services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();


                services.AddHttpContextAccessor();
                services.AddScoped<ICurrentUser>(sp => new TestCurrentUser(pfId, sp.GetService<IHttpContextAccessor>()));
                services.AddScoped<IAthleteAccessGuard, AthleteAccessGuard>();

                // Semáforo del día (KAN-367): motor y caso de uso registrados como en Program.cs.
                services.AddScoped<TrafficLightEngine>();
                services.AddScoped<GetCategoryTrafficLightTodayQueryHandler>();
                services.AddScoped<GetCategoryAttendanceTodayQueryHandler>();
                services.AddScoped<SetAttendanceCommandHandler>();

                if (customTimeProvider != null)
                {
                    services.AddSingleton(customTimeProvider);
                }
                if (customMeasurementService != null)
                {
                    services.AddSingleton(customMeasurementService);
                }
                else
                {
                    services.AddScoped<IFitnessTestMeasurementService, DefaultFitnessTestMeasurementService>();
                }
                services.AddScoped<GetCategoryAveragesQueryHandler>();
                services.AddScoped<GetAthleteGroupBenchmarkQueryHandler>();
                services.AddScoped<Application.UseCases.CheckIns.SubmitCheckIn.SubmitCheckInCommandHandler>();
                services.AddScoped<Application.UseCases.CheckIns.ListAthleteCheckIns.ListAthleteCheckInsQueryHandler>();
                services.AddScoped<Application.UseCases.CheckIns.GetTodayCheckIn.GetTodayCheckInQueryHandler>();
                services.AddScoped<Application.UseCases.FitnessTests.ListAthleteFitnessTests.ListAthleteFitnessTestsQueryHandler>();
                services.AddScoped<ListFitnessTestsQueryHandler>();

                // Lesiones de los atletas (KAN-419): mismos registros que en Program.cs.
                services.AddScoped<CreateAthleteInjuryCommandHandler>();
                services.AddScoped<ListAthleteInjuriesQueryHandler>();

                // Máquina de estados de las lesiones (KAN-420): mismo registro que en Program.cs.
                services.AddScoped<UpdateInjuryStatusCommandHandler>();

                // Carga rápida de resultados de campo (KAN-425): mismo registro que en Program.cs.
                services.AddScoped<CreateFieldTestResultCommandHandler>();
                services.AddScoped<GetFieldTestSessionQueryHandler>();
                services.AddScoped<UpdateFieldTestResultCommandHandler>();

                // Confirmación de un resultado de fuerza con envío del 1RM a Entrenamiento:
                // mismo registro que en Program.cs.
                services.AddScoped<ConfirmFieldTestResultCommandHandler>();

                // Gestión de las sesiones de test de campo y sus estados (KAN-427):
                // mismo registro que en Program.cs.
                services.AddScoped<CreateFieldTestSessionCommandHandler>();
                services.AddScoped<ListFieldTestSessionsQueryHandler>();
                services.AddScoped<UpdateFieldTestSessionStatusCommandHandler>();

                // Carga del día (KAN-442): mismo registro que en Program.cs.
                services.AddScoped<RegisterDailyLoadCommandHandler>();
                services.AddScoped<GetAthleteEvolutionQueryHandler>();

                // Estructura por HTTP hacia el handler falso: misma cadena de producción
                // (StructureClient + reenvío del token) y control total del comportamiento por test.
                services.AddTransient<ForwardAuthorizationHandler>();
                services.AddHttpClient<IStructureClient, StructureClient>(client =>
                        client.BaseAddress = new Uri("http://estructura.test/"))
                    .ConfigurePrimaryHttpMessageHandler(() => structureHandler)
                    .AddHttpMessageHandler<ForwardAuthorizationHandler>();

                // Entrenamiento por HTTP (#140 / KAN-435): consulta de récords 1RM para evolución.
                services.AddHttpClient<ITrainingClient, TrainingClient>(client =>
                        client.BaseAddress = new Uri("http://entrenamiento.test/"))
                    .ConfigurePrimaryHttpMessageHandler(() => trainingHandler)
                    .AddHttpMessageHandler<ForwardAuthorizationHandler>();

                // Entrenamiento por HTTP hacia el handler falso: misma cadena de
                // producción (EntrenamientoClient + reenvío del token), sin el pipeline real
                // de Polly (las rejecciones las simula el propio fake, igual que Estructura).
                services.AddHttpClient<IEntrenamientoClient, EntrenamientoClient>(client =>
                        client.BaseAddress = new Uri("http://entrenamiento.test/"))
                    .ConfigurePrimaryHttpMessageHandler(() => entrenamientoHandler)
                    .AddHttpMessageHandler<ForwardAuthorizationHandler>();

                // Autenticación y controllers reales: el endpoint exige rol PreparadorFisico.
                services.AddJwtAuthentication(configuration);
                services.AddControllers()
                    // Mismo converter que Program.cs: los enums del contrato viajan como texto
                    // ("InRecovery") y no como número (KAN-420).
                    .AddJsonOptions(options =>
                        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
                    .AddApplicationPart(typeof(CategoriesTrafficLightController).Assembly);
            })
            .Configure(app =>
            {
                app.UseMiddleware<CorrelationIdMiddleware>();
                app.UseMiddleware<ExceptionMiddleware>();

                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();

                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });

        var server = new TestServer(builder);

        using (var scope = server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.EnsureCreatedAsync();

            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
            await CatalogTestSeeder.SeedAsync(context, logger);
        }

        return new SeguimientoTestApplicationFactory(sqliteConnection, server, structureHandler, trainingHandler, entrenamientoHandler);
    }

    // Cliente HTTP sin token: sirve para probar los 401/403 del endpoint.
    public HttpClient CreateClient() => _server.CreateClient();

    // Cliente con JWT de PF, igual que el que usa el front del preparador físico.
    public HttpClient CreateAuthenticatedClient(Guid pfId, string role = "PreparadorFisico")
    {
        var client = _server.CreateClient();
        var token = TestJwtGenerator.GenerateToken(pfId, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public AppDbContext CreateDbContext(Guid? overridePfId = null)
    {
        var currentUserMock = new Moq.Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(overridePfId);
        currentUserMock.Setup(u => u.IsInRole(Moq.It.IsAny<string>()))
            .Returns<string>(role => overridePfId.HasValue && role == "PreparadorFisico");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        return new AppDbContext(options, currentUserMock.Object);
    }

    // Ejecuta una consulta contra la base del factory con un scope propio y limpio.
    public async Task<T> ExecuteInDbContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _server.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(context);
    }

    // Variante para acciones que no devuelven resultado (por ejemplo volver a sembrar).
    public async Task ExecuteInDbContextAsync(Func<AppDbContext, Task> action)
    {
        using var scope = _server.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(context);
    }

    public async ValueTask DisposeAsync()
    {
        _server.Dispose();
        await _sqliteConnection.DisposeAsync();
    }

    public void Dispose()
    {
        _server.Dispose();
        _sqliteConnection.Dispose();
    }
}

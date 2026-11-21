namespace Asistente.Tests.Database;

using System.Collections.Concurrent;
using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Recommendations.GenerateCategoryRecommendations;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Data.Repositories;
using Infrastructure.Services;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Pruebas de concurrencia contra PostgreSQL 16 real (Testcontainers) para el motor de recomendaciones por categoría
/// y la auditoría en ai_logs (KAN-491).
/// Verifica que la corrida paralela con IScopedAiServiceRunner y AiAuditService (IDbContextFactory)
/// no comparta DbContext entre hilos ni pierda registros de auditoría.
/// </summary>
[Trait("Category", "RequiresDocker")]
public sealed class CategoryRecommendationConcurrencyPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext(_pfId);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext CreateContext(Guid pfId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new AppDbContext(options, new FixedCurrentUser(pfId));
    }

    private sealed class FixedCurrentUser(Guid pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private sealed class WarningCapturingLogger<T> : ILogger<T>
    {
        public ConcurrentBag<string> WarningsAndErrors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                WarningsAndErrors.Add($"{logLevel}: {formatter(state, exception)} | {exception?.Message}");
            }
        }
    }

    [Fact]
    public async Task Given_CategoryWithMultipleAthletesInPostgres_When_RunningInParallelWithRealAudit_Then_PersistsAllRecommendationsAndAllAiLogs()
    {
        var athleteIds = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToList();

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.GetCategoryAsync(_categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureCategoryDto(_categoryId, "Primera División"));
        structureMock.Setup(s => s.GetCategoryAthletesAsync(_categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athleteIds.Select(id => new StructureCategoryAthleteDto(id, true)).ToList());
        foreach (var id in athleteIds)
        {
            structureMock.Setup(s => s.GetHabitsPermissionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StructureAthleteHabitsPermissionDto(IsMinor: false, AllowsHabitsRecommendations: true));
        }

        var seguimientoMock = new Mock<ISeguimientoClient>();
        seguimientoMock.Setup(s => s.GetCategoryAveragesAsync(_categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryAveragesRemoteDto(
                _categoryId,
                athleteIds.Count,
                new DateOnly(2026, 11, 9),
                new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 42.0, athleteIds.Count)],
                []));
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid id, CancellationToken ct) =>
            {
                await Task.Delay(15, ct);
                return (IReadOnlyList<AthleteFitnessTestRemoteDto>)
                [
                    new AthleteFitnessTestRemoteDto(
                        Guid.NewGuid(),
                        id,
                        Guid.NewGuid(),
                        "Salto Vertical CMJ",
                        "Power",
                        34.0m,
                        "cm",
                        1,
                        DateTime.UtcNow)
                ];
            });
        seguimientoMock.Setup(s => s.GetAthleteWeeklyTrendsAsync(It.IsAny<Guid>(), 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteWeeklyTrendsRemoteDto?)null);

        var auditLogger = new WarningCapturingLogger<AiAuditService>();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(
            options => options.UseNpgsql(_postgres.GetConnectionString()),
            ServiceLifetime.Scoped);
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddScoped<ICurrentUser>(_ => new FixedCurrentUser(_pfId));
        services.AddSingleton<ILogger<AiAuditService>>(auditLogger);
        services.AddScoped<IAiAuditService, AiAuditService>();
        services.AddScoped<RuleBasedAiClient>(sp => new RuleBasedAiClient(
            sp.GetRequiredService<IAiAuditService>(),
            NullLogger<RuleBasedAiClient>.Instance));
        services.AddScoped<IReadOnlyList<IAiClient>>(sp => [sp.GetRequiredService<RuleBasedAiClient>()]);
        services.AddScoped<IAiService>(sp => new ResilientAiService(
            providers: sp.GetRequiredService<IReadOnlyList<IAiClient>>(),
            semanticValidator: null,
            responseCache: null,
            currentUser: sp.GetRequiredService<ICurrentUser>(),
            auditService: sp.GetRequiredService<IAiAuditService>(),
            logger: NullLogger<ResilientAiService>.Instance));
        services.AddSingleton<IScopedAiServiceRunner, ScopedAiServiceRunner>();
        services.AddScoped<IAthleteRecommendationRepository, AthleteRecommendationRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton(structureMock.Object);
        services.AddSingleton(HabitsRecommendationsClientFactory.Enabled());
        services.AddSingleton(seguimientoMock.Object);
        services.AddSingleton(new RecommendationsOptions { MaxCategoryParallelism = 4 });
        services.AddSingleton<ILogger<GenerateCategoryRecommendationsCommandHandler>>(
            NullLogger<GenerateCategoryRecommendationsCommandHandler>.Instance);
        services.AddScoped<GenerateCategoryRecommendationsCommandHandler>();

        await using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        await using (var requestScope = serviceProvider.CreateAsyncScope())
        {
            var handler = requestScope.ServiceProvider.GetRequiredService<GenerateCategoryRecommendationsCommandHandler>();
            var response = await handler.HandleAsync(new GenerateCategoryRecommendationsCommand(_categoryId));

            Assert.Equal(athleteIds.Count, response.EvaluatedAthletes);
            Assert.Equal(athleteIds.Count, response.GeneratedCount);
            Assert.Equal(0, response.ExistingPendingCount);
        }

        Assert.Empty(auditLogger.WarningsAndErrors);

        await using var verificationContext = CreateContext(_pfId);
        var persistedRecommendations = await verificationContext.AthleteRecommendations
            .AsNoTracking()
            .Where(r => r.CategoryId == _categoryId)
            .ToListAsync();

        Assert.Equal(athleteIds.Count, persistedRecommendations.Count);
        Assert.All(persistedRecommendations, rec =>
        {
            Assert.Equal(RecommendationStatus.Pending, rec.Status);
            Assert.Equal("salto por debajo del promedio", rec.Reason);
            Assert.Equal("Reglas", rec.Provider);
        });

        var persistedLogs = await verificationContext.AiLogs
            .AsNoTracking()
            .Where(l => l.PfId == _pfId && l.AthleteId != null && athleteIds.Contains(l.AthleteId.Value))
            .ToListAsync();

        Assert.Equal(athleteIds.Count, persistedLogs.Count);
        Assert.Equal(
            athleteIds.OrderBy(id => id),
            persistedLogs.Select(l => l.AthleteId!.Value).OrderBy(id => id));
        Assert.All(persistedLogs, log =>
        {
            Assert.Equal("Reglas", log.Provider);
            Assert.True(log.IsValid);
        });
    }

    [Fact]
    public async Task Given_SingleAiAuditServiceInstanceInPostgres_When_LoggingConcurrently_Then_PersistsEveryLogWithoutConcurrencyWarnings()
    {
        var concurrentPfId = Guid.NewGuid();
        var athleteIds = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToList();
        var auditLogger = new WarningCapturingLogger<AiAuditService>();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(
            options => options.UseNpgsql(_postgres.GetConnectionString()),
            ServiceLifetime.Scoped);
        services.AddScoped<ICurrentUser>(_ => new FixedCurrentUser(concurrentPfId));
        services.AddSingleton<ILogger<AiAuditService>>(auditLogger);
        services.AddScoped<IAiAuditService, AiAuditService>();

        await using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var auditService = scope.ServiceProvider.GetRequiredService<IAiAuditService>();

            var tasks = athleteIds.Select(athleteId => auditService.LogAsync(AiLog.Create(
                provider: "Reglas",
                prompt: $"Prompt concurrente para atleta {athleteId}",
                latencyMs: 5,
                isValid: true,
                validationResult: "Valido",
                response: "Recomendación de prueba",
                athleteId: athleteId)));

            await Task.WhenAll(tasks);
        }

        Assert.Empty(auditLogger.WarningsAndErrors);

        await using var verificationContext = CreateContext(concurrentPfId);
        var persistedLogs = await verificationContext.AiLogs
            .AsNoTracking()
            .Where(l => l.PfId == concurrentPfId)
            .ToListAsync();

        Assert.Equal(athleteIds.Count, persistedLogs.Count);
        Assert.Equal(
            athleteIds.OrderBy(id => id),
            persistedLogs.Select(l => l.AthleteId!.Value).OrderBy(id => id));
    }
}

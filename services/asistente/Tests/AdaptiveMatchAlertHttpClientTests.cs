using System.Net;
using System.Text;
using System.Text.Json;
using API.Services;
using Application.DTOs.Alerts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Alerts.ProcessDailyOverloadAnalysis;
using Domain.Common;
using Domain.Entities;
using Infrastructure.Services.Seguimiento;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas de clientes HTTP y resiliencia del análisis diario de sobrecarga (KAN-477 · #182).
/// Utilizan las implementaciones reales de StructureAthleteClient, StructureCalendarClient y SeguimientoClient
/// con HttpMessageHandler controlado (sin mocks de Moq sobre interfaces externas).
/// </summary>
public sealed class AdaptiveMatchAlertHttpClientTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class InMemoryAlertRepository(ICurrentUser currentUser) : IPreventiveAlertRepository
    {
        public List<PreventiveAlert> Alerts { get; } = [];

        public Task<PreventiveAlert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Alerts.FirstOrDefault(a => a.Id == id));

        public Task<PreventiveAlert?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Alerts.FirstOrDefault(a => a.Id == id));

        public Task<IReadOnlyList<PreventiveAlert>> ListAsync(
            bool onlyUnreviewed = true,
            AlertSeverity? severity = null,
            CancellationToken cancellationToken = default)
        {
            var query = Alerts.AsEnumerable();
            if (currentUser.PfId.HasValue)
            {
                query = query.Where(a => a.PfId == currentUser.PfId.Value);
            }
            if (onlyUnreviewed)
            {
                query = query.Where(a => a.Status == AlertStatus.Pending);
            }
            if (severity.HasValue)
            {
                query = query.Where(a => a.Severity == severity.Value);
            }
            return Task.FromResult<IReadOnlyList<PreventiveAlert>>(query.ToList());
        }

        public Task AddAsync(PreventiveAlert alert, CancellationToken cancellationToken = default)
        {
            if (currentUser.PfId.HasValue)
            {
                typeof(BaseEntity)
                    .GetProperty(nameof(BaseEntity.PfId))!
                    .SetValue(alert, currentUser.PfId.Value);
            }
            Alerts.Add(alert);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(PreventiveAlert alert, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static ProcessDailyOverloadAnalysisCommandHandler CreateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> estructuraRespond,
        Func<HttpRequestMessage, HttpResponseMessage> seguimientoRespond,
        IPreventiveAlertRepository alertRepository,
        ICurrentUser currentUser)
    {
        var estructuraHttp = new HttpClient(new FakeHttpMessageHandler(estructuraRespond))
        {
            BaseAddress = new Uri("http://estructura.test/")
        };
        var seguimientoHttp = new HttpClient(new FakeHttpMessageHandler(seguimientoRespond))
        {
            BaseAddress = new Uri("http://seguimiento.test/")
        };

        return new ProcessDailyOverloadAnalysisCommandHandler(
            new StructureAthleteClient(estructuraHttp, NullLogger<StructureAthleteClient>.Instance),
            new SeguimientoClient(seguimientoHttp, NullLogger<SeguimientoClient>.Instance),
            new StructureCalendarClient(estructuraHttp, NullLogger<StructureCalendarClient>.Instance),
            alertRepository,
            new FakeUnitOfWork(),
            currentUser,
            NullLogger<ProcessDailyOverloadAnalysisCommandHandler>.Instance);
    }

    [Fact]
    public async Task Given_LegacyResponseWithoutCategoryIds_When_AthleteHasCategoryId_Then_UsesCategoryIdFallback()
    {
        // Arrange: respuesta JSON de Estructura sin el campo 'categoryIds' (contrato legacy)
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 11, 4);
        var satMatch = new DateOnly(2026, 11, 7);

        var athletesJson = $$"""
            [
                {
                    "id": "{{athleteId}}",
                    "pfId": "{{pfId}}",
                    "fullName": "Atleta Legacy",
                    "active": true,
                    "categoryId": "{{catId}}"
                }
            ]
            """;

        var eventsJson = $$"""
            [
                { "date": "{{satMatch:yyyy-MM-dd}}", "type": "Official" }
            ]
            """;

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, targetDate.AddDays(-2), 8.0, 4, 2, 2, 4, 2, 8, false, null, false),
            new(Guid.NewGuid(), athleteId, targetDate.AddDays(-1), 8.0, 4, 2, 2, 4, 2, 9, false, null, false)
        };
        var checkInsJson = JsonSerializer.Serialize(checkIns);

        var currentUser = new CurrentUser(new HttpContextAccessor());
        var alertRepository = new InMemoryAlertRepository(currentUser);

        var handler = CreateHandler(
            estructuraRespond: req => req.RequestUri!.AbsolutePath switch
            {
                "/api/v1/internal/athletes" => JsonResponse(HttpStatusCode.OK, athletesJson),
                var p when p == $"/api/v1/internal/categories/{catId}/events" => JsonResponse(HttpStatusCode.OK, eventsJson),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            },
            seguimientoRespond: req => req.RequestUri!.AbsolutePath == "/api/v1/internal/check-ins/history"
                ? JsonResponse(HttpStatusCode.OK, checkInsJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound),
            alertRepository: alertRepository,
            currentUser: currentUser);

        // Act
        var result = await handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(targetDate));

        // Assert
        Assert.Equal(1, result.GeneratedAlerts);
        var alert = Assert.Single(alertRepository.Alerts);
        Assert.Equal(catId, alert.CategoryId);
        Assert.Equal(AlertType.AcuteOverload, alert.Type);
        Assert.Equal(AlertSeverity.High, alert.Severity);
        Assert.Equal(20, alert.SuggestedAdjustmentPercentage);
    }

    [Fact]
    public async Task Given_StructureCalendarUnavailable_When_ProcessDailyAnalysis_Then_ContinuesEvaluatingNonCalendarRulesWithoutInterruption()
    {
        // Arrange: el endpoint de calendario en Estructura responde 503 ServiceUnavailable
        var pfId = Guid.NewGuid();
        var athlete1Id = Guid.NewGuid();
        var athlete2Id = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 11, 4);

        var athletesJson = $$"""
            [
                {
                    "id": "{{athlete1Id}}",
                    "pfId": "{{pfId}}",
                    "fullName": "Atleta Dos Sesiones",
                    "active": true,
                    "categoryId": "{{catId}}",
                    "categoryIds": ["{{catId}}"]
                },
                {
                    "id": "{{athlete2Id}}",
                    "pfId": "{{pfId}}",
                    "fullName": "Atleta Tres Sesiones",
                    "active": true,
                    "categoryId": "{{catId}}",
                    "categoryIds": ["{{catId}}"]
                }
            ]
            """;

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athlete1Id, targetDate.AddDays(-2), 8.0, 4, 2, 2, 4, 2, 8, false, null, false),
            new(Guid.NewGuid(), athlete1Id, targetDate.AddDays(-1), 8.0, 4, 2, 2, 4, 2, 8, false, null, false),
            new(Guid.NewGuid(), athlete2Id, targetDate.AddDays(-3), 8.0, 4, 2, 2, 4, 2, 8, false, null, false),
            new(Guid.NewGuid(), athlete2Id, targetDate.AddDays(-2), 8.0, 4, 2, 2, 4, 2, 9, false, null, false),
            new(Guid.NewGuid(), athlete2Id, targetDate.AddDays(-1), 8.0, 4, 2, 2, 4, 2, 8, false, null, false)
        };
        var checkInsJson = JsonSerializer.Serialize(checkIns);

        var currentUser = new CurrentUser(new HttpContextAccessor());
        var alertRepository = new InMemoryAlertRepository(currentUser);

        var handler = CreateHandler(
            estructuraRespond: req => req.RequestUri!.AbsolutePath switch
            {
                "/api/v1/internal/athletes" => JsonResponse(HttpStatusCode.OK, athletesJson),
                var p when p == $"/api/v1/internal/categories/{catId}/events" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            },
            seguimientoRespond: req => req.RequestUri!.AbsolutePath == "/api/v1/internal/check-ins/history"
                ? JsonResponse(HttpStatusCode.OK, checkInsJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound),
            alertRepository: alertRepository,
            currentUser: currentUser);

        // Act
        var result = await handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(targetDate));

        // Assert: no aborta el worker; Atleta 1 (2 sesiones) no dispara alerta y Atleta 2 (3 sesiones) sí.
        Assert.Equal(2, result.ProcessedAthletes);
        Assert.Equal(1, result.GeneratedAlerts);
        var alert = Assert.Single(alertRepository.Alerts);
        Assert.Equal(athlete2Id, alert.AthleteId);
        Assert.Contains("Sobrecarga aguda sostenida: 3 sesiones", alert.Reason);
    }
}

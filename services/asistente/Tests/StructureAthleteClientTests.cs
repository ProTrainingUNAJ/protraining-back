using System.Net;
using System.Text;
using Application.Exceptions;
using Application.Interfaces.Services;
using Infrastructure.Services.Structure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Asistente.Tests;

public sealed class StructureAthleteClientTests
{
    private sealed class DelegatingTestHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(handler(request));
        }
    }

    [Fact]
    public async Task GetAthletesAsync_WhenEndpointReturns200_ReturnsAthletesWithPfIdAndActiveStatus()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var json = $$"""
        [
            {
                "id": "{{athleteId}}",
                "pfId": "{{pfId}}",
                "firstName": "Lucas",
                "lastName": "Fernández",
                "fullName": "Lucas Fernández",
                "active": true,
                "categoryId": "{{categoryId}}"
            }
        ]
        """;

        DelegatingTestHandler? capturedHandler = null;
        capturedHandler = new DelegatingTestHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(capturedHandler)
        {
            BaseAddress = new Uri("http://estructura:8080/")
        };

        var structureClient = new StructureAthleteClient(client, NullLogger<StructureAthleteClient>.Instance);

        // Act
        var athletes = await structureClient.GetAthletesAsync(CancellationToken.None);

        // Assert
        Assert.Single(athletes);
        Assert.Equal(athleteId, athletes[0].Id);
        Assert.Equal(pfId, athletes[0].PfId);
        Assert.Equal(categoryId, athletes[0].CategoryId);
        Assert.True(athletes[0].Active);
        Assert.Equal("Lucas Fernández", athletes[0].FullName);
        Assert.Equal("http://estructura:8080/api/v1/internal/athletes", capturedHandler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetAthletesAsync_WhenEstructuraReturns403Forbidden_ThrowsStructureServiceUnavailableException()
    {
        // Arrange: Demuestra que NO se traga el error de autorización
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://estructura:8080/")
        };

        var structureClient = new StructureAthleteClient(client, NullLogger<StructureAthleteClient>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() => structureClient.GetAthletesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAthletesAsync_WhenEstructuraReturns401Unauthorized_ThrowsStructureServiceUnavailableException()
    {
        // Arrange
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://estructura:8080/")
        };

        var structureClient = new StructureAthleteClient(client, NullLogger<StructureAthleteClient>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() => structureClient.GetAthletesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IsOwnAthleteAsync_When200_ReturnsTrue()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://estructura:8080/")
        };

        var structureClient = new StructureAthleteClient(client, NullLogger<StructureAthleteClient>.Instance);

        // Act
        var result = await structureClient.IsOwnAthleteAsync(athleteId);

        // Assert
        Assert.True(result);
        Assert.Equal($"http://estructura:8080/api/v1/athletes/{athleteId}", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task IsOwnAthleteAsync_When404_ReturnsFalse()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://estructura:8080/")
        };

        var structureClient = new StructureAthleteClient(client, NullLogger<StructureAthleteClient>.Instance);

        // Act
        var result = await structureClient.IsOwnAthleteAsync(athleteId);

        // Assert
        Assert.False(result);
    }
}


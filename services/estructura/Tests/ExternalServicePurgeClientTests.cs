using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Estructura.Tests;

public sealed class ExternalServicePurgeClientTests
{
    private static (ExternalServicePurgeClient client, Mock<HttpMessageHandler> handlerMock) CreateClient()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);
        var client = new ExternalServicePurgeClient(httpClient, NullLogger<ExternalServicePurgeClient>.Instance);
        return (client, handlerMock);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Given_SuccessStatusCode_When_PurgingAthlete_Then_ReturnsIsSuccessTrue(HttpStatusCode statusCode)
    {
        // Arrange
        var (client, handlerMock) = CreateClient();
        var athleteId = Guid.NewGuid();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Delete &&
                    req.RequestUri!.ToString().Contains($"/api/v1/internal/athletes/{athleteId}/purge")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode));

        // Act
        var result = await client.PurgeAthleteInServiceAsync("Seguimiento", "http://seguimiento:8080", athleteId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Seguimiento", result.ServiceName);
        Assert.Equal((int)statusCode, result.StatusCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task Given_404NotFound_When_PurgingAthlete_Then_ReturnsIsSuccessTrueAsIdempotent()
    {
        // Arrange
        var (client, handlerMock) = CreateClient();
        var athleteId = Guid.NewGuid();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        // Act
        var result = await client.PurgeAthleteInServiceAsync("Asistente", "http://asistente:8080", athleteId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Given_5xxServerError_When_PurgingAthlete_Then_ReturnsIsSuccessFalse(HttpStatusCode statusCode)
    {
        // Arrange
        var (client, handlerMock) = CreateClient();
        var athleteId = Guid.NewGuid();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("Service error")
            });

        // Act
        var result = await client.PurgeAthleteInServiceAsync("Entrenamiento", "http://entrenamiento:8080", athleteId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal((int)statusCode, result.StatusCode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Given_HttpRequestException_When_PurgingAthlete_Then_CatchesAndReturnsIsSuccessFalse()
    {
        // Arrange
        var (client, handlerMock) = CreateClient();
        var athleteId = Guid.NewGuid();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var result = await client.PurgeAthleteInServiceAsync("Identidad", "http://identidad:8080", athleteId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.StatusCode);
        Assert.Contains("Connection refused", result.ErrorMessage);
    }
}


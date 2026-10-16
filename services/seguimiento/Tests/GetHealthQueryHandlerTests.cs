using Application.DTOs.Common;
using Application.Interfaces.Services;
using Application.UseCases.Health.GetHealth;
using Moq;
using Xunit;

namespace Seguimiento.Tests;

// Resiliencia del punto de entrada de salud (KAN-364):
// una base de datos inaccesible se informa como "Degraded", nunca como una excepción propagada.
public class GetHealthQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenDatabaseResponds_ReturnsHealthy()
    {
        var databaseHealth = new Mock<IDatabaseHealthService>();
        databaseHealth
            .Setup(servicio => servicio.CanConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var handler = new GetHealthQueryHandler(databaseHealth.Object);

        HealthDto result = await handler.HandleAsync(new GetHealthQuery());

        Assert.Equal("Healthy", result.Status);
        Assert.Equal("Connected", result.Database);
    }

    [Fact]
    public async Task HandleAsync_WhenDatabaseDoesNotRespond_ReturnsDegradedWithoutThrowing()
    {
        var databaseHealth = new Mock<IDatabaseHealthService>();
        databaseHealth
            .Setup(servicio => servicio.CanConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var handler = new GetHealthQueryHandler(databaseHealth.Object);

        HealthDto result = await handler.HandleAsync(new GetHealthQuery());

        Assert.Equal("Degraded", result.Status);
        Assert.Equal("Unavailable", result.Database);
    }

    [Fact]
    public async Task HandleAsync_WhenDatabaseThrows_PropagatesExceptionForMiddleware()
    {
        // El servicio de salud no debe dejar colgada la petición: la excepción sube al ExceptionMiddleware,
        // que responde con ProblemDetails en lugar de un cuelgue de red.
        var databaseHealth = new Mock<IDatabaseHealthService>();
        databaseHealth
            .Setup(servicio => servicio.CanConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("La base no responde."));
        var handler = new GetHealthQueryHandler(databaseHealth.Object);

        await Assert.ThrowsAsync<TimeoutException>(
            () => handler.HandleAsync(new GetHealthQuery()));
    }
}

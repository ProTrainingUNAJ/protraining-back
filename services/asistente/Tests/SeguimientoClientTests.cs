using System.Net;
using Application.Exceptions;
using Infrastructure.Services.Seguimiento;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Asistente.Tests;

public sealed class SeguimientoClientTests
{
    private sealed class TestHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetAthleteCheckInsHistoryAsync_WhenAuthorizationFails_ThrowsInsteadOfReturningEmpty(
        HttpStatusCode statusCode)
    {
        var client = CreateClient(new TestHandler(_ => new HttpResponseMessage(statusCode)));

        var action = () => client.GetAthleteCheckInsHistoryAsync(
            [Guid.NewGuid()],
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 7));

        if (statusCode == HttpStatusCode.Unauthorized)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(action);
        }
        else
        {
            await Assert.ThrowsAsync<SeguimientoServiceUnavailableException>(action);
        }
    }

    [Fact]
    public async Task GetAthleteCheckInsHistoryAsync_WhenServiceFails_ThrowsInsteadOfReturningEmpty()
    {
        var client = CreateClient(new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var action = () => client.GetAthleteCheckInsHistoryAsync(
            [Guid.NewGuid()],
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 7));

        await Assert.ThrowsAsync<SeguimientoServiceUnavailableException>(action);
    }

    [Fact]
    public async Task GetAthleteCheckInsHistoryAsync_WhenNotFound_ReturnsEmpty()
    {
        var client = CreateClient(new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await client.GetAthleteCheckInsHistoryAsync(
            [Guid.NewGuid()],
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 7));

        Assert.Empty(result);
    }

    [Fact]
    public async Task ProfileAndTrendsMethods_When200_DeserializeProperly()
    {
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var handler = new TestHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            string json;
            if (path.EndsWith("/fitness-tests", StringComparison.Ordinal))
            {
                json = $$"""[{"id":"{{Guid.NewGuid()}}","athleteId":"{{athleteId}}","catalogTestId":"{{Guid.NewGuid()}}","testName":"Salto Vertical CMJ","motorCapacity":"Power","value":36.5,"measurementUnit":"cm","attempt":1,"recordedAt":"2026-11-10T10:00:00Z"}]""";
            }
            else if (path.EndsWith("/averages", StringComparison.Ordinal))
            {
                json = $$"""{"categoryId":"{{categoryId}}","athleteCount":10,"weekStart":"2026-11-09","weekEnd":"2026-11-15","fitnessTests":[{"testName":"Salto Vertical CMJ","unit":"cm","average":42.0,"testedAthleteCount":10}],"wellnessMetrics":[]}""";
            }
            else
            {
                json = $$"""{"athleteId":"{{athleteId}}","from":"2026-10-20","to":"2026-11-16","weeks":[{"weekStartDate":"2026-11-10","weekEndDate":"2026-11-16","averageWellness":3.9,"totalLoad":1200,"totalMinutes":200,"checkInCount":4,"loadCount":3}]}""";
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);

        var tests = await client.GetAthleteFitnessTestsAsync(athleteId);
        var test = Assert.Single(tests);
        Assert.Equal("Salto Vertical CMJ", test.TestName);
        Assert.Equal(36.5m, test.Value);

        var averages = await client.GetCategoryAveragesAsync(categoryId);
        Assert.NotNull(averages);
        Assert.Single(averages!.FitnessTests);

        var trends = await client.GetAthleteWeeklyTrendsAsync(athleteId, 4);
        Assert.NotNull(trends);
        Assert.Single(trends!.Weeks);
    }

    [Fact]
    public async Task ProfileAndTrendsMethods_Handle404_401_403_And500()
    {
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var notFoundClient = CreateClient(new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        await Assert.ThrowsAsync<NotFoundException>(() => notFoundClient.GetAthleteFitnessTestsAsync(athleteId));
        await Assert.ThrowsAsync<NotFoundException>(() => notFoundClient.GetCategoryAveragesAsync(categoryId));
        await Assert.ThrowsAsync<NotFoundException>(() => notFoundClient.GetAthleteWeeklyTrendsAsync(athleteId, 4));

        var forbiddenClient = CreateClient(new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => forbiddenClient.GetAthleteFitnessTestsAsync(athleteId));

        var errorClient = CreateClient(new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<SeguimientoServiceUnavailableException>(() => errorClient.GetCategoryAveragesAsync(categoryId));
    }

    private static SeguimientoClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://seguimiento:8080/")
        };

        return new SeguimientoClient(httpClient, NullLogger<SeguimientoClient>.Instance);
    }
}

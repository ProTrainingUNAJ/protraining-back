namespace Asistente.Tests;

using System.Reflection;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Application.UseCases.Ai.SuggestRoutine;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using API.Controllers;

public sealed class AthleteAdaptationUseCaseSecurityTests
{
    [Fact]
    public async Task Given_AthleteNotOwnedByPf_When_AdaptationIsRequested_Then_RejectsBeforeReadingContext()
    {
        var athleteId = Guid.NewGuid();
        var structure = new Mock<IStructureAthleteClient>();
        var context = new Mock<IAthleteAdaptationContextService>();
        var ai = new Mock<IAiService>();
        structure
            .Setup(client => client.IsOwnAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var useCase = new SuggestRoutineUseCase(
            ai.Object,
            adaptationContextService: context.Object,
            structureAthleteClient: structure.Object);

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() =>
            useCase.ExecuteAdaptationAsync(
                new RoutineSuggestionRequestDto(athleteId, "Fuerza"),
                CancellationToken.None));

        context.Verify(
            service => service.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        ai.Verify(
            service => service.SuggestRoutineAsync(
                It.IsAny<RoutineSuggestionRequestDto>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Given_AdaptationEndpoint_When_InspectingMetadata_Then_DocumentsUnauthorizedResponse()
    {
        var method = typeof(AssistantController).GetMethod(nameof(AssistantController.GenerateAthleteAdaptation));

        Assert.NotNull(method);
        Assert.Contains(
            method.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status401Unauthorized);
    }
}

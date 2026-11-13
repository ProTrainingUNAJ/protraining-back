using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Interfaces.Services;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Asistente.Tests;

// Si Ollama y Gemini fallan, las reglas tienen que armar sesiones completas (antes eran 2 ejercicios) y con
// entrada en calor, igual que lo que se le pide a la IA
public sealed class RuleBasedPlanDraftSessionTests
{
    private static List<CatalogExerciseDto> Catalog(string group, int count) =>
        Enumerable.Range(1, count)
            .Select(i => new CatalogExerciseDto(Guid.NewGuid(), $"{group} {i}", group, null))
            .ToList();

    private static RuleBasedAiClient RuleClient(IReadOnlyList<CatalogExerciseDto> catalog)
    {
        var catalogMock = new Mock<IExerciseCatalogService>();
        catalogMock.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(catalog);
        return new RuleBasedAiClient(Mock.Of<IAiAuditService>(), Mock.Of<ILogger<RuleBasedAiClient>>(), catalogMock.Object);
    }

    [Fact]
    public async Task Given_FullCatalog_When_GeneratingWithRules_Then_EachSessionHasFiveDifferentExercisesAndAWarmUp()
    {
        var catalog = Catalog("Piernas", 6).Concat(Catalog("Pecho", 6)).Concat(Catalog("Espalda", 6)).ToList();
        var request = new CreatePlanDraftRequestDto(Goal: "Fuerza como un toro", CategoryId: Guid.NewGuid(), Weeks: 2, DaysPerWeek: 3);

        var content = await RuleClient(catalog).TryGeneratePlanDraftAsync(request);

        Assert.NotNull(content);
        Assert.All(content.Weeks.SelectMany(w => w.Sessions), session =>
        {
            Assert.Equal(5, session.Exercises.Count);
            Assert.Equal(5, session.Exercises.Select(e => e.ExerciseId).Distinct().Count());
            Assert.StartsWith("Entrada en calor (10 min)", session.CoachNotes);
        });
    }

    [Fact]
    public async Task Given_GroupWithFewExercises_When_GeneratingWithRules_Then_DoesNotRepeatExercisesInTheSession()
    {
        var catalog = Catalog("Piernas", 2).Concat(Catalog("Pecho", 6)).Concat(Catalog("Espalda", 6)).ToList();
        var request = new CreatePlanDraftRequestDto(Goal: "Fuerza general", CategoryId: Guid.NewGuid(), Weeks: 1, DaysPerWeek: 3);

        var content = await RuleClient(catalog).TryGeneratePlanDraftAsync(request);

        var legsSession = content!.Weeks[0].Sessions[0];
        Assert.Equal(2, legsSession.Exercises.Count);
        Assert.Equal(2, legsSession.Exercises.Select(e => e.ExerciseId).Distinct().Count());
    }
}

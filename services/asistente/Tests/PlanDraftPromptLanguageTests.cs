namespace Asistente.Tests;

using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Xunit;

// KAN-578: el PF lee el borrador, así que los textos tienen que pedirse en español (las claves del JSON siguen en inglés)
public sealed class PlanDraftPromptLanguageTests
{
    [Fact]
    public void BuildPlanDraftPrompt_AsksForSpanishTextValues()
    {
        var request = new CreatePlanDraftRequestDto(
            CategoryId: Guid.NewGuid(),
            Goal: "Fuerza de piernas",
            Weeks: 1,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 16));
        var exercises = new List<CatalogExerciseDto>
        {
            new(Guid.NewGuid(), "Sentadilla con barra", "Piernas", "Barra")
        };

        var prompt = RoutinePromptBuilder.BuildPlanDraftPrompt(request, exercises);

        Assert.Contains("in Spanish from Argentina", prompt);
        Assert.DoesNotContain("Output ONLY valid JSON in English", prompt);
    }
}

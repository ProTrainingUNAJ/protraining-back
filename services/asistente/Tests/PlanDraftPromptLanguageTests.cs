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

    // El PF pidió sesiones completas: con el ejemplo de un solo ejercicio, Ollama armaba sesiones de 1 o 2
    [Fact]
    public void BuildPlanDraftPrompt_AsksForFiveToSixExercisesPerSession()
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

        Assert.Contains("between 5 and 6 different exercises", prompt);
        Assert.Contains("Entrada en calor (10 min)", prompt);
    }

    [Fact]
    public void BuildPlanDraftPrompt_AsksForTheExactNumberOfWeeksAndSessions()
    {
        var request = new CreatePlanDraftRequestDto(
            CategoryId: Guid.NewGuid(),
            Goal: "Fuerza de tren superior",
            Weeks: 2,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 16));
        var exercises = new List<CatalogExerciseDto>
        {
            new(Guid.NewGuid(), "Press de banca con barra", "Pecho", "Barra")
        };

        var prompt = RoutinePromptBuilder.BuildPlanDraftPrompt(request, exercises);

        Assert.Contains("exactly 2 week objects, with 'weekNumber' from 1 to 2", prompt);
        Assert.Contains("exactly 3 sessions", prompt);
    }

    // "Tren superior" no matcheaba ningún grupo y el modelo recibía mayoría de ejercicios de piernas
    [Theory]
    [InlineData("fuerza de tren superior")]
    [InlineData("Upper body strength")]
    public void ExtractMuscleGroups_UpperBody_ReturnsUpperGroupsWithoutLegs(string goal)
    {
        var groups = RoutinePromptBuilder.ExtractMuscleGroups(goal);

        Assert.Contains("Pecho", groups);
        Assert.Contains("Espalda", groups);
        Assert.Contains("Hombros", groups);
        Assert.DoesNotContain("Piernas", groups);
    }

    [Theory]
    [InlineData("Fuerza de piernas para rodilla, por día quiero 5 ejericcios como maximo", 5)]
    [InlineData("Fuerza de piernas para rodilla, por día quiero 5 ejercicios como maximo", 5)]
    [InlineData("fuerza de piernas para mejorar la rodilla, que la rutina tenga 5 ejercicios por día", 5)]
    [InlineData("Hipertrofia torso, máximo 4 ejercicios por sesión", 4)]
    [InlineData("Potencia, no más de cinco ejercicios", 5)]
    [InlineData("Fuerza general, solo 3 ejercicios", 3)]
    public void ExtractMaxExercisesPerSession_WhenGoalContainsLimit_ReturnsExpectedCount(string goal, int expectedMax)
    {
        var actual = GoalConstraintExtractor.ExtractMaxExercisesPerSession(goal);

        Assert.Equal(expectedMax, actual);
    }

    [Theory]
    [InlineData("Fuerza de piernas para pretemporada, 3 días")]
    [InlineData("Plan de 4 semanas y 3 días por semana")]
    [InlineData("Hipertrofia 4 series de 8 al 75% desde 2026-11-20")]
    public void ExtractMaxExercisesPerSession_WhenGoalHasNoExerciseLimit_ReturnsNull(string goal)
    {
        var actual = GoalConstraintExtractor.ExtractMaxExercisesPerSession(goal);

        Assert.Null(actual);
    }

    [Fact]
    public void BuildBlockPlanDraftPrompt_WhenGoalRequestsMaxFiveExercisesAndKneeFocus_InjectsStrictConstraints()
    {
        var request = new CreatePlanDraftRequestDto(
            CategoryId: Guid.NewGuid(),
            Goal: "Fuerza de piernas para rodilla, por día quiero 5 ejericcios como maximo",
            Weeks: 4,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23));
        var exercises = new List<CatalogExerciseDto>
        {
            new(Guid.NewGuid(), "Prensa de piernas", "Piernas", "Máquina")
        };

        var prompt = RoutinePromptBuilder.BuildBlockPlanDraftPrompt(request, exercises);

        Assert.Contains("como máximo 5 ejercicios por día/sesión", prompt);
        Assert.Contains("warmUp + strengthA + strengthB + accessories <= 5", prompt);
        Assert.Contains("FOCO ARTICULAR / PREVENTIVO EN EL OBJETIVO (rodilla)", prompt);
        Assert.DoesNotContain("Usá entre 5 y 6 ejercicios por sesión", prompt);
    }
}

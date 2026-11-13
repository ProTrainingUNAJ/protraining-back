using System.Text.Json;
using Application.Common.PlanDrafts;
using Application.DTOs.PlanDrafts;
using Xunit;

namespace Asistente.Tests;

// Los modelos chicos de Ollama mezclan idiomas en el día de la semana ("Monday", "Miércoles", "Viernes")
// y eso hacía que el borrador se descartara y respondiera Gemini.
public sealed class FlexibleDayOfWeekConverterTests
{
    private const string MixedLanguageDraft = """
        {
          "name": "Fuerza en pretemporada",
          "objective": "Fuerza máxima",
          "justificationSummary": "Tres sesiones con descarga antes del partido.",
          "weeks": [
            {
              "weekNumber": 1,
              "sessions": [
                { "name": "Día 1", "dayOfWeek": "Monday", "order": 1, "coachNotes": null,
                  "exercises": [ { "exerciseId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "order": 1, "sets": 4, "reps": 5 } ] },
                { "name": "Día 2", "dayOfWeek": "Miércoles", "order": 2, "coachNotes": null,
                  "exercises": [ { "exerciseId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "order": 1, "sets": 4, "reps": 5 } ] },
                { "name": "Día 3", "dayOfWeek": "viernes", "order": 3, "coachNotes": null,
                  "exercises": [ { "exerciseId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "order": 1, "sets": 4, "reps": 5 } ] }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Given_DraftWithDaysInSpanishAndEnglish_When_Parse_Then_IsValidWithTheRightDays()
    {
        var (isValid, content, errorMessage, _) = PlanDraftAiParser.Parse(MixedLanguageDraft);

        Assert.True(isValid, errorMessage);
        Assert.Equal(
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            content!.Weeks[0].Sessions.Select(s => s.DayOfWeek));
    }

    [Theory]
    [InlineData("\"Sábado\"", DayOfWeek.Saturday)]
    [InlineData("\"sabado\"", DayOfWeek.Saturday)]
    [InlineData("\"MARTES\"", DayOfWeek.Tuesday)]
    [InlineData("\"thursday\"", DayOfWeek.Thursday)]
    [InlineData("0", DayOfWeek.Sunday)]
    [InlineData("\"Miérd\"", DayOfWeek.Wednesday)]
    [InlineData("\"Mié\"", DayOfWeek.Wednesday)]
    [InlineData("\"Vier\"", DayOfWeek.Friday)]
    [InlineData("\"Wed\"", DayOfWeek.Wednesday)]
    public void Given_DayInAnySupportedForm_When_Deserialize_Then_ReturnsTheDay(string json, DayOfWeek expected)
    {
        var options = new JsonSerializerOptions { Converters = { new FlexibleDayOfWeekConverter() } };

        Assert.Equal(expected, JsonSerializer.Deserialize<DayOfWeek>(json, options));
    }

    [Theory]
    [InlineData("\"Feriado\"")]
    [InlineData("\"3\"")]
    [InlineData("9")]
    public void Given_InvalidDay_When_Deserialize_Then_Throws(string json)
    {
        var options = new JsonSerializerOptions { Converters = { new FlexibleDayOfWeekConverter() } };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DayOfWeek>(json, options));
    }

    [Fact]
    public void Given_Session_When_Serialize_Then_WritesTheDayInEnglish()
    {
        var session = new PlanDraftSessionDto("Día 1", DayOfWeek.Wednesday, 1, null, []);

        Assert.Contains("\"dayOfWeek\":\"Wednesday\"", JsonSerializer.Serialize(session));
    }
}

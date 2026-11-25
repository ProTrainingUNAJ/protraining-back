namespace Asistente.Tests;

using Application.Common.PlanDrafts;
using Application.DTOs.Ai;
using Xunit;

/// <summary>
/// Pruebas unitarias para el parser de borradores estructurados por bloques funcionales.
/// Valida la traducción de índices numéricos (1..N) a GUIDs de catálogo y la tolerancia a formatos.
/// </summary>
public sealed class PlanDraftBlockParserTests
{
    private static IReadOnlyList<CatalogExerciseDto> CreateSampleCatalog()
    {
        return
        [
            new CatalogExerciseDto(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Sentadilla Trasera", "Piernas", "Barra"),
            new CatalogExerciseDto(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Press Banca", "Pecho", "Barra"),
            new CatalogExerciseDto(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Remo con Barra", "Espalda", "Barra"),
            new CatalogExerciseDto(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Movilidad de Cadera", "Piernas", "Corporal"),
            new CatalogExerciseDto(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Plank Frontal", "Core", "Corporal")
        ];
    }

    [Fact]
    public void Given_EmptyResponse_When_Parsing_Then_ReturnsInvalid()
    {
        var catalog = CreateSampleCatalog();
        var (isValid, content, error) = PlanDraftBlockParser.ParseAndExpand(string.Empty, catalog, requestedWeeks: 4);

        Assert.False(isValid);
        Assert.Null(content);
        Assert.NotNull(error);
    }

    [Fact]
    public void Given_EmptyCatalog_When_Parsing_Then_ReturnsInvalid()
    {
        var json = """{"name":"Plan","objective":"Fuerza","modelSessions":[]}""";
        var (isValid, content, error) = PlanDraftBlockParser.ParseAndExpand(json, [], requestedWeeks: 4);

        Assert.False(isValid);
        Assert.Null(content);
        Assert.NotNull(error);
    }

    [Fact]
    public void Given_ValidBlockJson_When_Parsing_Then_TranslatesNumericIndexesToGuids()
    {
        // Arrange
        var catalog = CreateSampleCatalog();
        var json = """
        {
          "name": "Mesociclo de Fuerza",
          "objective": "Desarrollo de fuerza máxima",
          "justificationSummary": "Estructura por bloques funcionales con progresión determinística",
          "modelSessions": [
            {
              "name": "Día 1 - Pierna",
              "dayOfWeek": "Lunes",
              "order": 1,
              "coachNotes": "Foco en la profundidad",
              "warmUp": [
                { "exerciseIndex": 4, "sets": 2, "reps": 10 }
              ],
              "strengthA": [
                { "exerciseIndex": 1, "sets": 4, "reps": 6, "targetPercentage": 80 }
              ],
              "strengthB": [
                { "exerciseIndex": 3, "sets": 3, "reps": 8, "targetPercentage": 75 }
              ],
              "accessories": [
                { "exerciseIndex": 5, "sets": 3, "reps": 12 }
              ]
            }
          ]
        }
        """;

        // Act
        var (isValid, content, error) = PlanDraftBlockParser.ParseAndExpand(json, catalog, requestedWeeks: 4);

        // Assert
        Assert.True(isValid, error);
        Assert.NotNull(content);
        Assert.Equal("Mesociclo de Fuerza", content.Name);
        Assert.Equal(4, content.Weeks.Count); // Se expandió a 4 semanas

        var week1Session = content.Weeks[0].Sessions[0];
        Assert.Equal(DayOfWeek.Monday, week1Session.DayOfWeek);
        Assert.Equal(4, week1Session.Exercises.Count);

        // Verificar que los exerciseIndex se tradujeron a los GUIDs reales
        Assert.Equal(catalog[3].Id, week1Session.Exercises[0].ExerciseId); // Index 4 -> catálogo[3]
        Assert.Equal(catalog[0].Id, week1Session.Exercises[1].ExerciseId); // Index 1 -> catálogo[0]
        Assert.Equal(catalog[2].Id, week1Session.Exercises[2].ExerciseId); // Index 3 -> catálogo[2]
        Assert.Equal(catalog[4].Id, week1Session.Exercises[3].ExerciseId); // Index 5 -> catálogo[4]
    }

    [Fact]
    public void Given_IndexOutOfRange_When_Parsing_Then_FailsValidationWithoutThrowing()
    {
        // Arrange: Index 99 no existe en un catálogo de 5 ejercicios
        var catalog = CreateSampleCatalog();
        var json = """
        {
          "name": "Plan Inválido",
          "objective": "Test",
          "justificationSummary": "Test",
          "modelSessions": [
            {
              "name": "Sesión 1",
              "dayOfWeek": "Wednesday",
              "order": 1,
              "warmUp": [],
              "strengthA": [
                { "exerciseIndex": 99, "sets": 4, "reps": 6 }
              ],
              "strengthB": [],
              "accessories": []
            }
          ]
        }
        """;

        // Act
        var (isValid, content, error) = PlanDraftBlockParser.ParseAndExpand(json, catalog, requestedWeeks: 1);

        // Assert
        Assert.False(isValid);
        Assert.Null(content);
        Assert.Contains("índice", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Given_MarkdownFences_When_Parsing_Then_CleansFencesSuccessfully()
    {
        var catalog = CreateSampleCatalog();
        var rawWithFences = """
        ```json
        {
          "name": "Plan con Fences",
          "objective": "Test",
          "justificationSummary": "Test",
          "modelSessions": [
            {
              "name": "Sesión 1",
              "dayOfWeek": "Viernes",
              "order": 1,
              "warmUp": [ { "exerciseIndex": 4, "sets": 2, "reps": 10 } ],
              "strengthA": [ { "exerciseIndex": 2, "sets": 4, "reps": 6, "targetPercentage": 80 } ],
              "strengthB": [],
              "accessories": []
            }
          ]
        }
        ```
        """;

        var (isValid, content, error) = PlanDraftBlockParser.ParseAndExpand(rawWithFences, catalog, requestedWeeks: 2);

        Assert.True(isValid, error);
        Assert.NotNull(content);
        Assert.Equal("Plan con Fences", content.Name);
        Assert.Equal(DayOfWeek.Friday, content.Weeks[0].Sessions[0].DayOfWeek);
    }

    [Fact]
    public void Given_UncatalogedExerciseWithNameAndMuscleGroup_When_Parsing_Then_IncludesAsReference()
    {
        var catalog = CreateSampleCatalog();
        var json = """
        {
          "name": "Plan con Referencia",
          "objective": "Potencia",
          "justificationSummary": "Incluye ejercicio nuevo como referencia",
          "modelSessions": [
            {
              "name": "Sesión 1",
              "dayOfWeek": "Lunes",
              "order": 1,
              "warmUp": [],
              "strengthA": [
                { "exerciseIndex": null, "exerciseName": "Hip Thrust Unilateral", "muscleGroup": "Piernas", "sets": 4, "reps": 8, "targetPercentage": 75 }
              ],
              "strengthB": [],
              "accessories": []
            }
          ]
        }
        """;

        var (isValid, content, error) = PlanDraftBlockParser.ParseAndExpand(json, catalog, requestedWeeks: 2);

        Assert.True(isValid, error);
        Assert.NotNull(content);
        var exercise = Assert.Single(content.Weeks[0].Sessions[0].Exercises);
        Assert.Null(exercise.ExerciseId);
        Assert.True(exercise.IsReference);
        Assert.Equal("Hip Thrust Unilateral", exercise.ExerciseName);
        Assert.Equal("Piernas", exercise.MuscleGroup);
    }

    [Fact]
    public void Given_ExerciseWithNullIndexAndExistingCatalogName_When_Parsing_Then_LinksExistingCatalogId()
    {
        var catalog = CreateSampleCatalog();
        var json = """
        {
          "name": "Plan con Nombre Existente",
          "objective": "Fuerza",
          "justificationSummary": "Resuelve por nombre si ya existe en catálogo",
          "modelSessions": [
            {
              "name": "Sesión 1",
              "dayOfWeek": "Martes",
              "order": 1,
              "warmUp": [],
              "strengthA": [
                { "exerciseIndex": null, "exerciseName": "  sentadilla trasera ", "muscleGroup": "Piernas", "sets": 4, "reps": 6, "targetPercentage": 80 }
              ],
              "strengthB": [],
              "accessories": []
            }
          ]
        }
        """;

        var (isValid, content, error) = PlanDraftBlockParser.ParseAndExpand(json, catalog, requestedWeeks: 1);

        Assert.True(isValid, error);
        Assert.NotNull(content);
        var exercise = Assert.Single(content.Weeks[0].Sessions[0].Exercises);
        Assert.Equal(catalog[0].Id, exercise.ExerciseId);
        Assert.False(exercise.IsReference);
        Assert.Equal("Sentadilla Trasera", exercise.ExerciseName);
    }

    [Fact]
    public void Given_ModelSessionWithSixExercisesAndMaxFiveRequested_When_Parsing_Then_TrimsToFivePreservingCoreLifts()
    {
        var catalog = CreateSampleCatalog();
        var json = """
        {
          "name": "Planificación Fuerza de Piernas para Rodilla",
          "objective": "Incrementar fuerza cuidando rodilla",
          "justificationSummary": "6 ejercicios devueltos por el modelo cuando el PF pidió máximo 5",
          "modelSessions": [
            {
              "name": "Sesión 1 - Fuerza Piernas",
              "dayOfWeek": "Monday",
              "order": 1,
              "coachNotes": "Controlar fase excéntrica",
              "warmUp": [
                { "exerciseIndex": 4, "sets": 2, "reps": 8 },
                { "exerciseIndex": null, "exerciseName": "Círculos de cadera", "muscleGroup": "Piernas", "sets": 2, "reps": 10 }
              ],
              "strengthA": [
                { "exerciseIndex": 1, "sets": 4, "reps": 6, "targetPercentage": 80 }
              ],
              "strengthB": [
                { "exerciseIndex": 3, "sets": 3, "reps": 8, "targetPercentage": 75 }
              ],
              "accessories": [
                { "exerciseIndex": 5, "sets": 3, "reps": 12 },
                { "exerciseIndex": 2, "sets": 3, "reps": 15 }
              ]
            }
          ]
        }
        """;

        var (isValid, content, error) = PlanDraftBlockParser.ParseAndExpand(
            json,
            catalog,
            requestedWeeks: 4,
            maxExercisesPerSession: 5);

        Assert.True(isValid, error);
        Assert.NotNull(content);
        Assert.All(content.Weeks, week =>
        {
            var session = Assert.Single(week.Sessions);
            Assert.Equal(5, session.Exercises.Count);
            Assert.Equal([1, 2, 3, 4, 5], session.Exercises.Select(e => e.Order).ToArray());
            // Verifica que se preservaron los bloques principales de fuerza (Sentadilla Trasera y Remo con Barra)
            Assert.Contains(session.Exercises, e => e.ExerciseId == catalog[0].Id);
            Assert.Contains(session.Exercises, e => e.ExerciseId == catalog[2].Id);
        });
    }

    [Fact]
    public void Given_ModelSessionWithFourExercisesAndEightRequested_When_Parsing_Then_CompletesToEightRespectingRestrictions()
    {
        var restrictedId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var substituteId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var catalog = new List<CatalogExerciseDto>
        {
            new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Sentadilla con barra", "Piernas", "Barra"),
            new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Prensa de piernas", "Piernas", "Máquina"),
            new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Hip thrust con barra", "Piernas", "Barra"),
            new(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Zancadas con mancuernas", "Piernas", "Mancuernas"),
            new(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Curl femoral acostado", "Piernas", "Máquina"),
            new(Guid.Parse("66666666-6666-6666-6666-666666666666"), "Elevación de gemelos", "Piernas", "Máquina"),
            new(Guid.Parse("77777777-7777-7777-7777-777777777777"), "Movilidad de cadera", "Piernas", "Corporal"),
            new(restrictedId, "Peso muerto piernas rígidas", "Piernas", "Barra"),
            new(substituteId, "Puente de glúteos", "Piernas", "Corporal")
        };

        var adaptationContext = new Application.DTOs.Adaptation.AthleteAdaptationContextDto(
            AthleteId: Guid.NewGuid(),
            ActiveInjuries: [],
            CurrentRecords: [],
            Restrictions:
            [
                new Application.DTOs.Adaptation.ExerciseRestrictionContextDto(
                    ExerciseId: restrictedId,
                    Zone: "isquiotibiales",
                    Reason: "Sobrecarga",
                    Substitutes: [new Application.DTOs.Adaptation.AuthorizedSubstituteDto(substituteId, "Puente de glúteos")])
            ]);

        var json = """
        {
          "name": "Plan Piernas",
          "objective": "Fuerza de piernas, rutina de 8 ejercicios",
          "justificationSummary": "El modelo devolvió solo 4 ejercicios y el PF pidió 8",
          "modelSessions": [
            {
              "name": "Sesión 1",
              "dayOfWeek": "Monday",
              "order": 1,
              "coachNotes": "Nota inicial",
              "warmUp": [
                { "exerciseIndex": 7, "sets": 2, "reps": 10 }
              ],
              "strengthA": [
                { "exerciseIndex": 1, "sets": 4, "reps": 5, "targetPercentage": 85 }
              ],
              "strengthB": [
                { "exerciseIndex": 2, "sets": 3, "reps": 8, "targetPercentage": 75 }
              ],
              "accessories": [
                { "exerciseIndex": 5, "sets": 3, "reps": 12 }
              ]
            }
          ]
        }
        """;

        var (isValid, content, error) = PlanDraftBlockParser.ParseAndExpand(
            json,
            catalog,
            requestedWeeks: 2,
            maxExercisesPerSession: 8,
            goal: "Fuerza de piernas, rutina de 8 ejercicios",
            adaptationContext: adaptationContext);

        Assert.True(isValid, error);
        Assert.NotNull(content);
        Assert.All(content.Weeks, week =>
        {
            var session = Assert.Single(week.Sessions);
            Assert.Equal(8, session.Exercises.Count);
            Assert.Equal(8, session.Exercises.Select(e => e.ExerciseId).Distinct().Count());
            Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], session.Exercises.Select(e => e.Order).ToArray());
            Assert.DoesNotContain(session.Exercises, e => e.ExerciseId == restrictedId);
            Assert.Contains(session.Exercises, e => e.ExerciseId == substituteId);
        });
    }

    [Fact]
    public void Given_CatalogSmallerThanRequestedCount_When_Parsing_Then_UsesAllAvailableAndAddsWarningNote()
    {
        var catalog = CreateSampleCatalog(); // 5 ejercicios en total (2 Piernas, 1 Pecho, 1 Espalda, 1 Core)
        var json = """
        {
          "name": "Plan General",
          "objective": "rutina de 8 ejercicios",
          "justificationSummary": "Resumen original.",
          "modelSessions": [
            {
              "name": "Sesión 1",
              "dayOfWeek": "Monday",
              "order": 1,
              "coachNotes": "Nota de sesión.",
              "warmUp": [ { "exerciseIndex": 4, "sets": 2, "reps": 10 } ],
              "strengthA": [ { "exerciseIndex": 1, "sets": 4, "reps": 6, "targetPercentage": 80 } ],
              "strengthB": [ { "exerciseIndex": 2, "sets": 3, "reps": 8, "targetPercentage": 75 } ],
              "accessories": []
            }
          ]
        }
        """;

        var (isValid, content, error) = PlanDraftBlockParser.ParseAndExpand(
            json,
            catalog,
            requestedWeeks: 1,
            maxExercisesPerSession: 8,
            goal: "rutina de 8 ejercicios");

        Assert.True(isValid, error);
        Assert.NotNull(content);
        var session = Assert.Single(content.Weeks[0].Sessions);
        Assert.Equal(5, session.Exercises.Count);
        Assert.Contains("se pidieron 8 ejercicios por sesión", content.JustificationSummary);
        Assert.Contains("solo cuenta con 5 ejercicios compatibles", content.JustificationSummary);
        Assert.Contains("se pidieron 8 ejercicios por sesión", session.CoachNotes);
    }

    [Fact]
    public void Given_RequestedCountAboveTwelve_When_Parsing_Then_CapsToTwelveAndAddsWarningNote()
    {
        var catalog = Enumerable.Range(1, 15)
            .Select(i => new CatalogExerciseDto(Guid.NewGuid(), $"Sentadilla variante {i}", "Piernas", "Barra"))
            .ToList();

        var json = """
        {
          "name": "Plan",
          "objective": "Fuerza de piernas, rutina de 15 ejercicios",
          "justificationSummary": "Resumen base.",
          "modelSessions": [
            {
              "name": "Sesión 1",
              "dayOfWeek": "Monday",
              "order": 1,
              "coachNotes": "Nota base.",
              "warmUp": [ { "exerciseIndex": 1, "sets": 2, "reps": 10 } ],
              "strengthA": [ { "exerciseIndex": 2, "sets": 4, "reps": 5, "targetPercentage": 80 } ],
              "strengthB": [ { "exerciseIndex": 3, "sets": 3, "reps": 8, "targetPercentage": 75 } ],
              "accessories": []
            }
          ]
        }
        """;

        var (isValid, content, error) = PlanDraftBlockParser.ParseAndExpand(
            json,
            catalog,
            requestedWeeks: 1,
            goal: "Fuerza de piernas, rutina de 15 ejercicios");

        Assert.True(isValid, error);
        Assert.NotNull(content);
        var session = Assert.Single(content.Weeks[0].Sessions);
        Assert.Equal(12, session.Exercises.Count);
        Assert.Contains("se pidieron 15 ejercicios por sesión", content.JustificationSummary);
        Assert.Contains("máximo permitido es de 12", content.JustificationSummary);
    }
}

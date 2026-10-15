using Application.Common.Ai;
using Xunit;

namespace Asistente.Tests;

public sealed class AiRoutineParserTests
{
    [Fact]
    public void Given_Valid_Json_When_ParseAndValidate_Then_Returns_Success_With_Mapped_Exercises()
    {
        // Arrange
        const string json = """
        {
          "tituloPropuesto": "Fuerza e Hipertrofia",
          "resumenJustificacion": "Adaptación progresiva con sobrecarga.",
          "ejercicios": [
            {
              "ejercicioNombre": "Sentadilla Trasera",
              "series": 4,
              "repeticiones": 6,
              "porcentajeSugerido": 80.5
            },
            {
              "ejercicioNombre": "Press de Banca",
              "series": 3,
              "repeticiones": 10,
              "porcentajeSugerido": 70.0
            }
          ]
        }
        """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(json);

        // Assert
        Assert.True(result.IsValid);
        Assert.False(result.IsSyntaxError);
        Assert.Empty(result.Errors);
        Assert.Equal("Fuerza e Hipertrofia", result.ProposedTitle);
        Assert.Equal("Adaptación progresiva con sobrecarga.", result.JustificationSummary);
        Assert.Equal(2, result.Exercises.Count);

        Assert.Equal("Sentadilla Trasera", result.Exercises[0].ExerciseName);
        Assert.Equal(4, result.Exercises[0].Sets);
        Assert.Equal(6, result.Exercises[0].Reps);
        Assert.Equal(80.5m, result.Exercises[0].SuggestedPercentage);

        Assert.Equal("Press de Banca", result.Exercises[1].ExerciseName);
        Assert.Equal(3, result.Exercises[1].Sets);
        Assert.Equal(10, result.Exercises[1].Reps);
        Assert.Equal(70.0m, result.Exercises[1].SuggestedPercentage);
    }

    [Fact]
    public void Given_Valid_Json_Wrapped_In_Markdown_Code_Fences_When_ParseAndValidate_Then_Strips_Fences_And_Succeeds()
    {
        // Arrange
        const string markdown = """
        Aquí tienes la rutina:
        ```json
        {
          "tituloPropuesto": "Potencia Sprint",
          "resumenJustificacion": "Enfoque neuromuscular.",
          "ejercicios": [
            {
              "ejercicioNombre": "Salto al Cajón",
              "series": 5,
              "repeticiones": 5,
              "porcentajeSugerido": 50.0
            }
          ]
        }
        ```
        Espero que te sirva.
        """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(markdown);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("Potencia Sprint", result.ProposedTitle);
        Assert.Single(result.Exercises);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void Given_Empty_Or_Whitespace_Text_When_ParseAndValidate_Then_Returns_Failure(string emptyText)
    {
        // Act
        var result = AiRoutineParser.ParseAndValidate(emptyText);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("vacío", result.ErrorMessage);
    }

    [Fact]
    public void Given_Malformed_Json_Syntax_When_ParseAndValidate_Then_Returns_Syntax_Failure()
    {
        // Arrange
        const string invalidJson = "{ esto no es un json valido }";

        // Act
        var result = AiRoutineParser.ParseAndValidate(invalidJson);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.IsSyntaxError);
        Assert.Contains("Sintaxis JSON inválida", result.ErrorMessage);
    }

    [Fact]
    public void Given_Json_Not_An_Object_When_ParseAndValidate_Then_Returns_Failure()
    {
        // Arrange
        const string arrayJson = "[ { \"tituloPropuesto\": \"Test\" } ]";

        // Act
        var result = AiRoutineParser.ParseAndValidate(arrayJson);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("debe ser un objeto JSON", result.ErrorMessage);
    }

    [Fact]
    public void Given_Json_Missing_TituloPropuesto_When_ParseAndValidate_Then_Rejects_And_Reports_Concrete_Error()
    {
        // Arrange
        const string json = """
        {
          "resumenJustificacion": "Explicación válida",
          "ejercicios": [
            {
              "ejercicioNombre": "Dominadas",
              "series": 3,
              "repeticiones": 8,
              "porcentajeSugerido": 60.0
            }
          ]
        }
        """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(json);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Falta el campo obligatorio 'tituloPropuesto'", result.ErrorMessage);
    }

    [Fact]
    public void Given_Json_With_Series_As_String_When_ParseAndValidate_Then_Rejects_And_Reports_Type_Mismatch()
    {
        // Arrange
        const string json = """
        {
          "tituloPropuesto": "Rutina con Tipo Incorrecto",
          "resumenJustificacion": "Prueba de tipo incorrecto",
          "ejercicios": [
            {
              "ejercicioNombre": "Sentadilla",
              "series": "cuatro",
              "repeticiones": 10,
              "porcentajeSugerido": 70.0
            }
          ]
        }
        """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(json);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("El campo 'series' en el ejercicio #1 debe ser un número entero", result.ErrorMessage);
    }

    [Fact]
    public void Given_Json_With_Empty_Exercises_Array_When_ParseAndValidate_Then_Rejects()
    {
        // Arrange
        const string json = """
        {
          "tituloPropuesto": "Rutina Sin Ejercicios",
          "resumenJustificacion": "Sin ejercicios",
          "ejercicios": []
        }
        """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(json);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("debe contener al menos un ejercicio", result.ErrorMessage);
    }

    [Fact]
    public void Given_Json_With_Percentage_Above_100_When_ParseAndValidate_Then_Rejects_Range_Violation()
    {
        // Arrange
        const string json = """
        {
          "tituloPropuesto": "Rutina Porcentaje Excesivo",
          "resumenJustificacion": "Excede el límite porcentual",
          "ejercicios": [
            {
              "ejercicioNombre": "Peso Muerto",
              "series": 3,
              "repeticiones": 5,
              "porcentajeSugerido": 150.0
            }
          ]
        }
        """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(json);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("porcentajeSugerido", result.ErrorMessage);
        Assert.Contains("entre 0.0 y 100.0", result.ErrorMessage);
    }

    [Fact]
    public void Given_Json_With_Negative_Series_When_ParseAndValidate_Then_Rejects_Range_Violation()
    {
        // Arrange
        const string json = """
        {
          "tituloPropuesto": "Rutina Series Negativas",
          "resumenJustificacion": "Series menor a cero",
          "ejercicios": [
            {
              "ejercicioNombre": "Remo con Barra",
              "series": -2,
              "repeticiones": 10,
              "porcentajeSugerido": 70.0
            }
          ]
        }
        """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(json);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("El campo 'series' en el ejercicio #1 debe ser mayor a 0", result.ErrorMessage);
    }

    [Fact]
    public void Given_Json_With_Reps_Exceeding_100_When_ParseAndValidate_Then_Rejects_Max_Range()
    {
        // Arrange
        const string json = """
        {
          "tituloPropuesto": "Rutina Repeticiones Excesivas",
          "resumenJustificacion": "Repeticiones excesivas",
          "ejercicios": [
            {
              "ejercicioNombre": "Flexiones",
              "series": 3,
              "repeticiones": 250,
              "porcentajeSugerido": 40.0
            }
          ]
        }
        """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(json);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("repeticiones", result.ErrorMessage);
        Assert.Contains("supera el límite máximo permitido de 100", result.ErrorMessage);
    }

    [Fact]
    public void Given_PascalCase_Json_Properties_When_ParseAndValidate_Then_Parses_Correctly()
    {
        // Arrange
        const string json = """
        {
          "TituloPropuesto": "Rutina PascalCase",
          "ResumenJustificacion": "Modelo generó propiedades en mayúsculas.",
          "Ejercicios": [
            {
              "EjercicioNombre": "Press Militar",
              "Series": 4,
              "Repeticiones": 8,
              "PorcentajeSugerido": 65.0
            }
          ]
        }
        """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(json);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("Rutina PascalCase", result.ProposedTitle);
        Assert.Single(result.Exercises);
        Assert.Equal("Press Militar", result.Exercises[0].ExerciseName);
    }
}

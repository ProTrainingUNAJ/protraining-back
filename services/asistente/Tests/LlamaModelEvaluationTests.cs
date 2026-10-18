namespace Asistente.Tests;

using System.Net;
using System.Text.Json;
using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Services.Ai.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias de evaluación y compatibilidad del modelo Llama seleccionado (KAN-374 / #079).
/// Valida la configuración predeterminada de 'llama3.2:1b', el formato de payload para Ollama,
/// el procesamiento de respuestas JSON típicas y la resiliencia ante tiempos de espera.
/// </summary>
public sealed class LlamaModelEvaluationTests
{
    private readonly Mock<IAiAuditService> _auditMock = new();

    [Fact]
    public void Given_DefaultOllamaOptions_WhenInstantiated_ThenDefaultModelIsLlama3_2_1b()
    {
        // Arrange & Act
        var options = new OllamaOptions();

        // Assert: El modelo por defecto debe ser llama3.2:1b (seleccionado por benchmark en KAN-374)
        Assert.Equal("llama3.2:1b", options.Model);
        Assert.Equal("http://localhost:11434", options.BaseUrl);
        Assert.Equal(15, options.TimeoutSeconds);
    }

    [Fact]
    public void Given_RealisticLlama3_2_1b_JsonResponse_WhenParsed_ThenExtractsStructuredRoutineSuccessfully()
    {
        // Arrange: Respuesta típica generada por llama3.2:1b en formato JSON estricto
        var rawLlamaResponse = """
        {
          "tituloPropuesto": "Fuerza Máxima de Piernas",
          "resumenJustificacion": "Enfoque en sentadilla y pesos pesados para atletas de potencia.",
          "ejercicios": [
            {
              "ejercicioNombre": "Sentadilla trasera con barra",
              "series": 4,
              "repeticiones": 6,
              "porcentajeSugerido": 80.0
            },
            {
              "ejercicioNombre": "Prensa de piernas 45°",
              "series": 3,
              "repeticiones": 8,
              "porcentajeSugerido": 75.0
            }
          ]
        }
        """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(rawLlamaResponse);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Fuerza Máxima de Piernas", result.ProposedTitle);
        Assert.Equal(2, result.Exercises.Count);
        Assert.Equal("Sentadilla trasera con barra", result.Exercises[0].ExerciseName);
        Assert.Equal(4, result.Exercises[0].Sets);
        Assert.Equal(6, result.Exercises[0].Reps);
        Assert.Equal(80.0m, result.Exercises[0].SuggestedPercentage);
    }

    [Fact]
    public void Given_LlamaResponseWithMarkdownFences_WhenParsed_ThenCleansAndParsesCorrectly()
    {
        // Arrange: En ocasiones modelos pequeños pueden incluir fences markdown ```json ... ```
        var fencedResponse = """
        ```json
        {
          "tituloPropuesto": "Hipertrofia de Pecho",
          "resumenJustificacion": "Volumen intermedio",
          "ejercicios": [
            {
              "ejercicioNombre": "Press de banca plano",
              "series": 4,
              "repeticiones": 10,
              "porcentajeSugerido": 70.0
            }
          ]
        }
        ```
        """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(fencedResponse);

        // Assert: El parser debe limpiar las vallas de código y procesar el contenido
        Assert.NotNull(result);
        Assert.Equal("Hipertrofia de Pecho", result.ProposedTitle);
        Assert.Single(result.Exercises);
        Assert.Equal("Press de banca plano", result.Exercises[0].ExerciseName);
    }

    [Fact]
    public async Task Given_SlowModelExceedingTimeout_WhenTrySuggestRoutineAsync_ThenAbortsGracefullyAndAuditsTimeout()
    {
        // Arrange: Simula un modelo pesado (e.g. 8B) que tarda más del timeout configurado
        var handler = new DelayedHttpMessageHandler(TimeSpan.FromMilliseconds(300));
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            Model = "llama3.1:8b",
            TimeoutSeconds = 0 // timeout inmediato para probar aborto seguro
        });

        var client = new OllamaClient(
            httpClient,
            options,
            _auditMock.Object,
            NullLogger<OllamaClient>.Instance
        );

        var request = new RoutineSuggestionRequestDto(
            AthleteId: Guid.NewGuid(),
            Goal: "Fuerza",
            MuscleGroupEmphasis: ["Piernas"]
        );

        // Act
        var result = await client.TrySuggestRoutineAsync(request);

        // Assert: Debe retornar null para activar el respaldo resiliente sin lanzar excepción no controlada
        Assert.Null(result);

        // Debe registrar la auditoría de Timeout
        _auditMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" &&
            log.ValidationResult == "Timeout" &&
            !log.IsValid
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class DelayedHttpMessageHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"response": "{}"}""")
            };
        }
    }
}

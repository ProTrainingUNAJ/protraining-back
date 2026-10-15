using System.Text.Json;
using Application.DTOs.Ai;

namespace Application.Common.Ai;

/// <summary>
/// Parser y validador de esquema JSON estricto para las respuestas generadas por los LLM (Ollama, Gemini).
/// Valida tipos de datos primitivos, campos obligatorios y rangos lógicos deportivos.
/// </summary>
public static class AiRoutineParser
{
    public static AiRoutineValidationResult ParseAndValidate(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return AiRoutineValidationResult.Failure("El texto recibido del modelo de IA está vacío.");
        }

        var cleanedJson = CleanMarkdownFences(rawResponse);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(cleanedJson);
        }
        catch (JsonException ex)
        {
            return AiRoutineValidationResult.SyntaxFailure($"Sintaxis JSON inválida devuelta por la IA: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return AiRoutineValidationResult.Failure("El payload devuelto por la IA debe ser un objeto JSON.");
            }

            var errors = new List<string>();

            // 1. Validar tituloPropuesto
            string? proposedTitle = null;
            if (!TryGetPropertyCaseInsensitive(root, "tituloPropuesto", out var titleProp))
            {
                errors.Add("Falta el campo obligatorio 'tituloPropuesto'.");
            }
            else if (titleProp.ValueKind != JsonValueKind.String)
            {
                errors.Add($"El campo 'tituloPropuesto' debe ser de tipo string (se recibió {titleProp.ValueKind}).");
            }
            else if (string.IsNullOrWhiteSpace(titleProp.GetString()))
            {
                errors.Add("El campo 'tituloPropuesto' no puede estar vacío.");
            }
            else
            {
                proposedTitle = titleProp.GetString()!.Trim();
            }

            // 2. Validar resumenJustificacion
            string? justificationSummary = null;
            if (!TryGetPropertyCaseInsensitive(root, "resumenJustificacion", out var justProp))
            {
                errors.Add("Falta el campo obligatorio 'resumenJustificacion'.");
            }
            else if (justProp.ValueKind != JsonValueKind.String)
            {
                errors.Add($"El campo 'resumenJustificacion' debe ser de tipo string (se recibió {justProp.ValueKind}).");
            }
            else if (string.IsNullOrWhiteSpace(justProp.GetString()))
            {
                errors.Add("El campo 'resumenJustificacion' no puede estar vacío.");
            }
            else
            {
                justificationSummary = justProp.GetString()!.Trim();
            }

            // 3. Validar ejercicios
            var exercises = new List<SuggestedExerciseDto>();
            if (!TryGetPropertyCaseInsensitive(root, "ejercicios", out var exercisesProp))
            {
                errors.Add("Falta el campo obligatorio 'ejercicios'.");
            }
            else if (exercisesProp.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"El campo 'ejercicios' debe ser una lista/array (se recibió {exercisesProp.ValueKind}).");
            }
            else if (exercisesProp.GetArrayLength() == 0)
            {
                errors.Add("La lista de 'ejercicios' debe contener al menos un ejercicio.");
            }
            else
            {
                var index = 0;
                foreach (var exerciseElement in exercisesProp.EnumerateArray())
                {
                    index++;
                    if (exerciseElement.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add($"El ejercicio #{index} debe ser un objeto JSON.");
                        continue;
                    }

                    // ejercicioNombre
                    string? exerciseName = null;
                    if (!TryGetPropertyCaseInsensitive(exerciseElement, "ejercicioNombre", out var nameProp))
                    {
                        errors.Add($"El ejercicio #{index} no contiene el campo obligatorio 'ejercicioNombre'.");
                    }
                    else if (nameProp.ValueKind != JsonValueKind.String)
                    {
                        errors.Add($"El campo 'ejercicioNombre' en el ejercicio #{index} debe ser de tipo string (se recibió {nameProp.ValueKind}).");
                    }
                    else if (string.IsNullOrWhiteSpace(nameProp.GetString()))
                    {
                        errors.Add($"El campo 'ejercicioNombre' en el ejercicio #{index} no puede estar vacío.");
                    }
                    else
                    {
                        exerciseName = nameProp.GetString()!.Trim();
                    }

                    // series
                    int? sets = null;
                    if (!TryGetPropertyCaseInsensitive(exerciseElement, "series", out var setsProp))
                    {
                        errors.Add($"El ejercicio #{index} no contiene el campo obligatorio 'series'.");
                    }
                    else if (setsProp.ValueKind != JsonValueKind.Number)
                    {
                        errors.Add($"El campo 'series' en el ejercicio #{index} debe ser un número entero (se recibió {setsProp.ValueKind}).");
                    }
                    else if (!setsProp.TryGetInt32(out var parsedSets))
                    {
                        errors.Add($"El campo 'series' en el ejercicio #{index} no es un número entero válido.");
                    }
                    else if (parsedSets <= 0)
                    {
                        errors.Add($"El campo 'series' en el ejercicio #{index} debe ser mayor a 0 (se recibió {parsedSets}).");
                    }
                    else if (parsedSets > 20)
                    {
                        errors.Add($"El campo 'series' en el ejercicio #{index} supera el límite máximo permitido de 20 (se recibió {parsedSets}).");
                    }
                    else
                    {
                        sets = parsedSets;
                    }

                    // repeticiones
                    int? reps = null;
                    if (!TryGetPropertyCaseInsensitive(exerciseElement, "repeticiones", out var repsProp))
                    {
                        errors.Add($"El ejercicio #{index} no contiene el campo obligatorio 'repeticiones'.");
                    }
                    else if (repsProp.ValueKind != JsonValueKind.Number)
                    {
                        errors.Add($"El campo 'repeticiones' en el ejercicio #{index} debe ser un número entero (se recibió {repsProp.ValueKind}).");
                    }
                    else if (!repsProp.TryGetInt32(out var parsedReps))
                    {
                        errors.Add($"El campo 'repeticiones' en el ejercicio #{index} no es un número entero válido.");
                    }
                    else if (parsedReps <= 0)
                    {
                        errors.Add($"El campo 'repeticiones' en el ejercicio #{index} debe ser mayor a 0 (se recibió {parsedReps}).");
                    }
                    else if (parsedReps > 100)
                    {
                        errors.Add($"El campo 'repeticiones' en el ejercicio #{index} supera el límite máximo permitido de 100 (se recibió {parsedReps}).");
                    }
                    else
                    {
                        reps = parsedReps;
                    }

                    // porcentajeSugerido
                    decimal? percentage = null;
                    if (!TryGetPropertyCaseInsensitive(exerciseElement, "porcentajeSugerido", out var pctProp))
                    {
                        errors.Add($"El ejercicio #{index} no contiene el campo obligatorio 'porcentajeSugerido'.");
                    }
                    else if (pctProp.ValueKind != JsonValueKind.Number)
                    {
                        errors.Add($"El campo 'porcentajeSugerido' en el ejercicio #{index} debe ser numérico (se recibió {pctProp.ValueKind}).");
                    }
                    else if (!pctProp.TryGetDecimal(out var parsedPct))
                    {
                        errors.Add($"El campo 'porcentajeSugerido' en el ejercicio #{index} no es un número decimal válido.");
                    }
                    else if (parsedPct < 0m || parsedPct > 100m)
                    {
                        errors.Add($"El campo 'porcentajeSugerido' en el ejercicio #{index} debe estar entre 0.0 y 100.0 (se recibió {parsedPct}).");
                    }
                    else
                    {
                        percentage = parsedPct;
                    }

                    if (exerciseName != null && sets.HasValue && reps.HasValue && percentage.HasValue)
                    {
                        exercises.Add(new SuggestedExerciseDto(exerciseName, sets.Value, reps.Value, percentage.Value));
                    }
                }
            }

            if (errors.Count > 0)
            {
                return AiRoutineValidationResult.Failure(errors);
            }

            return AiRoutineValidationResult.Success(proposedTitle!, justificationSummary!, exercises);
        }
    }

    public static string CleanMarkdownFences(string text)
    {
        var trimmed = text.Trim();
        var startIndex = trimmed.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (startIndex >= 0)
        {
            startIndex += 7;
            var endIndex = trimmed.IndexOf("```", startIndex, StringComparison.OrdinalIgnoreCase);
            if (endIndex > startIndex)
            {
                return trimmed[startIndex..endIndex].Trim();
            }
            return trimmed[startIndex..].Trim();
        }

        startIndex = trimmed.IndexOf("```", StringComparison.OrdinalIgnoreCase);
        if (startIndex >= 0)
        {
            startIndex += 3;
            var endIndex = trimmed.IndexOf("```", startIndex, StringComparison.OrdinalIgnoreCase);
            if (endIndex > startIndex)
            {
                return trimmed[startIndex..endIndex].Trim();
            }
            return trimmed[startIndex..].Trim();
        }

        return trimmed;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

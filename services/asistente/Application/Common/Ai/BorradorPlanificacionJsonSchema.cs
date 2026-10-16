namespace Application.Common.Ai;

/// <summary>
/// Define el esquema JSON Schema estricto (Draft 2020-12) que toda respuesta generada
/// por proveedores de inteligencia artificial (Ollama, Gemini) debe cumplir obligatoriamente.
/// </summary>
public static class BorradorPlanificacionJsonSchema
{
    public const string SchemaDefinition = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "title": "BorradorPlanificacionDTO",
      "description": "Esquema estricto para propuestas de entrenamiento generadas por modelos de IA.",
      "type": "object",
      "required": ["tituloPropuesto", "resumenJustificacion", "ejercicios"],
      "properties": {
        "tituloPropuesto": {
          "type": "string",
          "minLength": 3
        },
        "resumenJustificacion": {
          "type": "string",
          "minLength": 5
        },
        "ejercicios": {
          "type": "array",
          "minItems": 1,
          "items": {
            "type": "object",
            "required": ["ejercicioNombre", "series", "repeticiones", "porcentajeSugerido"],
            "properties": {
              "ejercicioId": {
                "type": ["string", "null"]
              },
              "ejercicioNombre": {
                "type": "string",
                "minLength": 2
              },
              "series": {
                "type": "integer",
                "minimum": 1,
                "maximum": 20
              },
              "repeticiones": {
                "type": "integer",
                "minimum": 1,
                "maximum": 100
              },
              "porcentajeSugerido": {
                "type": "number",
                "minimum": 0.0,
                "maximum": 100.0
              },
              "esReferencia": {
                "type": "boolean"
              }
            }
          }
        }
      }
    }
    """;
}

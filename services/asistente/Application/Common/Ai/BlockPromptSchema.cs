namespace Application.Common.Ai;

/// <summary>
/// Define el esquema JSON forzado (Structured Outputs) para la generación de borradores por bloques.
/// Utilizado por proveedores compatibles con la API de OpenAI, Gemini y Ollama format schema.
/// </summary>
public static class BlockPromptSchema
{
    public const string SchemaJson = """
    {
      "type": "object",
      "properties": {
        "name": { "type": "string" },
        "objective": { "type": "string" },
        "justificationSummary": { "type": "string" },
        "modelSessions": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "name": { "type": "string" },
              "dayOfWeek": { "type": "string" },
              "order": { "type": "integer" },
              "coachNotes": { "type": "string" },
              "warmUp": {
                "type": "array",
                "items": {
                  "type": "object",
                  "properties": {
                    "exerciseIndex": { "type": "integer" },
                    "sets": { "type": "integer" },
                    "reps": { "type": "integer" }
                  },
                  "required": ["exerciseIndex", "sets", "reps"]
                }
              },
              "strengthA": {
                "type": "array",
                "items": {
                  "type": "object",
                  "properties": {
                    "exerciseIndex": { "type": "integer" },
                    "sets": { "type": "integer" },
                    "reps": { "type": "integer" },
                    "targetPercentage": { "type": "number" }
                  },
                  "required": ["exerciseIndex", "sets", "reps", "targetPercentage"]
                }
              },
              "strengthB": {
                "type": "array",
                "items": {
                  "type": "object",
                  "properties": {
                    "exerciseIndex": { "type": "integer" },
                    "sets": { "type": "integer" },
                    "reps": { "type": "integer" },
                    "targetPercentage": { "type": "number" }
                  },
                  "required": ["exerciseIndex", "sets", "reps", "targetPercentage"]
                }
              },
              "accessories": {
                "type": "array",
                "items": {
                  "type": "object",
                  "properties": {
                    "exerciseIndex": { "type": "integer" },
                    "sets": { "type": "integer" },
                    "reps": { "type": "integer" }
                  },
                  "required": ["exerciseIndex", "sets", "reps"]
                }
              }
            },
            "required": ["name", "dayOfWeek", "order", "warmUp", "strengthA", "strengthB", "accessories"]
          }
        }
      },
      "required": ["name", "objective", "justificationSummary", "modelSessions"]
    }
    """;
}

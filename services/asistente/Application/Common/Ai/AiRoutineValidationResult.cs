using Application.DTOs.Ai;

namespace Application.Common.Ai;

/// <summary>
/// Resultado de la validación semántica y de esquema de una propuesta de rutina generada por IA.
/// </summary>
public sealed class AiRoutineValidationResult
{
    public bool IsValid { get; }
    public bool IsSyntaxError { get; }
    public IReadOnlyList<string> Errors { get; }
    public string ErrorMessage => string.Join("; ", Errors);

    public string? ProposedTitle { get; }
    public string? JustificationSummary { get; }
    public IReadOnlyList<SuggestedExerciseDto> Exercises { get; }

    private AiRoutineValidationResult(
        bool isValid,
        bool isSyntaxError,
        IReadOnlyList<string> errors,
        string? proposedTitle = null,
        string? justificationSummary = null,
        IReadOnlyList<SuggestedExerciseDto>? exercises = null)
    {
        IsValid = isValid;
        IsSyntaxError = isSyntaxError;
        Errors = errors;
        ProposedTitle = proposedTitle;
        JustificationSummary = justificationSummary;
        Exercises = exercises ?? [];
    }

    public static AiRoutineValidationResult Success(
        string proposedTitle,
        string justificationSummary,
        IReadOnlyList<SuggestedExerciseDto> exercises)
    {
        return new AiRoutineValidationResult(true, false, [], proposedTitle, justificationSummary, exercises);
    }

    public static AiRoutineValidationResult Failure(params string[] errors)
    {
        return new AiRoutineValidationResult(false, false, errors);
    }

    public static AiRoutineValidationResult Failure(IEnumerable<string> errors)
    {
        return new AiRoutineValidationResult(false, false, errors.ToList());
    }

    public static AiRoutineValidationResult SyntaxFailure(string syntaxErrorMessage)
    {
        return new AiRoutineValidationResult(false, true, [syntaxErrorMessage]);
    }
}

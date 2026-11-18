namespace Application.DTOs.TrainingPlans;

public sealed record TrainingPlanPdfDto(
    byte[] Content,
    string FileName,
    string ContentType = "application/pdf");

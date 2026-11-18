using Application.DTOs.TrainingPlans;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.TrainingPlans.GetPlanPdf;

public sealed class GetTrainingPlanPdfQueryHandler(
    ITrainingPlanRepository planRepository,
    ITrainingPlanPdfGenerator pdfGenerator)
{
    private static readonly char[] DisallowedFileNameChars =
        Path.GetInvalidFileNameChars()
            .Union(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|', ';', ',' })
            .ToArray();

    public async Task<TrainingPlanPdfDto> Handle(
        GetTrainingPlanPdfQuery query,
        CancellationToken cancellationToken = default)
    {
        var plan = await planRepository.GetWithDetailsNoTrackingByIdAsync(query.PlanId, cancellationToken);
        if (plan == null)
            throw new NotFoundException($"No se encontró la planificación con ID '{query.PlanId}'.");

        var content = pdfGenerator.Generate(plan);

        var rawName = string.Join("_", plan.Name.Split(DisallowedFileNameChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
        var safeName = string.IsNullOrWhiteSpace(rawName) ? "rutina" : rawName.Replace(' ', '_');

        return new TrainingPlanPdfDto(content, $"{safeName}.pdf");
    }
}

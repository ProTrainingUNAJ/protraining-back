using Application.Common.PlanDrafts;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;

namespace Application.UseCases.PlanDrafts.UpdatePlanDraft;

public sealed record UpdatePlanDraftCommand(Guid Id, UpdatePlanDraftRequestDto Request);

public sealed class UpdatePlanDraftCommandHandler(
    IPlanDraftRepository planDraftRepository,
    IPfDecisionRepository pfDecisionRepository,
    IExerciseCatalogService catalogService,
    IMatchDayLoadValidator matchDayLoadValidator,
    IStructureCalendarClient calendarClient,
    IUnitOfWork unitOfWork)
{
    public async Task<PlanDraftResponseDto> HandleAsync(
        UpdatePlanDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var draft = await planDraftRepository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("El borrador solicitado no existe.");

        if (draft.Status != PlanDraftStatus.Draft)
            throw new ConflictException(draft.GetNonEditableStatusMessage());

        var previousContent = PlanDraftMapper.DeserializeContent(draft.Content);

        var requestedContent = new PlanDraftContentDto(
            Name: command.Request.Name?.Trim() ?? string.Empty,
            Objective: command.Request.Objective?.Trim() ?? string.Empty,
            JustificationSummary: command.Request.JustificationSummary?.Trim() ?? string.Empty,
            Weeks: command.Request.Weeks ?? []
        );

        // 1. Validaciones KAN-383: esquema, rangos y existencia real en el catálogo de ejercicios
        var exerciseNames = await PlanDraftContentValidator.ValidateAsync(
            requestedContent,
            catalogService,
            cancellationToken);

        // 2. Validador de partidos KAN-408: consultar calendario de la categoría y ajustar topes si corresponde
        IReadOnlyList<DateOnly> matchDates = [];
        if (draft.CategoryId.HasValue && draft.CategoryId.Value != Guid.Empty && requestedContent.Weeks.Count > 0)
        {
            var maxWeek = requestedContent.Weeks.Max(w => w.WeekNumber);
            var endDate = draft.StartDate.AddDays(maxWeek * 7);
            matchDates = await calendarClient.GetMatchDatesAsync(
                draft.CategoryId.Value,
                draft.StartDate,
                endDate,
                cancellationToken);
        }

        var matchValidation = matchDayLoadValidator.ValidateAndAdjust(
            requestedContent,
            draft.StartDate,
            matchDates,
            exerciseNames);

        var finalContent = matchValidation.Content;

        // 3. Memoria del PF KAN-423: registrar sustituciones (Modified) y bajas (Rejected) de ejercicios
        var decisions = PfDecisionDiffExtractor.ExtractDecisions(
            draft.Id,
            draft.AthleteId,
            previousContent,
            finalContent);

        if (decisions.Count > 0)
        {
            await pfDecisionRepository.AddRangeAsync(decisions, cancellationToken);
        }

        // 4. Actualizar contenido del borrador y persistir en la misma transacción
        var serializedContent = PlanDraftMapper.SerializeContent(finalContent);
        draft.UpdateContent(serializedContent);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PlanDraftMapper.ToResponseDto(draft, finalContent, matchValidation.Corrections);
    }
}

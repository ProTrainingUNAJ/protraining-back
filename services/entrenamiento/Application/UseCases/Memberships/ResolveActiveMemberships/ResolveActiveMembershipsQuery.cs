using Application.DTOs.Memberships;
using Application.Interfaces.Services;

namespace Application.UseCases.Memberships.ResolveActiveMemberships;

public sealed record ResolveActiveMembershipsQuery;

// A qué categorías pertenece el atleta logueado (#062). Lo usa la rutina de hoy (#063) para ubicar su planificación.
public sealed class ResolveActiveMembershipsQueryHandler(IStructureClient structureClient)
{
    public Task<IReadOnlyList<AthleteCategoryDto>> HandleAsync(
        ResolveActiveMembershipsQuery query,
        CancellationToken cancellationToken = default) =>
        structureClient.GetMyCategoriesAsync(cancellationToken);
}
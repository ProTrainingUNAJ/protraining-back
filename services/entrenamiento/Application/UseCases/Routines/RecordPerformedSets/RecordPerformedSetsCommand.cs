using Application.DTOs.Routines;

namespace Application.UseCases.Routines.RecordPerformedSets;

public class RecordPerformedSetsCommand
{
    public List<PerformedSetInputDto> Sets { get; }

    public RecordPerformedSetsCommand(List<PerformedSetInputDto> sets)
    {
        Sets = sets ?? [];
    }
}


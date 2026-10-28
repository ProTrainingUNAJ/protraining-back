using Application.DTOs.DailyLoad;
using Domain.Entities;

namespace Application.Mappings;

public static class DailyLoadMappings
{
    public static DailyLoadResponseDto ToResponse(DailyLoad load, string message) =>
        new(
            message,
            load.TotalMinutes,
            DateOnly.FromDateTime(load.Date),
            new ActivityLoadDto { Minutes = load.GymMinutes, Rpe = load.GymRpe },
            new ActivityLoadDto { Minutes = load.SportTrainingMinutes, Rpe = load.SportTrainingRpe },
            new ActivityLoadDto { Minutes = load.Match1Minutes, Rpe = load.Match1Rpe },
            new ActivityLoadDto { Minutes = load.Match2Minutes, Rpe = load.Match2Rpe },
            new ActivityLoadDto { Minutes = load.IndividualWorkMinutes, Rpe = load.IndividualWorkRpe });
}

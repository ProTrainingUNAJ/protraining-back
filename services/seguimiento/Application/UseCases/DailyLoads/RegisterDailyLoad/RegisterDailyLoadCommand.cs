using Application.DTOs.DailyLoad;

namespace Application.UseCases.DailyLoads.RegisterDailyLoad;

public sealed record RegisterDailyLoadCommand(Guid AthleteId, RegisterDailyLoadRequest? Request = null)
{
    public RegisterDailyLoadCommand(
        Guid athleteId, ActivityLoadDto? gym, ActivityLoadDto? sportTraining,
        ActivityLoadDto? match1, ActivityLoadDto? match2, ActivityLoadDto? individualWork)
        : this(athleteId, new RegisterDailyLoadRequest
        {
            Gym = gym, SportTraining = sportTraining, Match1 = match1,
            Match2 = match2, IndividualWork = individualWork
        }) { }
}
using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

// Carga del día del atleta: minutos y RPE de las cinco actividades fijas (issue #147).
public class DailyLoad : BaseEntity
{
    public Guid AthleteId { get; set; }
    public DateTime Date { get; set; }

    public int GymMinutes { get; set; }
    public int? GymRpe { get; set; }

    public int SportTrainingMinutes { get; set; }
    public int? SportTrainingRpe { get; set; }

    public int Match1Minutes { get; set; }
    public int? Match1Rpe { get; set; }

    public int Match2Minutes { get; set; }
    public int? Match2Rpe { get; set; }

    public int IndividualWorkMinutes { get; set; }
    public int? IndividualWorkRpe { get; set; }

    public int TotalMinutes =>
        GymMinutes + SportTrainingMinutes + Match1Minutes + Match2Minutes + IndividualWorkMinutes;

    // Carga del día = Σ (minutos × RPE) por actividad (issue #148).
    public int TotalLoad =>
        GymMinutes * (GymRpe ?? 0) +
        SportTrainingMinutes * (SportTrainingRpe ?? 0) +
        Match1Minutes * (Match1Rpe ?? 0) +
        Match2Minutes * (Match2Rpe ?? 0) +
        IndividualWorkMinutes * (IndividualWorkRpe ?? 0);

    public static DailyLoad Register(
        Guid athleteId,
        DateTime date,
        int gymMinutes,
        int? gymRpe,
        int sportTrainingMinutes,
        int? sportTrainingRpe,
        int match1Minutes,
        int? match1Rpe,
        int match2Minutes,
        int? match2Rpe,
        int individualWorkMinutes,
        int? individualWorkRpe)
    {
        var load = new DailyLoad { AthleteId = athleteId, Date = date };
        load.Update(
            gymMinutes, gymRpe,
            sportTrainingMinutes, sportTrainingRpe,
            match1Minutes, match1Rpe,
            match2Minutes, match2Rpe,
            individualWorkMinutes, individualWorkRpe);
        return load;
    }

    public void Update(
        int gymMinutes,
        int? gymRpe,
        int sportTrainingMinutes,
        int? sportTrainingRpe,
        int match1Minutes,
        int? match1Rpe,
        int match2Minutes,
        int? match2Rpe,
        int individualWorkMinutes,
        int? individualWorkRpe)
    {
        Validate(gymMinutes, gymRpe, "Gimnasio");
        Validate(sportTrainingMinutes, sportTrainingRpe, "Entrenamiento del deporte");
        Validate(match1Minutes, match1Rpe, "Partido 1");
        Validate(match2Minutes, match2Rpe, "Partido 2");
        Validate(individualWorkMinutes, individualWorkRpe, "Entrenamiento individual");

        if (gymMinutes == 0 && sportTrainingMinutes == 0 && match1Minutes == 0
            && match2Minutes == 0 && individualWorkMinutes == 0)
            throw new DomainException("Debe registrar al menos una actividad con minutos mayores a 0.");

        GymMinutes = gymMinutes; GymRpe = gymRpe;
        SportTrainingMinutes = sportTrainingMinutes; SportTrainingRpe = sportTrainingRpe;
        Match1Minutes = match1Minutes; Match1Rpe = match1Rpe;
        Match2Minutes = match2Minutes; Match2Rpe = match2Rpe;
        IndividualWorkMinutes = individualWorkMinutes; IndividualWorkRpe = individualWorkRpe;
    }

    private static void Validate(int minutes, int? rpe, string name)
    {
        if (minutes < 0)
            throw new DomainException($"{name}: los minutos deben ser 0 o más.");
        if (rpe is < 0 or > 10)
            throw new DomainException($"{name}: el RPE debe estar entre 0 y 10.");
        if (minutes > 0 && rpe is null)
            throw new DomainException($"{name}: el RPE es obligatorio si los minutos son mayores a 0.");
    }
}
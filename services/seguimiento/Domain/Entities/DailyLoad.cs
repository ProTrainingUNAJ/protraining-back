using Domain.Common;

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
}

using Domain.Common;

namespace Domain.Entities
{
    public class AthleteRecord : BaseEntity
    {
        public Guid AthleteId { get; set; }
        public Guid ExerciseId { get; set; }
        public decimal Weight { get; set; }
        public DateTime Date { get; set; }
        public bool IsActive { get; set; }
        public RecordSource Source { get; set; }
    }

    public enum RecordSource
    {
        Tested,
        Reported,
        Estimated,
        NoData
    }
}

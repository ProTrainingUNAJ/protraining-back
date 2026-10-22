
namespace Application.DTOs.Memberships
{
    public class AthleteMeDto
    {
        public Guid AthleteId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool Active { get; set; }
        public IReadOnlyList<AthleteCategoryDto> Categories { get; set; } = [];
    }
}

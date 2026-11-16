using Microsoft.AspNetCore.Identity;
namespace Domain.Entities;

public class User : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public Guid? PfId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool HasPhoto { get; set; } = false;
    public DateTime? PhotoUpdatedAt { get; set; }
    public bool HabitsRecommendationsEnabled { get; set; } = true;
    public User()
    {
        Id = Guid.NewGuid();
        SecurityStamp = Guid.NewGuid().ToString();
    }
}

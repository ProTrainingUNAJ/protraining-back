namespace Domain.Common;

// Base for every entity in the service. Id is generated when the entity is created;
// PfId, CreatedAt and DeletedAt are set by AppDbContext on save.
public abstract class BaseEntity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PfId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }
}

namespace Domain.Common;

// Base for every entity in the service. Id is generated when the entity is created;
// PfId, CreatedAt and DeletedAt are set by AppDbContext on save.
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public Guid PfId { get; protected set; }
    public DateTime CreatedAt { get; protected set; }
    public DateTime? DeletedAt { get; protected set; }
}

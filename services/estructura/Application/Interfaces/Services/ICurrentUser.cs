namespace Application.Interfaces.Services;

public interface ICurrentUser
{
    // PF that owns the data of the current request ("pf_id" claim of the token). Null without a valid token.
    Guid? PfId { get; }
}

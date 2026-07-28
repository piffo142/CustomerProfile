namespace SIG.ClientCard.Core.Abstractions;

/// <summary>
/// The salon the current user is operating in. Phase 0 uses a fixed local id;
/// Phase 1 replaces it with the id from the auth session.
/// </summary>
public interface ITenantContext
{
    Guid SalonId { get; }
}

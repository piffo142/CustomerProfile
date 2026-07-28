using System.Text.Json;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Data.Entities;

namespace SIG.ClientCard.Data.Infrastructure;

/// <summary>
/// Adds outbox rows to the same change set as the domain write, so a single
/// SaveChanges commits both atomically.
/// </summary>
public sealed class OutboxWriter(IClock clock)
{
    public void EnqueueUpsert<TPayload>(ClientCardContext db, string entity, Guid entityId, TPayload payload)
        => Enqueue(db, entity, entityId, SyncOperations.Upsert,
            JsonSerializer.Serialize(payload, SyncJson.Options));

    public void EnqueueDelete(ClientCardContext db, string entity, Guid entityId)
        => Enqueue(db, entity, entityId, SyncOperations.Delete,
            JsonSerializer.Serialize(new { id = entityId }, SyncJson.Options));

    private void Enqueue(ClientCardContext db, string entity, Guid entityId, string operation, string payload)
    {
        db.Outbox.Add(new SyncOutboxEntry
        {
            OpId = Guid.CreateVersion7(),
            Entity = entity,
            EntityId = entityId,
            Operation = operation,
            Payload = payload,
            CreatedAt = clock.UtcNow,
        });
    }
}

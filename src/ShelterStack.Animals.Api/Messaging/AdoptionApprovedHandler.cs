using Microsoft.EntityFrameworkCore;
using ShelterStack.Animals.Api.Data;
using ShelterStack.Animals.Api.Tenancy;

namespace ShelterStack.Animals.Api.Messaging;

/// <summary>
/// Applies an approved adoption to the animal: <c>… → Adopted</c>, recorded in the same
/// status-history trail the HTTP status endpoint writes to.
/// <para>
/// This is the consuming half of the project's first service-to-service integration, and it
/// carries the isolation rule across the broker. The tenant comes from the message body and is
/// pushed through the normal <see cref="ITenantContext"/> — so every query runs under the same
/// EF Core global query filters an HTTP request would, and a forged or mistaken
/// <c>TenantId</c> can only ever reach that tenant's own animals. It is deliberately not a
/// trusted-transport tenant claim; see issue #106 for the failure mode that shape produces.
/// </para>
/// </summary>
public sealed class AdoptionApprovedHandler(
    DbContextOptions<AnimalsDbContext> dbOptions,
    EventPublisher publisher,
    ILogger<AdoptionApprovedHandler> logger
)
{
    public async Task HandleAsync(AdoptionApproved message, CancellationToken cancellationToken)
    {
        // Built explicitly rather than injected: there is no HTTP request here for
        // ClaimsTenantContext to resolve from, and the tenant this unit of work runs as is the
        // one named by the message.
        await using var db = new AnimalsDbContext(
            dbOptions,
            new StaticTenantContext(message.TenantId)
        );

        var animal = await db.Animals.FirstOrDefaultAsync(
            a => a.Id == message.AnimalId,
            cancellationToken
        );

        if (animal is null)
        {
            // Either the animal genuinely does not exist, or it belongs to a different tenant
            // and the query filter hid it — indistinguishable here by design, exactly as a
            // cross-tenant id is a 404 rather than a 403 on the HTTP side.
            await RejectAsync(
                message,
                "The animal does not exist in this organisation.",
                cancellationToken
            );
            return;
        }

        if (!AnimalStatusTransitions.IsAllowed(animal.Status, AnimalStatus.Adopted))
        {
            await RejectAsync(
                message,
                $"Cannot move an animal from {animal.Status} to {AnimalStatus.Adopted}.",
                cancellationToken
            );
            return;
        }

        animal.Status = AnimalStatus.Adopted;
        db.AnimalStatusHistory.Add(
            new AnimalStatusHistory
            {
                Id = Guid.NewGuid(),
                TenantId = message.TenantId,
                AnimalId = animal.Id,
                Status = AnimalStatus.Adopted,
                ChangedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Animal {AnimalId} moved to Adopted for approved application {ApplicationId}.",
            animal.Id,
            message.ApplicationId
        );
    }

    private async Task RejectAsync(
        AdoptionApproved message,
        string reason,
        CancellationToken cancellationToken
    )
    {
        logger.LogWarning(
            "Refused the Adopted transition for animal {AnimalId} (application {ApplicationId}): {Reason}",
            message.AnimalId,
            message.ApplicationId,
            reason
        );

        await publisher.PublishAsync(
            new AnimalStatusChangeRejected(
                message.TenantId,
                message.ApplicationId,
                message.AnimalId,
                reason,
                DateTimeOffset.UtcNow
            ),
            ShelterStackEvents.AnimalStatusChangeRejectedRoutingKey,
            cancellationToken
        );
    }
}

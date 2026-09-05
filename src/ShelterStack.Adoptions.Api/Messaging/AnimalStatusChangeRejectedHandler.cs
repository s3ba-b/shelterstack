using Microsoft.EntityFrameworkCore;
using ShelterStack.Adoptions.Api.Data;
using ShelterStack.Adoptions.Api.Tenancy;

namespace ShelterStack.Adoptions.Api.Messaging;

/// <summary>
/// The compensating half of the asynchronous approval. Approving returns 200 as soon as the
/// application is marked <see cref="AdoptionApplicationStatus.Approved"/> and the event is on
/// the wire, so by the time ShelterStack.Animals.Api discovers it cannot move the animal — a
/// medical hold, a stale pre-check, an animal that is not there — nothing is left to fail. This
/// handler is what stops that from being lost: the application drops to
/// <see cref="AdoptionApplicationStatus.NeedsAttention"/> carrying the downstream reason, where
/// staff can see it.
/// <para>
/// As on the publishing side, the tenant comes from the message body and is pushed through the
/// normal <see cref="ITenantContext"/>, so the update runs under the same EF Core global query
/// filters an HTTP request would — a message naming another tenant can only ever reach that
/// tenant's own applications.
/// </para>
/// </summary>
public sealed class AnimalStatusChangeRejectedHandler(
    DbContextOptions<AdoptionsDbContext> dbOptions,
    ILogger<AnimalStatusChangeRejectedHandler> logger
)
{
    public async Task HandleAsync(
        AnimalStatusChangeRejected message,
        CancellationToken cancellationToken
    )
    {
        await using var db = new AdoptionsDbContext(
            dbOptions,
            new StaticTenantContext(message.TenantId)
        );

        var application = await db.AdoptionApplications.FirstOrDefaultAsync(
            a => a.Id == message.ApplicationId,
            cancellationToken
        );

        if (application is null)
        {
            // Either the application does not exist or it belongs to another tenant and the
            // query filter hid it — the same indistinguishable outcome a cross-tenant id gets
            // over HTTP. Nothing to compensate either way, so the message is simply acked.
            logger.LogWarning(
                "Ignoring a rejected status change for unknown application {ApplicationId}.",
                message.ApplicationId
            );
            return;
        }

        application.Status = AdoptionApplicationStatus.NeedsAttention;
        application.StatusReason = message.Reason;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Application {ApplicationId} needs attention: the animal could not be adopted — {Reason}",
            application.Id,
            message.Reason
        );
    }
}

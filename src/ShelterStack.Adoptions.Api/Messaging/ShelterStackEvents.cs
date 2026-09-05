using System.Text.Json;
using RabbitMQ.Client;

namespace ShelterStack.Adoptions.Api.Messaging;

/// <summary>
/// The wire contract this service shares with ShelterStack.Animals.Api over the Aspire
/// "messaging" (RabbitMQ) resource: exchange and routing-key names, the JSON settings the
/// bodies are written with, and the topology both ends declare. Byte-for-byte the same contract
/// as that service's copy — if one side changes, both must.
/// <para>
/// Deliberately duplicated in each service rather than extracted to a shared assembly, matching
/// how every service already keeps its own <c>ITenantContext</c>, <c>TokenAuth</c>, and
/// <c>DemoTenants</c>. The contract that actually binds the two services is the JSON on the
/// wire, not a compile-time reference — a shared assembly would couple their deployments
/// without making the message shape any more enforced than it already is.
/// </para>
/// </summary>
public static class ShelterStackEvents
{
    /// <summary>Durable topic exchange every integration event is published to.</summary>
    public const string Exchange = "shelterstack.events";

    /// <summary>
    /// Where a message a consumer could not handle is parked. Nothing consumes this exchange:
    /// its queues exist so a poison message is visible in the RabbitMQ dashboard instead of
    /// being silently dropped or redelivered forever.
    /// </summary>
    public const string DeadLetterExchange = "shelterstack.events.dead-letter";

    /// <summary>Published by this service when staff approve an application.</summary>
    public const string AdoptionApprovedRoutingKey = "adoption.approved";

    /// <summary>
    /// Published by ShelterStack.Animals.Api when it cannot apply the status change the
    /// approval asked for — the compensating path that keeps an asynchronous approval from
    /// failing silently.
    /// </summary>
    public const string AnimalStatusChangeRejectedRoutingKey = "animal.status-change-rejected";

    /// <summary>Web defaults (camelCase), so event bodies read like the services' HTTP payloads.</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web
    );

    /// <summary>
    /// Declares the exchanges. Idempotent and declared by both the publisher and the consumer,
    /// so neither service depends on the other having started first.
    /// </summary>
    public static async Task DeclareExchangesAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct
        );
        await channel.ExchangeDeclareAsync(
            DeadLetterExchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct
        );
    }

    /// <summary>
    /// Declares a durable consumer queue bound to <paramref name="routingKey"/>, plus its
    /// dead-letter queue. A dead-lettered message keeps its original routing key, so the
    /// dead-letter queue binds on the same one.
    /// </summary>
    public static async Task DeclareQueueAsync(
        IChannel channel,
        string queue,
        string routingKey,
        CancellationToken ct
    )
    {
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange,
            },
            cancellationToken: ct
        );
        await channel.QueueBindAsync(queue, Exchange, routingKey, cancellationToken: ct);

        var deadLetterQueue = $"{queue}.dead-letter";
        await channel.QueueDeclareAsync(
            deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            deadLetterQueue,
            DeadLetterExchange,
            routingKey,
            cancellationToken: ct
        );
    }
}

/// <summary>
/// Staff approved an adoption application; ShelterStack.Animals.Api should move the animal to
/// its <c>Adopted</c> status.
/// <para>
/// <paramref name="TenantId"/> travels in the body rather than being implied by the transport:
/// the consumer resolves its tenant from it and runs under the same EF Core global query
/// filters an HTTP request would, so a message can only ever address the tenant it names — and
/// a message naming tenant B cannot reach tenant A's animal even with a correct animal id.
/// </para>
/// </summary>
public sealed record AdoptionApproved(
    Guid TenantId,
    Guid ApplicationId,
    Guid AnimalId,
    DateTimeOffset ApprovedAtUtc
);

/// <summary>
/// ShelterStack.Animals.Api refused the status change the approval asked for — the animal is
/// not in that tenant, or the move is illegal per its transition table (a <c>MedicalHold</c>
/// animal cannot go straight to <c>Adopted</c>). Because the approval was asynchronous the
/// caller has long since had its 200, so consuming this is what lets the application move to
/// <see cref="Data.AdoptionApplicationStatus.NeedsAttention"/> instead of standing approved for
/// an animal that never moved.
/// </summary>
public sealed record AnimalStatusChangeRejected(
    Guid TenantId,
    Guid ApplicationId,
    Guid AnimalId,
    string Reason,
    DateTimeOffset RejectedAtUtc
);

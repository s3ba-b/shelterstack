using System.Text.Json;
using RabbitMQ.Client;

namespace ShelterStack.Adoptions.Api.Messaging;

/// <summary>
/// Publishes integration events to the <see cref="ShelterStackEvents.Exchange"/> topic exchange.
/// A fresh channel per publish: <c>IChannel</c> is not built for concurrent use, publishing here
/// is rare (one message per approved application), and a short-lived channel keeps that correct
/// without a lock or a channel pool.
/// </summary>
public sealed class EventPublisher(IConnection connection)
{
    public async Task PublishAsync<TEvent>(
        TEvent message,
        string routingKey,
        CancellationToken cancellationToken
    )
        where TEvent : notnull
    {
        await using var channel = await connection.CreateChannelAsync(
            cancellationToken: cancellationToken
        );
        await ShelterStackEvents.DeclareExchangesAsync(channel, cancellationToken);

        var body = JsonSerializer.SerializeToUtf8Bytes(
            message,
            ShelterStackEvents.SerializerOptions
        );

        // Persistent + a durable exchange and queue: an approval must survive a broker restart,
        // otherwise an animal quietly never becomes adopted.
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Persistent = true,
        };

        await channel.BasicPublishAsync(
            ShelterStackEvents.Exchange,
            routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken
        );
    }
}

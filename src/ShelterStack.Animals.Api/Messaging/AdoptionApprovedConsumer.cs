using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ShelterStack.Animals.Api.Messaging;

/// <summary>
/// Subscribes to <see cref="AdoptionApproved"/> and hands each message to
/// <see cref="AdoptionApprovedHandler"/>.
/// <para>
/// Subscription is retried rather than fatal: the service must still start and serve HTTP when
/// the broker is briefly unavailable — or absent entirely, as it is for the API-level tests that
/// boot this host without a "messaging" resource.
/// </para>
/// </summary>
public sealed class AdoptionApprovedConsumer(
    IServiceProvider services,
    ILogger<AdoptionApprovedConsumer> logger
) : BackgroundService
{
    /// <summary>Durable, service-owned queue. Named for its owner so it is obvious in the
    /// RabbitMQ dashboard which service is behind on which events.</summary>
    public const string QueueName = "animals-api.adoption-approved";

    private static readonly TimeSpan SubscribeRetryDelay = TimeSpan.FromSeconds(10);

    private IChannel? _channel;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Deliberately not awaited. Resolving the broker connection retries internally with a
        // backoff that does not observe the stopping token, so awaiting the loop here would make
        // every host shutdown sit through those retries whenever the broker is unreachable —
        // half a minute of dead time on a restart, and on every test that boots this host without
        // a broker. Subscribing owns no state the host needs at startup, and the channel is
        // closed in StopAsync either way.
        _ = Task.Run(() => SubscribeWithRetriesAsync(stoppingToken), stoppingToken);

        return Task.CompletedTask;
    }

    private async Task SubscribeWithRetriesAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SubscribeAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Could not subscribe to '{Queue}'; retrying in {Delay}.",
                    QueueName,
                    SubscribeRetryDelay
                );
            }

            try
            {
                await Task.Delay(SubscribeRetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        // Resolved here rather than through the constructor so a missing or unreachable broker
        // surfaces as a retryable subscribe failure instead of a host that refuses to start.
        var connection = services.GetRequiredService<IConnection>();

        _channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await ShelterStackEvents.DeclareExchangesAsync(_channel, cancellationToken);
        await ShelterStackEvents.DeclareQueueAsync(
            _channel,
            QueueName,
            ShelterStackEvents.AdoptionApprovedRoutingKey,
            cancellationToken
        );

        // One unacknowledged message at a time: handling an event mutates an animal and may
        // publish a compensating event, so back-pressure matters more here than throughput.
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken: cancellationToken
        );

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await _channel.BasicConsumeAsync(
            QueueName,
            autoAck: false,
            consumer,
            cancellationToken: cancellationToken
        );

        logger.LogInformation(
            "Consuming '{RoutingKey}' events from '{Queue}'.",
            ShelterStackEvents.AdoptionApprovedRoutingKey,
            QueueName
        );
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        var channel =
            _channel ?? throw new InvalidOperationException("Received a delivery with no channel.");

        try
        {
            var message =
                JsonSerializer.Deserialize<AdoptionApproved>(
                    args.Body.Span,
                    ShelterStackEvents.SerializerOptions
                ) ?? throw new InvalidOperationException("Event body deserialized to null.");

            // A scope per message, so the handler gets the same scoped services (and the same
            // unpooled DbContext options) a request-scoped handler would.
            using var scope = services.CreateScope();
            await scope
                .ServiceProvider.GetRequiredService<AdoptionApprovedHandler>()
                .HandleAsync(message, args.CancellationToken);

            await channel.BasicAckAsync(
                args.DeliveryTag,
                multiple: false,
                cancellationToken: args.CancellationToken
            );
        }
        catch (Exception ex)
        {
            // Dead-letter rather than requeue: a message this consumer cannot process will fail
            // the same way on redelivery, and an endless redelivery loop would bury the failure
            // instead of surfacing it. The queue's x-dead-letter-exchange parks it where it can
            // be inspected.
            logger.LogError(
                ex,
                "Failed to handle an '{RoutingKey}' event; dead-lettering it.",
                ShelterStackEvents.AdoptionApprovedRoutingKey
            );

            await channel.BasicNackAsync(
                args.DeliveryTag,
                multiple: false,
                requeue: false,
                cancellationToken: args.CancellationToken
            );
        }
    }
}

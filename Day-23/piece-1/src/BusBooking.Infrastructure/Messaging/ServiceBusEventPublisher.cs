using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BusBooking.Application.Common;
using BusBooking.Domain.Common;
using Microsoft.Extensions.Logging;

namespace BusBooking.Infrastructure.Messaging;

internal sealed class ServiceBusEventPublisher(
    ServiceBusClient client,
    ILogger<ServiceBusEventPublisher> logger) : IEventPublisher
{
    // Topic name derived from event type: BookingConfirmedEvent → "booking-confirmed"
    private static string TopicFor(Type eventType) =>
        string.Concat(eventType.Name
            .Replace("Event", "")
            .Select((c, i) => i > 0 && char.IsUpper(c) ? $"-{c}" : $"{c}"))
            .ToLowerInvariant();

    public async Task PublishAsync<T>(T domainEvent, CancellationToken ct = default) where T : IDomainEvent
    {
        var topic = TopicFor(typeof(T));
        await using var sender = client.CreateSender(topic);

        var body = JsonSerializer.Serialize(domainEvent, domainEvent.GetType());
        var message = new ServiceBusMessage(body)
        {
            ContentType = "application/json",
            Subject = typeof(T).Name,
        };

        await sender.SendMessageAsync(message, ct);
        logger.LogInformation("Published {Event} to topic {Topic}", typeof(T).Name, topic);
    }
}

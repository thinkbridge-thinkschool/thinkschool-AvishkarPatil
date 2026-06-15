using BusBooking.Application.Common;
using BusBooking.Domain.Common;
using Microsoft.Extensions.Logging;

namespace BusBooking.Api;

internal sealed class NoOpEventPublisher(ILogger<NoOpEventPublisher> logger) : IEventPublisher
{
    public Task PublishAsync<T>(T domainEvent, CancellationToken ct = default) where T : IDomainEvent
    {
        logger.LogInformation("[NoOp] Would publish {Event}: {Payload}", typeof(T).Name, domainEvent);
        return Task.CompletedTask;
    }
}

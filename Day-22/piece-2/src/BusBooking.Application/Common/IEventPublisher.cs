using BusBooking.Domain.Common;

namespace BusBooking.Application.Common;

public interface IEventPublisher
{
    Task PublishAsync<T>(T domainEvent, CancellationToken ct = default) where T : IDomainEvent;
}

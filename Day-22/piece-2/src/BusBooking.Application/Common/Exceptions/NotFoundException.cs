namespace BusBooking.Application.Common.Exceptions;

public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} with key '{key}' was not found.");

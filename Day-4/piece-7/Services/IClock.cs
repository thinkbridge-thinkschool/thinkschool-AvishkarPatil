namespace QuotesApi.Services;

public interface IClock
{
    DateTime UtcNow { get; }
}

using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Configuration;

public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    public string TopicName              { get; set; } = "quotes-topic";
    public string AnalyticsSubscription  { get; set; } = "analytics-subscription";
    public string NotificationsSubscription { get; set; } = "notifications-subscription";
}

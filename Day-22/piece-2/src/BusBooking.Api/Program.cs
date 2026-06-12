using BusBooking.Api;
using BusBooking.Api.Booking;
using BusBooking.Api.Scheduling;
using BusBooking.Application.Common;
using BusBooking.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddInfrastructure(builder.Configuration);

// In Development, skip real Service Bus — log events to console instead.
if (builder.Environment.IsDevelopment())
    builder.Services.AddScoped<IEventPublisher, NoOpEventPublisher>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

app.MapScheduleEndpoints();
app.MapBookingEndpoints();

// Seed on startup in Development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<BusBooking.Infrastructure.Persistence.DatabaseSeeder>();
    await seeder.SeedAsync();
}

app.Run();

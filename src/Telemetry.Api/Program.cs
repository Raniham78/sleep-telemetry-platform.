using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IConnection>(_ =>
{
    var factory = new ConnectionFactory
    {
        HostName = builder.Configuration["RabbitMq:Host"] ?? "localhost",
        UserName = builder.Configuration["RabbitMq:User"] ?? "guest",
        Password = builder.Configuration["RabbitMq:Password"] ?? "guest"
    };

    return factory.CreateConnection();
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "telemetry-api",
    utc = DateTimeOffset.UtcNow
}));

app.MapPost("/api/telemetry", (TelemetryRequest request, IConnection connection) =>
{
    var validationError = request.Validate();

    if (validationError is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = [validationError]
        });
    }

    var message = new TelemetryEnvelope(
        Guid.NewGuid(),
        request.DeviceId.Trim(),
        request.RecordedAt,
        request.TemperatureCelsius,
        request.HeartRateBpm,
        request.Movement);

    using var channel = connection.CreateModel();

    channel.QueueDeclare(
        queue: "telemetry.ingest",
        durable: true,
        exclusive: false,
        autoDelete: false,
        arguments: null);

    // Web defaults serialize property names as camelCase to match the Python consumer.
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, jsonOptions));

    var properties = channel.CreateBasicProperties();
    properties.Persistent = true;
    properties.ContentType = "application/json";

    channel.BasicPublish(
        exchange: "",
        routingKey: "telemetry.ingest",
        basicProperties: properties,
        body: body);

    return Results.Accepted(
        $"/api/telemetry/{message.DeviceId}",
        new
        {
            message.EventId,
            message.DeviceId,
            status = "queued"
        });
});

app.Run();

public sealed record TelemetryRequest(
    string DeviceId,
    DateTimeOffset RecordedAt,
    double TemperatureCelsius,
    int HeartRateBpm,
    double Movement)
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(DeviceId))
            return "DeviceId is required.";

        if (TemperatureCelsius is < 0 or > 50)
            return "TemperatureCelsius must be between 0 and 50.";

        if (HeartRateBpm is < 25 or > 240)
            return "HeartRateBpm must be between 25 and 240.";

        if (Movement is < 0 or > 10)
            return "Movement must be between 0 and 10.";

        return null;
    }
}

public sealed record TelemetryEnvelope(
    Guid EventId,
    string DeviceId,
    DateTimeOffset RecordedAt,
    double TemperatureCelsius,
    int HeartRateBpm,
    double Movement);

using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Telemetry")
    ?? throw new InvalidOperationException("ConnectionStrings:Telemetry is not configured.");

builder.Services.AddDbContext<TelemetryDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IConnection>(_ =>
{
    var rabbit = builder.Configuration.GetSection("RabbitMq");
    var factory = new ConnectionFactory
    {
        HostName = rabbit["Host"] ?? "localhost",
        UserName = rabbit["User"] ?? "telemetry",
        Password = rabbit["Password"] ?? "telemetry_dev_password",
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
    };

    return factory.CreateConnection();
});
builder.Services.AddSingleton<IMessagePublisher, RabbitMqTelemetryPublisher>();

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async Task<IResult> (
    TelemetryDbContext db,
    CancellationToken cancellationToken) =>
{
    var databaseAvailable = await db.Database.CanConnectAsync(cancellationToken);
    if (!databaseAvailable)
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    return Results.Ok(new { status = "ready", database = "available" });
});

var telemetry = app.MapGroup("/api/v1/telemetry");

telemetry.MapPost("/", async Task<IResult> (
    IngestTelemetryRequest request,
    IMessagePublisher publisher,
    CancellationToken cancellationToken) =>
{
    var errors = RequestValidation.Validate(request);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var message = new TelemetryEnvelope(
        Guid.NewGuid(),
        request.DeviceId.Trim(),
        request.RecordedAt.ToUniversalTime(),
        request.TemperatureCelsius,
        request.HeartRateBpm,
        request.Movement);

    await publisher.PublishAsync(message, cancellationToken);

    return Results.Accepted($"/api/v1/telemetry/{message.DeviceId}", new
    {
        message.EventId,
        message.DeviceId,
        status = "queued"
    });
});

telemetry.MapGet("/{deviceId}", async Task<IResult> (
    string deviceId,
    int? limit,
    TelemetryDbContext db,
    CancellationToken cancellationToken) =>
{
    var take = Math.Clamp(limit ?? 100, 1, 500);
    var events = await db.TelemetryEvents
        .AsNoTracking()
        .Where(item => item.DeviceId == deviceId)
        .OrderByDescending(item => item.RecordedAt)
        .Take(take)
        .Select(item => new
        {
            item.EventId,
            item.DeviceId,
            item.RecordedAt,
            item.TemperatureCelsius,
            item.HeartRateBpm,
            item.Movement,
            item.ReceivedAt
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(events);
});

app.Run();

public partial class Program { }

public sealed class IngestTelemetryRequest
{
    [Required]
    [StringLength(80, MinimumLength = 1)]
    public string DeviceId { get; init; } = string.Empty;

    public DateTimeOffset RecordedAt { get; init; }

    [Range(0, 50)]
    public double TemperatureCelsius { get; init; }

    [Range(25, 240)]
    public int HeartRateBpm { get; init; }

    [Range(0, 10)]
    public double Movement { get; init; }
}

public sealed record TelemetryEnvelope(
    Guid EventId,
    string DeviceId,
    DateTimeOffset RecordedAt,
    double TemperatureCelsius,
    int HeartRateBpm,
    double Movement);

public sealed class TelemetryEventEntity
{
    public Guid EventId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; set; }
    public double TemperatureCelsius { get; set; }
    public int HeartRateBpm { get; set; }
    public double Movement { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class TelemetryDbContext(DbContextOptions<TelemetryDbContext> options)
    : DbContext(options)
{
    public DbSet<TelemetryEventEntity> TelemetryEvents => Set<TelemetryEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TelemetryEventEntity>();
        entity.ToTable("telemetry_events");
        entity.HasKey(x => x.EventId);
        entity.Property(x => x.EventId).HasColumnName("event_id");
        entity.Property(x => x.DeviceId).HasColumnName("device_id").HasMaxLength(80).IsRequired();
        entity.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        entity.Property(x => x.TemperatureCelsius).HasColumnName("temperature_celsius");
        entity.Property(x => x.HeartRateBpm).HasColumnName("heart_rate_bpm");
        entity.Property(x => x.Movement).HasColumnName("movement");
        entity.Property(x => x.ReceivedAt).HasColumnName("received_at");
        entity.HasIndex(x => new { x.DeviceId, x.RecordedAt })
            .HasDatabaseName("ix_telemetry_events_device_recorded");
    }
}

public interface IMessagePublisher
{
    Task PublishAsync(TelemetryEnvelope message, CancellationToken cancellationToken);
}

public sealed class RabbitMqTelemetryPublisher(IConnection connection) : IMessagePublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task PublishAsync(TelemetryEnvelope message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var channel = connection.CreateModel();
        channel.QueueDeclare(
            queue: "telemetry.ingest",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonOptions));
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.MessageId = message.EventId.ToString();

        channel.BasicPublish(
            exchange: "",
            routingKey: "telemetry.ingest",
            mandatory: false,
            basicProperties: properties,
            body: body);

        return Task.CompletedTask;
    }
}

public static class RequestValidation
{
    public static Dictionary<string, string[]> Validate(object request)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(request);
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            var members = result.MemberNames.ToArray();
            if (members.Length == 0)
                members = ["request"];

            foreach (var member in members)
            {
                if (!errors.TryGetValue(member, out var messages))
                {
                    messages = [];
                    errors[member] = messages;
                }

                messages.Add(result.ErrorMessage ?? "Invalid value.");
            }
        }

        return errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }
}

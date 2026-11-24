using Xunit;

public sealed class RequestValidationTests
{
    [Fact]
    public void Valid_request_returns_no_errors()
    {
        var request = new IngestTelemetryRequest
        {
            DeviceId = "pod-001",
            RecordedAt = DateTimeOffset.Parse("2026-11-24T21:30:00Z"),
            TemperatureCelsius = 29.5,
            HeartRateBpm = 62,
            Movement = 0.12
        };

        Assert.Empty(RequestValidation.Validate(request));
    }

    [Fact]
    public void Empty_device_id_is_rejected()
    {
        var request = new IngestTelemetryRequest
        {
            DeviceId = " ",
            RecordedAt = DateTimeOffset.UtcNow,
            TemperatureCelsius = 29,
            HeartRateBpm = 60,
            Movement = 0.1
        };

        var errors = RequestValidation.Validate(request);

        Assert.Contains(nameof(IngestTelemetryRequest.DeviceId), errors.Keys);
    }

    [Theory]
    [InlineData(-1, 60, 0.1)]
    [InlineData(29, 10, 0.1)]
    [InlineData(29, 60, 11)]
    public void Out_of_range_measurements_are_rejected(
        double temperature,
        int heartRate,
        double movement)
    {
        var request = new IngestTelemetryRequest
        {
            DeviceId = "pod-001",
            RecordedAt = DateTimeOffset.UtcNow,
            TemperatureCelsius = temperature,
            HeartRateBpm = heartRate,
            Movement = movement
        };

        Assert.NotEmpty(RequestValidation.Validate(request));
    }
}

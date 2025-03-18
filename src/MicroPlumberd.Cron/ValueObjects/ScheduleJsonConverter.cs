using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Cron;

public class ScheduleJsonConverter : JsonConverter<Schedule>
{
    public override Schedule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Parse the JSON into a JsonDocument to inspect the "type" property
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // Check for the "type" discriminator property
        if (!root.TryGetProperty("type", out var typeProp))
            throw new JsonException("Missing 'type' property in schedule JSON.");

        string? typeStr = typeProp.GetString();
        if (string.IsNullOrEmpty(typeStr))
            throw new JsonException("The 'type' property cannot be null or empty.");

        // Deserialize to the appropriate derived type based on "type"
        return typeStr switch
        {
            "Interval" => JsonSerializer.Deserialize<IntervalSchedule>(root.GetRawText(), options)
                          ?? throw new JsonException("Failed to deserialize IntervalSchedule."),
            "Daily" => JsonSerializer.Deserialize<DailySchedule>(root.GetRawText(), options)
                       ?? throw new JsonException("Failed to deserialize DailySchedule."),
            "Weekly" => JsonSerializer.Deserialize<WeeklySchedule>(root.GetRawText(), options)
                        ?? throw new JsonException("Failed to deserialize WeeklySchedule."),
            _ => throw new JsonException($"Unknown schedule type: {typeStr}")
        };
    }

    public override void Write(Utf8JsonWriter writer, Schedule value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Write the "type" discriminator based on the concrete type
        string typeStr = value switch
        {
            IntervalSchedule _ => "Interval",
            DailySchedule _ => "Daily",
            WeeklySchedule _ => "Weekly",
            _ => throw new JsonException($"Unknown schedule type: {value.GetType().Name}")
        };
        writer.WriteString("type", typeStr);

        // Write common properties from the base Schedule class
        writer.WriteString("StartTime", value.StartTime?.ToString("O")); // ISO 8601 format
        writer.WriteString("EndTime", value.EndTime?.ToString("O"));

        // Write type-specific properties
        switch (value)
        {
            case IntervalSchedule interval:
                writer.WriteString("Interval", interval.Interval.ToString());
                break;

            case DailySchedule daily:
                writer.WritePropertyName("Items");
                JsonSerializer.Serialize(writer, daily.Items, options);
                break;

            case WeeklySchedule weekly:
                writer.WritePropertyName("Items");
                JsonSerializer.Serialize(writer, weekly.Item, options); // Note: 'Item' property
                break;
        }

        writer.WriteEndObject();
    }
}
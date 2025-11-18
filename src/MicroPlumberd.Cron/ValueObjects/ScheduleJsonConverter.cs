using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// JSON converter for polymorphic schedule types using a type discriminator.
/// </summary>
/// <typeparam name="T">The schedule type to convert.</typeparam>
public class ScheduleJsonConverter<T> : JsonConverter<T> where T:Schedule
{
    /// <inheritdoc/>
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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

        // Read base properties
        DateTime? startTime = root.TryGetProperty("StartTime", out var startProp) && startProp.ValueKind != JsonValueKind.Null
            ? startProp.GetDateTime()
            : null;

        DateTime? endTime = root.TryGetProperty("EndTime", out var endProp) && endProp.ValueKind != JsonValueKind.Null
            ? endProp.GetDateTime()
            : null;

        // Create instance based on type
        Schedule? result = typeStr switch
        {
            "Interval" => CreateIntervalSchedule(root, startTime, endTime),
            "Daily" => CreateDailySchedule(root, startTime, endTime),
            "Weekly" => CreateWeeklySchedule(root, startTime, endTime),
            "Empty" => new EmptySchedule(),
            _ => throw new JsonException($"Unknown schedule type: {typeStr}")
        };

        return result as T ?? throw new JsonException($"Failed to deserialize {typeStr} schedule.");
    }

    private static IntervalSchedule CreateIntervalSchedule(JsonElement root, DateTime? startTime, DateTime? endTime)
    {
        if (!root.TryGetProperty("Interval", out var intervalProp))
            throw new JsonException("Missing 'Interval' property in IntervalSchedule JSON.");

        var interval = TimeSpan.Parse(intervalProp.GetString() ?? throw new JsonException("Interval cannot be null."));

        return new IntervalSchedule
        {
            StartTime = startTime,
            EndTime = endTime,
            Interval = interval
        };
    }

    private static DailySchedule CreateDailySchedule(JsonElement root, DateTime? startTime, DateTime? endTime)
    {
        if (!root.TryGetProperty("Items", out var itemsProp) || itemsProp.ValueKind != JsonValueKind.Array)
            throw new JsonException("Missing or invalid 'Items' property in DailySchedule JSON.");

        var items = new List<TimeOnly>();
        foreach (var item in itemsProp.EnumerateArray())
        {
            items.Add(TimeOnly.Parse(item.GetString() ?? throw new JsonException("Item cannot be null.")));
        }

        return new DailySchedule
        {
            StartTime = startTime,
            EndTime = endTime,
            Items = items.ToArray()
        };
    }

    private static WeeklySchedule CreateWeeklySchedule(JsonElement root, DateTime? startTime, DateTime? endTime)
    {
        if (!root.TryGetProperty("Items", out var itemsProp) || itemsProp.ValueKind != JsonValueKind.Array)
            throw new JsonException("Missing or invalid 'Items' property in WeeklySchedule JSON.");

        var items = new List<WeeklyScheduleItem>();
        foreach (var item in itemsProp.EnumerateArray())
        {
            if (!item.TryGetProperty("Day", out var dayProp) || !item.TryGetProperty("Time", out var timeProp))
                throw new JsonException("WeeklyScheduleItem missing 'Day' or 'Time' property.");

            var day = (DayOfWeek)dayProp.GetInt32();
            var time = TimeOnly.Parse(timeProp.GetString() ?? throw new JsonException("Time cannot be null."));

            items.Add(new WeeklyScheduleItem(day, time));
        }

        return new WeeklySchedule
        {
            StartTime = startTime,
            EndTime = endTime,
            Items = items.ToArray()
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Write the "type" discriminator based on the concrete type
        string typeStr = value switch
        {
            IntervalSchedule _ => "Interval",
            DailySchedule _ => "Daily",
            WeeklySchedule _ => "Weekly",
            EmptySchedule _ => "Empty",
            _ => throw new JsonException($"Unknown schedule type: {value.GetType().Name}")
        };
        writer.WriteString("type", typeStr);

        // Write common base properties
        if (value.StartTime.HasValue)
            writer.WriteString("StartTime", value.StartTime.Value);
        else
            writer.WriteNull("StartTime");

        if (value.EndTime.HasValue)
            writer.WriteString("EndTime", value.EndTime.Value);
        else
            writer.WriteNull("EndTime");

        // Write type-specific properties
        switch (value)
        {
            case IntervalSchedule interval:
                writer.WriteString("Interval", interval.Interval.ToString());
                break;

            case DailySchedule daily:
                writer.WritePropertyName("Items");
                writer.WriteStartArray();
                foreach (var item in daily.Items)
                {
                    writer.WriteStringValue(item.ToString("O"));
                }
                writer.WriteEndArray();
                break;

            case WeeklySchedule weekly:
                writer.WritePropertyName("Items");
                writer.WriteStartArray();
                foreach (var item in weekly.Items)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("Day", (int)item.Day);
                    writer.WriteString("Time", item.Time.ToString("O"));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                break;

            case EmptySchedule:
                // No additional properties
                break;
        }

        writer.WriteEndObject();
    }
}
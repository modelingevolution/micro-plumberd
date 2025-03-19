using System.Text.Json;
using System.Text.Json.Serialization;

using MicroPlumberd.Services.Cron;

namespace MicroPlumberd.Tests.Unit
{
    public class ScheduleSerializationTests
    {
        
        [Fact]
        public void IntervalSchedule_RoundTrip()
        {
            // Arrange: Create an IntervalSchedule with null StartTime
            var original = new IntervalSchedule
            {
                StartTime = null,
                EndTime = new DateTime(2023, 10, 26, 10, 0, 0, DateTimeKind.Utc),
                Interval = TimeSpan.FromHours(1)
            };

            

            // Act: Serialize to JSON and deserialize back
            string json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<Schedule>(json);

            // Assert: Verify type and property values
            Assert.IsType<IntervalSchedule>(deserialized);
            var intervalSchedule = (IntervalSchedule)deserialized;
            Assert.Null(intervalSchedule.StartTime);
            Assert.Equal(original.EndTime, intervalSchedule.EndTime);
            Assert.Equal(original.Interval, intervalSchedule.Interval);
        }

        [Fact]
        public void DailySchedule_RoundTrip()
        {
            // Arrange: Create a DailySchedule with null EndTime and multiple Items
            var original = new DailySchedule
            {
                StartTime = new DateTime(2023, 10, 25, 0, 0, 0, DateTimeKind.Utc),
                EndTime = null,
                Items = new TimeOnly[]
                {
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0),
                    new TimeOnly(15, 0)
                }
            };

            

            // Act: Serialize to JSON and deserialize back
            string json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<Schedule>(json);

            // Assert: Verify type and property values
            Assert.IsType<DailySchedule>(deserialized);
            var dailySchedule = (DailySchedule)deserialized;
            Assert.Equal(original.StartTime, dailySchedule.StartTime);
            Assert.Null(dailySchedule.EndTime);
            Assert.Equal(original.Items, dailySchedule.Items); // Compares array elements
        }

        [Fact]
        public void WeeklySchedule_RoundTrip()
        {
            // Arrange: Create a WeeklySchedule with multiple Item entries
            var original = new WeeklySchedule
            {
                StartTime = new DateTime(2023, 10, 25, 0, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2023, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                Items = new WeeklyScheduleItem[]
                {
                    new WeeklyScheduleItem(DayOfWeek.Monday, new TimeOnly(9, 0)),
                    new WeeklyScheduleItem(DayOfWeek.Wednesday, new TimeOnly(12, 0))
                }
            };

            

            // Act: Serialize to JSON and deserialize back
            string json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<Schedule>(json);

            // Assert: Verify type and property values
            Assert.IsType<WeeklySchedule>(deserialized);
            var weeklySchedule = (WeeklySchedule)deserialized;
            Assert.Equal(original.StartTime, weeklySchedule.StartTime);
            Assert.Equal(original.EndTime, weeklySchedule.EndTime);
            Assert.Equal(original.Items, weeklySchedule.Items); // Compares array of records
        }
    }
}
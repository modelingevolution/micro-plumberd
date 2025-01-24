using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Reflection;
using System.Text.Json.Serialization;
using MicroPlumberd.Services;

namespace MicroPlumberd.Examples.Cinema.Scheduler
{
    [CommandHandler]
    public partial class ScheduleCommandHandler(IPlumber plumber) 
    {
        [ThrowsFaultException<ScreeningTimeCannotBeInPast>]
        public async Task Handle(Guid id, PatchScreening cmd)
        {
            if (cmd.Date.IsDefined && cmd.Time.IsDefined)
                if (cmd.Date.Value.ToDateTime(cmd.Time.Value) < DateTime.Now)
                    throw new FaultException<ScreeningTimeCannotBeInPast>(new ScreeningTimeCannotBeInPast());


            ScreeningStateDefined state = await plumber.GetState<ScreeningStateDefined>(id) ?? throw new Exception();
            
            if (cmd.SeatConfiguration.IsDefined)
            {
                SeatRoomConfiguration conf = cmd.SeatConfiguration;
                state.SeatsInRows = new Space[conf.RowCount, conf.SeatCount];
                foreach (var i in conf.EmptySpaces)
                    state.SeatsInRows[i.Row, i.Seat] = Space.Open;
            }

            if (cmd.Movie.IsDefined)
                state.Movie = cmd.Movie;

            if (cmd.Room.IsDefined)
                state.Room = cmd.Room;

            if (cmd.Time.IsDefined)
                state.When = state.When.Date.Add(cmd.Time.Value.ToTimeSpan());

            if (cmd.Date.IsDefined)
                state.When = cmd.Date.Value.ToDateTime(TimeOnly.MinValue).Add(state.When.TimeOfDay);

            await plumber.AppendState(state);
        }

        public async Task Handle(Guid id, CreateScreening cmd)
        {
            var state = Map(cmd);
            
            await plumber.AppendState(state, id);
        }
        private static ScreeningStateDefined Map(CreateScreening createScreening)
        {
            var seatConfiguration = createScreening.SeatConfiguration;
            var seatsInRows = new Space[seatConfiguration.RowCount, seatConfiguration.SeatCount];
            // Mark empty spaces
            foreach (var emptySpace in seatConfiguration.EmptySpaces)
            {
                seatsInRows[emptySpace.Row, emptySpace.Seat] = Space.Empty;
            }
            return new ScreeningStateDefined
            {
                SeatsInRows = seatsInRows,
                Movie = createScreening.Movie,
                Room = createScreening.Room,
                When = new DateTime(createScreening.Date.Year, createScreening.Date.Month, createScreening.Date.Day, createScreening.Time.Hour, createScreening.Time.Minute, createScreening.Time.Second),
                Version = 1, 
                Id = Guid.NewGuid() 
            };
        }
    }

    
    public class ScreeningTimeCannotBeInPast
    {

    }

    public readonly record struct SeatLocation(ushort Row, ushort Seat);
    
   

    public record PatchScreening
    {
        public Option<SeatRoomConfiguration> SeatConfiguration { get; set; }
        public Option<string> Movie { get; set; }
        public Option<string> Room { get; set; }
        public Option<TimeOnly> Time { get; set; }
        public Option<DateOnly> Date { get; set; }
    }
    public record SeatRoomConfiguration
    {
        public int SeatCount { get; init; } = 0;
        public int RowCount { get; init; } = 0;
        public SeatLocation[] EmptySpaces { get; init; } = Array.Empty<SeatLocation>();
    }
    public record CreateScreening
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public SeatRoomConfiguration SeatConfiguration { get; set; }
        public string Movie { get; set; }
        public string Room { get; set; }
        public TimeOnly Time { get; set; }
        public DateOnly Date { get; set; }

    }

    public enum Space
    {
        Open,Used,Empty
    }
    [OutputStream("Screening")]
    public record ScreeningStateDefined 
    {
        [JsonConverter(typeof(SpaceArrayJsonConverter))]
        public Space[,] SeatsInRows { get; set; }
        public string Movie { get; set; }
        public string Room { get; set; }
        
        public DateTime When { get; set; }
        public long Version { get; set; }
        public Guid Id { get; set; }
    }
}

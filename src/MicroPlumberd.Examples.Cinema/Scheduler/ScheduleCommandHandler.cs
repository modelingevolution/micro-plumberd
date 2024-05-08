using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicroPlumberd.Services;

namespace MicroPlumberd.Examples.Cinema.Scheduler
{
    [CommandHandler]
    public partial class ScheduleCommandHandler(IPlumber plumber)
    {
        [ThrowsFaultException<ScreeningTimeCannotBeInPast>]
        public async Task Handle(Guid id, PathScreening cmd)
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
                    state.SeatsInRows[i.Row, i.Seat] = Space.Empty;
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
    }

    
    public class ScreeningTimeCannotBeInPast
    {

    }

    public readonly record struct SeatLocation(ushort Row, ushort Seat);
    
    public record SeatRoomConfiguration
    {
        public int SeatCount { get; init; }
        public int RowCount { get; init; }
        public SeatLocation[] EmptySpaces { get; init; }
    }

    public record PathScreening
    {
        public Option<SeatRoomConfiguration> SeatConfiguration { get; set; }
        public Option<string> Movie { get; set; }
        public Option<string> Room { get; set; }
        public Option<TimeOnly> Time { get; set; }
        public Option<DateOnly> Date { get; set; }
    }
    public record CreateScreening
    {
        public SeatRoomConfiguration SeatConfiguration { get; set; }
        public string Movie { get; set; }
        public string Room { get; set; }
        public TimeOnly Time { get; set; }
        public DateOnly Date { get; set; }

    }

    public enum Space
    {
        Seat, Empty
    }
    [OutputStream("Screening")]
    public record ScreeningStateDefined 
    {
        public Space[,] SeatsInRows { get; set; }
        public string Movie { get; set; }
        public string Room { get; set; }
        
        public DateTime When { get; set; }
        public long Version { get; set; }
        public Guid Id { get; set; }
    }
}

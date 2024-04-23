using System.ComponentModel.DataAnnotations;
using System.Drawing;
using MicroPlumberd.Services;

namespace MicroPlumberd.Examples.Cinema.Scheduler
{
    [CommandHandler]
    public partial class ScheduleCommandHandler(IPlumber plumber)
    {
        [ThrowsFaultException<ScreeningTimeCannotBeInPast>]
        public async Task Handle(Guid id, DefineScreening cmd)
        {
            if (cmd.Date.IsDefined && cmd.Time.IsDefined)
            if (cmd.Date.Value.ToDateTime(cmd.Time.Value) < DateTime.Now)
                throw new FaultException<ScreeningTimeCannotBeInPast>(new ScreeningTimeCannotBeInPast());

           
            ScreeningStateDefined state = await plumber.GetState<ScreeningStateDefined>(id);
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

            //await plumber.AppendState(state);
        }
    }

    
    public class ScreeningTimeCannotBeInPast
    {

    }

    public readonly record struct SeatLocation(ushort Row, ushort Seat);

    public readonly record struct Property<T>
    {
        public T Value { get; init; }
        public static implicit operator T(Property<T> value) => value.Value;
        public static implicit operator Property<T>(T value) => new Property<T>() { Value = value, IsDefined = true};

        public bool IsDefined { get; init; }
    }
    public record SeatRoomConfiguration
    {
        public int SeatCount { get; init; }
        public int RowCount { get; init; }
        public SeatLocation[] EmptySpaces { get; init; }
    }
    public record DefineScreening
    {
       public Property<SeatRoomConfiguration> SeatConfiguration { get; init; }
        public Property<string> Movie { get; init; }
        public Property<string> Room { get; init; }
        public Property<TimeOnly> Time { get; init; }
        public Property<DateOnly> Date { get; init; }

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

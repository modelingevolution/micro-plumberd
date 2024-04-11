using EventStore.Client;

namespace MicroPlumberd;

public readonly struct FromRelativeStreamPosition
{
    private readonly FromStream _start;
    private readonly long _delta;
    public FromStream StartPosition => _start;
    public Direction Direction => _delta >= 0 ? Direction.Forwards : Direction.Backwards;
    public ulong Count => (ulong)(_delta >= 0 ? _delta : -_delta);
    public FromRelativeStreamPosition(FromStream start, long delta)
    {
        this._start = start;
        this._delta = delta;
    }

    public static implicit operator FromRelativeStreamPosition(FromStream st) => new FromRelativeStreamPosition(st, 0);
    public static FromRelativeStreamPosition Start => new FromRelativeStreamPosition(FromStream.Start, 0);
    public static FromRelativeStreamPosition End => new FromRelativeStreamPosition(FromStream.End, 0);
    public static FromRelativeStreamPosition operator ++(FromRelativeStreamPosition p) => new(p._start, p._delta + 1);
    public static FromRelativeStreamPosition operator --(FromRelativeStreamPosition p) => new(p._start, p._delta - 1);
    public static FromRelativeStreamPosition operator +(FromRelativeStreamPosition p, long d) =>
        new FromRelativeStreamPosition(p._start, p._delta + d);
    public static FromRelativeStreamPosition operator -(FromRelativeStreamPosition p, long d) =>
        new FromRelativeStreamPosition(p._start, p._delta - d);
}
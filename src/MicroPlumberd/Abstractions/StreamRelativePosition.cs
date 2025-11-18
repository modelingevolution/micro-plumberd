using EventStore.Client;

namespace MicroPlumberd;

/// <summary>
/// Represents a position in an EventStore stream relative to a starting point.
/// Supports forward and backward navigation from the start or end of a stream.
/// </summary>
public readonly struct FromRelativeStreamPosition
{
    private readonly FromStream _start;
    private readonly long _delta;

    /// <summary>
    /// Gets the starting position in the stream.
    /// </summary>
    /// <value>The <see cref="FromStream"/> position used as the reference point.</value>
    public FromStream StartPosition => _start;

    /// <summary>
    /// Gets the direction of navigation from the starting position.
    /// </summary>
    /// <value><see cref="Direction.Forwards"/> if delta is non-negative, otherwise <see cref="Direction.Backwards"/>.</value>
    public Direction Direction => _delta >= 0 ? Direction.Forwards : Direction.Backwards;

    /// <summary>
    /// Gets the absolute number of events to move from the starting position.
    /// </summary>
    /// <value>The absolute value of the delta.</value>
    public ulong Count => (ulong)(_delta >= 0 ? _delta : -_delta);

    /// <summary>
    /// Initializes a new instance of the <see cref="FromRelativeStreamPosition"/> struct.
    /// </summary>
    /// <param name="start">The starting position in the stream.</param>
    /// <param name="delta">The number of events to move from the start. Positive values move forward, negative values move backward.</param>
    public FromRelativeStreamPosition(FromStream start, long delta)
    {
        this._start = start;
        this._delta = delta;
    }

    /// <summary>
    /// Implicitly converts a <see cref="FromStream"/> to a <see cref="FromRelativeStreamPosition"/> with zero delta.
    /// </summary>
    /// <param name="st">The starting stream position.</param>
    public static implicit operator FromRelativeStreamPosition(FromStream st) => new FromRelativeStreamPosition(st, 0);

    /// <summary>
    /// Gets a position representing the start of the stream with zero offset.
    /// </summary>
    public static FromRelativeStreamPosition Start => new FromRelativeStreamPosition(FromStream.Start, 0);

    /// <summary>
    /// Gets a position representing the end of the stream with zero offset.
    /// </summary>
    public static FromRelativeStreamPosition End => new FromRelativeStreamPosition(FromStream.End, 0);

    /// <summary>
    /// Increments the position by one event.
    /// </summary>
    /// <param name="p">The position to increment.</param>
    /// <returns>A new position moved forward by one event.</returns>
    public static FromRelativeStreamPosition operator ++(FromRelativeStreamPosition p) => new(p._start, p._delta + 1);

    /// <summary>
    /// Decrements the position by one event.
    /// </summary>
    /// <param name="p">The position to decrement.</param>
    /// <returns>A new position moved backward by one event.</returns>
    public static FromRelativeStreamPosition operator --(FromRelativeStreamPosition p) => new(p._start, p._delta - 1);

    /// <summary>
    /// Adds a delta to the position.
    /// </summary>
    /// <param name="p">The current position.</param>
    /// <param name="d">The number of events to add.</param>
    /// <returns>A new position offset by the specified delta.</returns>
    public static FromRelativeStreamPosition operator +(FromRelativeStreamPosition p, long d) =>
        new FromRelativeStreamPosition(p._start, p._delta + d);

    /// <summary>
    /// Subtracts a delta from the position.
    /// </summary>
    /// <param name="p">The current position.</param>
    /// <param name="d">The number of events to subtract.</param>
    /// <returns>A new position offset backward by the specified delta.</returns>
    public static FromRelativeStreamPosition operator -(FromRelativeStreamPosition p, long d) =>
        new FromRelativeStreamPosition(p._start, p._delta - d);
}
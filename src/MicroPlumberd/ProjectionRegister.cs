using System.Collections.Concurrent;
﻿using KurrentDB.Client;
using Grpc.Core;

namespace MicroPlumberd;

/// <summary>
/// Provides caching and retrieval of EventStore projection details.
/// </summary>
public interface IProjectionRegister
{
    /// <summary>
    /// Gets the projection details for the specified projection name.
    /// </summary>
    /// <param name="name">The name of the projection.</param>
    /// <returns>The projection details if found; otherwise, null.</returns>
    Task<ProjectionDetails?> Get(string name);
}

static class Executor
{
    public static async Task<TResult> Retry<TException, TResult>(Func<Task<TResult>> action, int count = 3, int delay=100) where TException:Exception
    {
        TException last = null!;
        for(int i = 0; i < count; i++)
        try
        {
            return await action();
        }
        catch (TException e)
        {
            last = e;
            await Task.Delay(delay + i*delay);
        }

        throw last!;
    }
}
/// <summary>
/// The projection list, loaded once and reused. A projection THIS process creates is not in a list loaded before it, so
/// an ensure that creates one <see cref="Invalidate"/>s it: the next <see cref="Get"/> reloads and sees it. What a second
/// ensure in the SAME run does is decided by <see cref="DefinedHere"/> first: the same definition is no change, a different
/// one surfaces — never by the list, which would send it down update-if-changed and silently overwrite.
/// </summary>
class ProjectionRegister : IProjectionRegister
{
    private readonly KurrentDBProjectionManagementClient _client;
    private volatile AsyncLazy<Dictionary<string, ProjectionDetails>> _lazyLoader;

    public ProjectionRegister(KurrentDBProjectionManagementClient client)
    {
        _client = client;
        _lazyLoader = Load();
    }

    private int _loads;

    /// <summary>How many times the list was loaded from the store (a reload is paid on every startup: kept to the create path).</summary>
    internal int Loads => Volatile.Read(ref _loads);

    private AsyncLazy<Dictionary<string, ProjectionDetails>> Load() => new(async () =>
    {
        Interlocked.Increment(ref _loads);
        return await Executor.Retry<RpcException, Dictionary<string, ProjectionDetails>>(async () =>
            await _client.ListContinuousAsync().ToDictionaryAsync(x => x.Name).AsTask(), delay: 500, count: 5);
    });

    /// <summary>Drops the loaded list; the next <see cref="Get"/> loads it again.</summary>
    internal void Invalidate() => _lazyLoader = Load();

    // name → (query hash, who defined it) of each projection THIS process ensured in this run. Within one run an output stream
    // has ONE definition: a second, different one is two code paths fighting over it (#29) and must surface, not overwrite —
    // naming BOTH definers, so two model types whose short names collide (Ns1.FooModel / Ns2.FooModel) are obvious.
    private readonly ConcurrentDictionary<string, Definition> _definedHere = new(StringComparer.Ordinal);

    /// <summary>The definition this process ensured <paramref name="name"/> with in this run, or null.</summary>
    internal Definition? DefinedHere(string name) => _definedHere.TryGetValue(name, out var d) ? d : null;

    /// <summary>Records that this process ensured <paramref name="name"/> with <paramref name="definition"/>; the first one wins.</summary>
    internal Definition Defined(string name, Definition definition) => _definedHere.GetOrAdd(name, definition);

    /// <summary>A projection's query hash and what defined it: a model type's full name, or a by-name ensure.</summary>
    internal readonly record struct Definition(string Hash, string By)
    {
        public static Definition Of(string hash, Type? definedBy) =>
            new(hash, definedBy?.FullName ?? "an ensure by output stream name (no model type)");
    }

    public async Task<ProjectionDetails?> Get(string name)
    {
        return (await _lazyLoader.Value).TryGetValue(name, out var v) ? v : null;
    }
}

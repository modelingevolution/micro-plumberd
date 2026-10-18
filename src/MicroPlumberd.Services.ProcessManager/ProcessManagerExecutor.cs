using System;
using System.Collections.Concurrent;
using KurrentDB.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services.ProcessManagers;

/// <summary>
/// Executes process manager operations including event handling, command dispatching, and error compensation.
/// </summary>
/// <typeparam name="TProcessManager">The type of the process manager.</typeparam>
public class ProcessManagerExecutor<TProcessManager>(ProcessManagerClient pmClient, ILogger<ProcessManagerExecutor<TProcessManager>> log)  : IEventHandler, ITypeRegister
    where TProcessManager : IProcessManager, ITypeRegister
{
    /// <summary>
    /// rev-reach (CB2): the engine sets an IVersionAware manager's Version to the STREAM REVISION (issue #27), so for such a manager
    /// `Version >= 0` no longer means "this build initialised it" — a stream of only events this build does not map reads as
    /// created. "Created" needs the applied-events count (IAppliedEventsAware, which ProcessManagerBase implements); a manager that
    /// is IVersionAware WITHOUT it cannot be told apart, and is refused here, by name, when the executor is built (at startup).
    /// A manager that is not IVersionAware keeps its own Version semantics and the old rule.
    /// </summary>
    private static readonly bool CountsAppliedEvents = typeof(IAppliedEventsAware).IsAssignableFrom(typeof(TProcessManager));
    private readonly bool _refusedUnlessItCountsAppliedEvents =
        CountsAppliedEvents || !typeof(IVersionAware).IsAssignableFrom(typeof(TProcessManager))
            ? true
            : throw new InvalidOperationException(
                $"Process manager {typeof(TProcessManager).FullName} is IVersionAware but not IAppliedEventsAware: its Version is the " +
                "stream revision, so whether THIS build ever initialised it cannot be told. Derive from ProcessManagerBase, or implement IAppliedEventsAware.");

    /// <summary>
    /// Maintains a lookup table mapping command recipient IDs to process manager IDs.
    /// </summary>
    public class Lookup : IEventHandler, ITypeRegister
    {
        private readonly Dictionary<Guid, Guid> _managerByReceiverId = new Dictionary<Guid, Guid>();

        /// <summary>
        /// Gets the process manager ID associated with the specified command recipient ID.
        /// </summary>
        /// <param name="receiverId">The command recipient identifier.</param>
        /// <returns>The process manager ID if found; otherwise, null.</returns>
        public Guid? GetProcessManagerIdByReceiverId(Guid receiverId) => _managerByReceiverId.TryGetValue(receiverId, out var v) ? v : null;

        private void Given(Metadata m, ICommandEnqueued ev)
        {
            _managerByReceiverId[ev.RecipientId] = m.Id;
        }

        /// <inheritdoc />
        public async Task Handle(Metadata m, object ev)
        {
            if (ev is ICommandEnqueued sf)
                Given(m, sf);
        }

        /// <inheritdoc />
        public static IEnumerable<Type> Types=> TProcessManager.CommandTypes;
    }

    /// <summary>
    /// Handles sending enqueued commands from process managers to their recipients.
    /// </summary>
    internal class Sender(IProcessManagerClient pmClient) : IEventHandler, ITypeRegister
    {
        public async Task Handle(Metadata m, object cmd)
        {
            var c = (ICommandEnqueued)cmd;
            try
            {
                await pmClient.Bus.SendAsync(c.RecipientId, c.Command);
            }
            catch (Exception ex)
            {
                var manager = await pmClient.GetManager<TProcessManager>(c.RecipientId);

                CommandInvocationFailed evt = new CommandInvocationFailed() { Command = CommandRequest.Create(c.RecipientId, c.Command), Message = ex.Message, RecipientId = c.RecipientId };
                var plm = pmClient.Plumber;
                var streamId = plm.Config.Conventions.GetStreamIdConvention(null, typeof(TProcessManager), manager.Id);
                await plm.AppendEvents(streamId, StreamState.Any, evt);

                Guid causationId = m.CausationId() ?? throw new InvalidOperationException("Causation id is not provided.");
                var causationEvent = await plm.FindEventInStream(streamId, causationId, pmClient.Plumber.TypeHandlerRegisters.GetEventNameConverterFor<TProcessManager>());

                ExecutionContext context = new ExecutionContext(causationEvent.Metadata, causationEvent.Event, c.RecipientId, CommandRequest.Create(c.RecipientId,c.Command),ex);
                var compensationCommand = await manager.HandleError(context);
                if (compensationCommand != null)
                {
                    var evt2 =  CommandEnqueued.Create(compensationCommand.RecipientId, compensationCommand.Command);
                    await plm.AppendEvents(streamId, StreamState.StreamExists, evt2);
                }
            }

        }

        public static IEnumerable<Type> Types => TProcessManager.CommandTypes;
    }


    /// <inheritdoc />
    public async Task Handle(Metadata m, object evt)
    {
        var plb = pmClient.Plumber;
        IProcessAction? action = null;
        string streamId = string.Empty;
        if (TProcessManager.StartEvent == evt.GetType())
        {
            Guid aggId = Guid.Parse(m.SourceStreamId.Substring(m.SourceStreamId.IndexOf('-')+1));
            var manager = await pmClient.GetManager<TProcessManager>(aggId);
            streamId = plb.Config.Conventions.GetStreamIdConvention(null,typeof(TProcessManager), manager.Id);
            action = await manager.StartWhen(m, evt);
        }
        else
        {
            var manager = await pmClient.GetManager<TProcessManager>(m.Id);
            streamId = plb.Config.Conventions.GetStreamIdConvention(null, typeof(TProcessManager), manager.Id);
            // "Created" is at least one APPLIED event, never `Version < 0` (rev-reach cc224bd49): the version is the stream revision
            // (issue #27), so a stream of only events this build does not map has a version yet a manager in its default state —
            // acting on it would enqueue commands from nothing. A manager that is not IVersionAware keeps its own Version rule (an
            // IVersionAware one that does not count applied events is refused when the executor is built).
            var created = manager is IAppliedEventsAware applied ? applied.AppliedEvents > 0 : manager.Version >= 0; // not IVersionAware: its own Version
            if (!created)
            {
                if (manager.Version >= 0)
                    log.LogWarning("Process manager {Stream} holds only events this build does not map (version {Version}); the event is not handled.", streamId, manager.Version);
                else
                    log.LogDebug("We've received event to process-manager that was not created.");
                return;
            }
            action = await manager.When(m, evt);
        }

        await plb.AppendLink(streamId,m);
        if (action is ICommandRequest cmd)
            await plb.AppendEvents(streamId, StreamState.Any, CommandEnqueued.Create(cmd.RecipientId, cmd.Command));
        else if (action is IStateChangeAction s)
            await plb.AppendEvents(streamId, (ulong)(s.Version), s.Events);
    }


    /// <inheritdoc />
    public static IEnumerable<Type> Types => TProcessManager.Types;
}
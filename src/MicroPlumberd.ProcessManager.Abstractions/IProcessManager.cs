using System.Security.Cryptography;
using System;

// ReSharper disable once CheckNamespace
namespace MicroPlumberd
{
    /// <summary>
    /// Interface representing a process manager that orchestrates long-running business processes by handling events and generating commands.
    /// </summary>
    public interface IProcessManager : IEventHandler, IVersioned, IId
    {
        /// <summary>
        /// Gets the type of event that starts this process manager.
        /// </summary>
        static abstract Type StartEvent { get; }

        /// <summary>
        /// Gets the collection of command types that this process manager can generate.
        /// </summary>
        static abstract IEnumerable<Type> CommandTypes { get; }

        /// <summary>
        /// Handles errors that occur during process manager execution.
        /// </summary>
        /// <param name="executionContext">The execution context containing error information.</param>
        /// <returns>An optional command request to execute in response to the error, or <c>null</c> to skip error handling.</returns>
        Task<ICommandRequest?> HandleError(ExecutionContext executionContext);

        /// <summary>
        /// Handles an event and potentially generates a command request in response.
        /// </summary>
        /// <param name="m">The metadata associated with the event.</param>
        /// <param name="evt">The event to handle.</param>
        /// <returns>An optional command request to execute, or <c>null</c> if no command should be generated.</returns>
        Task<ICommandRequest?> When(Metadata m, object evt);

        /// <summary>
        /// Handles the starting event that initiates this process manager and generates the first command.
        /// </summary>
        /// <param name="m">The metadata associated with the starting event.</param>
        /// <param name="evt">The starting event to handle.</param>
        /// <returns>A command request to execute.</returns>
        Task<ICommandRequest> StartWhen(Metadata m, object evt);
    }

    /// <summary>
    /// Abstract base class for process managers with strongly-typed identifiers, providing common infrastructure for ID and version management.
    /// </summary>
    /// <typeparam name="TId">The type of the process manager identifier, which must be parsable.</typeparam>
    public abstract class ProcessManagerBase<TId> : IVersionAware, IIdAware, IId<TId> where TId : IParsable<TId>
    {
        private TId _id;

        /// <summary>
        /// Gets the unique identifier of this process manager instance.
        /// </summary>
        public TId Id => _id;

        /// <summary>
        /// Gets or sets the unique identifier of this process manager instance as an object.
        /// </summary>
        object IIdAware.Id { set => _id = (TId)value;
            get => _id;
        }

        /// <summary>
        /// Gets or sets the version of this process manager for optimistic concurrency control.
        /// </summary>
        public long Version { get; set; } = -1;

        /// <summary>
        /// Handles errors that occur during process manager execution. Default implementation returns <c>null</c>.
        /// </summary>
        /// <param name="executionContext">The execution context containing error information.</param>
        /// <returns>An optional command request to execute in response to the error, or <c>null</c> to skip error handling.</returns>
        public virtual async Task<ICommandRequest?> HandleError(ExecutionContext executionContext) => null;
    }


}

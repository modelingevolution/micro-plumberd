using System.ComponentModel;
using System.Runtime.Serialization;

namespace MicroPlumberd.Services.Cron
{
    /// <summary>
    /// Event indicating that a job execution command has been executed.
    /// </summary>
    public record StartJobExecutionExecuted
    {
        /// <summary>
        /// Gets or sets the unique identifier for this event.
        /// </summary>
        public Guid Id { get; init; } = Guid.NewGuid();

        /// <summary>
        /// Gets or sets the unique identifier of the command that was executed.
        /// </summary>
        [DataMember(Order = 1)]
        public Guid CommandId { get; set; }

        /// <summary>
        /// Gets or sets the duration of the execution.
        /// </summary>
        [DataMember(Order = 2)]
        public TimeSpan Duration { get; set; }
    }
}

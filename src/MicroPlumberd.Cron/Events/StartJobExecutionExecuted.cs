using System.ComponentModel;
using System.Runtime.Serialization;

namespace MicroPlumberd.Services.Cron
{

    public record StartJobExecutionExecuted
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        [DataMember(Order = 1)]
        public Guid CommandId { get; set; }

        [DataMember(Order = 2)]
        public TimeSpan Duration { get; set; }
    }
}

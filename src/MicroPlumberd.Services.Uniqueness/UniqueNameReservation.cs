using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroPlumberd.Services.Uniqueness
{
    class UniqueNameReservation
    {
        public long Id { get; init; }
        
        public string Name { get; init; }
        public Guid SourceId { get; init; }
        public DateTime ValidUntil { get; init; }
        public bool IsConfirmed { get; set; }
    }
}
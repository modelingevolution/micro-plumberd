using System.Security.Cryptography;
using System;

// ReSharper disable once CheckNamespace
namespace MicroPlumberd
{
    public interface IProcessManager : IEventHandler, IVersioned, IId
    {
        static abstract Type StartEvent { get; }
        Task<ICommandRequest?> HandleError(ExecutionContext executionContext);
        Task<ICommandRequest?> When(Metadata m, object evt);
        Task<ICommandRequest> StartWhen(Metadata m, object evt);
    }




    
}

using System.Security.Cryptography;
using System;

// ReSharper disable once CheckNamespace
namespace MicroPlumberd
{
    public interface IProcessManager : IEventHandler, IVersioned, IId
    {
        static abstract Type StartEvent { get; }
        static abstract IEnumerable<Type> CommandTypes { get; }
        Task<ICommandRequest?> HandleError(ExecutionContext executionContext);
        Task<ICommandRequest?> When(Metadata m, object evt);
        Task<ICommandRequest> StartWhen(Metadata m, object evt);
    }


    public abstract class ProcessManagerBase : IVersionAware, IIdAware, IId
    {
        private long _version = -1;
        private Guid _id;
        public Guid Id => _id;
        Guid IIdAware.Id { set => _id = value; }
        public long Version => _version;
        void IVersionAware.Increase() => _version += 1;
    }


}

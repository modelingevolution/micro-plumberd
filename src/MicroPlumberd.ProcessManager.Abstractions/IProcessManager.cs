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


    public abstract class ProcessManagerBase<TId> : IVersionAware, IIdAware, IId<TId> where TId : IParsable<TId>
    {
        private long _version = -1;
        private TId _id;
        public TId Id => _id;
        object IIdAware.Id { set => _id = (TId)value; }
        public long Version => _version;
        void IVersionAware.Increase() => _version += 1;
        void IVersionAware.SetValue(long nv) { _version = nv; }

        public virtual async Task<ICommandRequest?> HandleError(ExecutionContext executionContext) => null;
    }


}

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Dynamic;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using EventStore.Client;
using MicroPlumberd;
using MicroPlumberd.Services;
using MicroPlumberd.Utils;
using static CorrelationModel;

[assembly: InternalsVisibleTo("MicroPlumberd.Tests")]

public class CorrelationModelBuilder
{
    private readonly List<TypeEventConverter> _converters = new();
    private readonly IServicesConvention _conventions;
    private readonly IPlumber _plumber;
    private readonly Dictionary<string, Type> _register = new Dictionary<string, Type>();
    internal CorrelationModelBuilder(IPlumber plumber)
    {
        _plumber = plumber;
        _conventions = plumber.Config.Conventions.ServicesConventions();
        _converters.Add(_register.TryGetValue);
    }

    public CorrelationModelBuilder WithEventHandler<T>() where T:IEventHandler, ITypeRegister
    {
        var converter = _plumber.TypeHandlerRegisters.GetEventNameConverterFor<T>();
        _converters.Add(converter);
        return this;
    }

    public CorrelationModelBuilder WithEvent<T>()
    {
        var converter = _plumber.Config.Conventions.GetEventNameConvention(null, typeof(T));
        _register.Add(converter, typeof(T));
        return this;
    }
    public CorrelationModelBuilder WithCommandHandler<T>() where T : ICommandHandler, IServiceTypeRegister
    {
        foreach (var commandType in T.CommandTypes)
        {
            string cmdName = _plumber.Config.Conventions.GetEventNameConvention(null, commandType);
            _register.Add(cmdName, commandType);
            var cmdNameExecuted = _conventions.CommandNameConvention(commandType);
            //ThrowsFaultExceptionAttribute
            string executedCommand = $"{cmdNameExecuted}Executed";
            _register.Add(executedCommand, typeof(CommandExecuted));

            string executedFailed = $"{cmdNameExecuted}Failed";
            _register.Add(executedFailed, typeof(CommandFailed));

            foreach (var i in commandType.GetCustomAttributes<ThrowsFaultExceptionAttribute>())
            {
                string executedFailedWithPayload = $"{cmdNameExecuted}Failed<{i.ThrownType.Name}>";
                _register.Add(executedFailedWithPayload, typeof(CommandFailed<>).MakeGenericType(i.ThrownType));
            }
        }

        return this;
    }
    public async Task<CorrelationModel> Read(Guid correlationId)
    {
        
        var model = new CorrelationModel(_converters.ToArray(), correlationId);
        await this._plumber.Rehydrate(model, $"$bc-{correlationId}", model.TryConvert, StreamPosition.Start);
        return model;
    }
    public async Task<CorrelationModel> Subscribe(Guid correlationId)
    {
        var model = new CorrelationModel(_converters.ToArray(), correlationId);
        await this._plumber.Subscribe( $"$bc-{correlationId}",FromRelativeStreamPosition.Start)
            .WithHandler(model, model.TryConvert);
        return model;
    }
}

public static class CorrelationModelBuilderExtensions
{
    public static CorrelationModelBuilder CorrelationModel(this IPlumber plumber)
    {
        return new CorrelationModelBuilder(plumber);
    }
}
[DebuggerDisplay("{Event}")]
public class CorrelationModel : IEventHandler, IEnumerable<CorrelationNode>
{
    public class CorrelationNode : IEquatable<CorrelationNode>, INotifyCollectionChanged, IReadOnlyList<CorrelationNode>, INotifyPropertyChanged
    {
        public CorrelationNode? Parent
        {
            get => _parent;
            internal set
            {
                if (_parent == value) return;
                _parent = value;
                if (_parent == null) return;
                if (!_parent._children.Contains(this))
                    _parent.AddChild(this);
            }
        }

        public Guid Id { get; }
        private readonly ObservableCollection<CorrelationNode> _children = new();
        private CorrelationNode? _parent;
        private TimeSpan? _duration;
        private HttpStatusCode? _faultCode;
        private string _faultMessage;
        private bool? _isFaulted;
        private object? _fault;
        public object Event { get; }

        public TimeSpan? Duration
        {
            get => _duration;
            internal set
            {
                if (SetField(ref _duration, value))
                {
                    OnPropertyChanged(nameof(IsCompleted));
                }
            }
        }

        public object? Fault
        {
            get => _fault;
            internal set => SetField(ref _fault, value);
        }

        public HttpStatusCode? FaultCode
        {
            get => _faultCode;
            internal set => SetField(ref _faultCode, value);
        }

        public string FaultMessage
        {
            get => _faultMessage;
            internal set => SetField(ref _faultMessage, value);
        }

        public bool? IsFaulted
        {
            get => _isFaulted;
            internal set => SetField(ref _isFaulted, value);
        }

        public bool IsCompleted => Duration.HasValue;
        
        public T EventAs<T>() => (T)Event;
        
        public CorrelationNode(Metadata metadata, object @event)
        {
            Id = IdDuckTyping.Instance.TryGetGuidId(@event, out var g) ? g : throw new InvalidOperationException("Id is required!");
            Event = @event;
        }

        public CorrelationNode AddChild(CorrelationNode node)
        {
            _children.Add(node);
            return this;
        }
        public IEnumerator<CorrelationNode> GetEnumerator()
        {
            return _children.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_children).GetEnumerator();
        }

        public bool Equals(CorrelationNode? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Id.Equals(other.Id);
        }

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((CorrelationNode)obj);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public static bool operator ==(CorrelationNode? left, CorrelationNode? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(CorrelationNode? left, CorrelationNode? right)
        {
            return !Equals(left, right);
        }

        public event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add => _children.CollectionChanged += value;
            remove => _children.CollectionChanged -= value;
        }

        public int Count => _children.Count;

        public CorrelationNode this[int index] => _children[index];
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    private readonly List<CorrelationNode> _items = new();
    private CorrelationNode _root;
    private readonly ConcurrentDictionary<Guid, CorrelationNode> _nodes = new();
    private readonly TypeEventConverter[] _converters;
    private readonly Guid _correlationId;
    public Guid CorrelationId => _correlationId;
    internal CorrelationModel(TypeEventConverter[] converters, Guid correlationId)
    {
        _converters = converters;
        _correlationId = correlationId;
    }

    Task IEventHandler.Handle(Metadata m, object ev)
    {
        var causation = m.CausationId() ?? throw new InvalidOperationException("CausationId is required!");
        if (_nodes.TryGetValue(causation, out var src))
        {
            if (ev is CommandExecuted ce)
            {
                src.Duration = ce.Duration;
                src.IsFaulted = false;
            }
            else if (ev is ICommandFailed cf)
            {
                src.IsFaulted = true;
                src.FaultCode = cf.Code;
                src.FaultMessage = cf.Message;
            }
            else if (ev is ICommandFailedEx cfo)
            {
                src.IsFaulted = true;
                src.FaultCode = cfo.Code;
                src.FaultMessage = cfo.Message;
                src.Fault = cfo.Fault;
            }
            else
            {
                var node = new CorrelationNode(m, ev) { Parent = src! };
                _nodes.TryAdd(node.Id, node);
                _items.Add(node);
            }
        }
        else
        {
            if (_root != null) throw new InvalidOperationException("Root node is already set!");
            // this is root node.
            _root = new CorrelationNode(m, ev);
            _nodes.TryAdd(_root.Id, _root);
            _items.Add(_root);
       }
       return Task.CompletedTask;
    }


    public IEnumerator<CorrelationNode> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)_items).GetEnumerator();
    }
    public bool TryConvert(string type, out Type t)
    {
        foreach (var converter in _converters)
        {
            if (converter(type, out t))
            {
                return true;
            }
        }

        t = null;
        return false;
    }
}
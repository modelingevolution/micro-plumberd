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

/// <summary>
/// Provides a fluent API for building and configuring correlation models that track command and event relationships.
/// </summary>
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

    /// <summary>
    /// Registers an event handler's event types with the correlation model builder.
    /// </summary>
    /// <typeparam name="T">The event handler type that implements IEventHandler and ITypeRegister.</typeparam>
    /// <returns>The builder instance for method chaining.</returns>
    public CorrelationModelBuilder WithEventHandler<T>() where T:IEventHandler, ITypeRegister
    {
        var converter = _plumber.TypeHandlerRegisters.GetEventNameConverterFor<T>();
        _converters.Add(converter);
        return this;
    }

    /// <summary>
    /// Registers a single event type with the correlation model builder.
    /// </summary>
    /// <typeparam name="T">The event type to register.</typeparam>
    /// <returns>The builder instance for method chaining.</returns>
    public CorrelationModelBuilder WithEvent<T>()
    {
        var converter = _plumber.Config.Conventions.GetEventNameConvention(null, typeof(T));
        _register.Add(converter, typeof(T));
        return this;
    }

    /// <summary>
    /// Registers a command handler's command types and their result events with the correlation model builder.
    /// </summary>
    /// <typeparam name="T">The command handler type that implements ICommandHandler and IServiceTypeRegister.</typeparam>
    /// <returns>The builder instance for method chaining.</returns>
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

    /// <summary>
    /// Reads and reconstructs the correlation model from the event store for the specified correlation ID.
    /// </summary>
    /// <param name="correlationId">The correlation ID to read events for.</param>
    /// <returns>A task representing the asynchronous operation, containing the reconstructed correlation model.</returns>
    public async Task<CorrelationModel> Read(Guid correlationId)
    {

        var model = new CorrelationModel(_converters.ToArray(), correlationId);
        await this._plumber.Rehydrate(model, $"$bc-{correlationId}", model.TryConvert, StreamPosition.Start);
        return model;
    }

    /// <summary>
    /// Subscribes to and builds the correlation model in real-time from the event stream.
    /// </summary>
    /// <param name="correlationId">The correlation ID to subscribe to.</param>
    /// <returns>A task representing the asynchronous operation, containing the live correlation model.</returns>
    public async Task<CorrelationModel> Subscribe(Guid correlationId)
    {
        var model = new CorrelationModel(_converters.ToArray(), correlationId);
        await this._plumber.Subscribe( $"$bc-{correlationId}",FromRelativeStreamPosition.Start)
            .WithHandler(model, model.TryConvert);
        return model;
    }
}

/// <summary>
/// Provides extension methods for creating correlation model builders.
/// </summary>
public static class CorrelationModelBuilderExtensions
{
    /// <summary>
    /// Creates a new correlation model builder for tracking command and event causation chains.
    /// </summary>
    /// <param name="plumber">The plumber instance.</param>
    /// <returns>A new correlation model builder instance.</returns>
    public static CorrelationModelBuilder CorrelationModel(this IPlumber plumber)
    {
        return new CorrelationModelBuilder(plumber);
    }
}
/// <summary>
/// Represents a hierarchical model of correlated commands and events, tracking causation relationships.
/// </summary>
[DebuggerDisplay("{Event}")]
public class CorrelationModel : IEventHandler, IEnumerable<CorrelationNode>
{
    /// <summary>
    /// Represents a node in the correlation tree, tracking a single command or event and its relationships.
    /// </summary>
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

        /// <summary>
        /// Gets the unique identifier of the command or event represented by this node.
        /// </summary>
        public Guid Id { get; }
        private readonly ObservableCollection<CorrelationNode> _children = new();
        private CorrelationNode? _parent;
        private TimeSpan? _duration;
        private HttpStatusCode? _faultCode;
        private string _faultMessage;
        private bool? _isFaulted;
        private object? _fault;

        /// <summary>
        /// Gets the event or command object associated with this node.
        /// </summary>
        public object Event { get; }

        /// <summary>
        /// Gets or sets the duration of command execution, if applicable.
        /// </summary>
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

        /// <summary>
        /// Gets or sets the fault object if the command failed with a typed exception.
        /// </summary>
        public object? Fault
        {
            get => _fault;
            internal set => SetField(ref _fault, value);
        }

        /// <summary>
        /// Gets or sets the HTTP status code associated with a fault, if applicable.
        /// </summary>
        public HttpStatusCode? FaultCode
        {
            get => _faultCode;
            internal set => SetField(ref _faultCode, value);
        }

        /// <summary>
        /// Gets or sets the fault message if the command failed.
        /// </summary>
        public string FaultMessage
        {
            get => _faultMessage;
            internal set => SetField(ref _faultMessage, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the command execution resulted in a fault.
        /// </summary>
        public bool? IsFaulted
        {
            get => _isFaulted;
            internal set => SetField(ref _isFaulted, value);
        }

        /// <summary>
        /// Gets a value indicating whether the command execution has completed (either successfully or with a fault).
        /// </summary>
        public bool IsCompleted => Duration.HasValue;

        /// <summary>
        /// Casts the event to the specified type.
        /// </summary>
        /// <typeparam name="T">The type to cast the event to.</typeparam>
        /// <returns>The event cast to the specified type.</returns>
        public T EventAs<T>() => (T)Event;
        
        public CorrelationNode(Metadata metadata, object @event)
        {
            Id = IdDuckTyping.Instance.TryGetGuidId(@event, out var g) ? g : throw new InvalidOperationException("Id is required!");
            Event = @event;
        }

        /// <summary>
        /// Adds a child node to this correlation node, establishing a causation relationship.
        /// </summary>
        /// <param name="node">The child node to add.</param>
        /// <returns>This node for method chaining.</returns>
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

    /// <summary>
    /// Gets the correlation ID that identifies this correlation chain.
    /// </summary>
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

    /// <summary>
    /// Attempts to convert an event type name to its corresponding CLR type.
    /// </summary>
    /// <param name="type">The event type name to convert.</param>
    /// <param name="t">When this method returns, contains the CLR type if conversion succeeded; otherwise, null.</param>
    /// <returns>True if the conversion succeeded; otherwise, false.</returns>
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
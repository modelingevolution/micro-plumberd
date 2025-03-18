﻿using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Services.Cron;



[EventHandler]
public partial class JobDefinitionModel
{
    public event EventHandler<JobDefinition> JobSchduleChanged;
    public event EventHandler<JobDefinition> JobAvailabilityChanged;

    public record JobDefinition : INotifyPropertyChanged
    {
        private string _name;
        private bool _isEnabled;
        private Type _commandType;
        private string _recipient;
        private JsonElement _command;
        private Schedule _schedule;
        public Guid JobDefinitionId { get; init; }

        public Schedule Schedule
        {
            get => _schedule;
            set => SetField(ref _schedule, value);
        }

        public JsonElement Command
        {
            get => _command;
            set => SetField(ref _command, value);
        }

        public string Recipient
        {
            get => _recipient;
            set => SetField(ref _recipient, value);
        }

        public Type CommandType
        {
            get => _commandType;
            set => SetField(ref _commandType, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetField(ref _isEnabled, value);
        }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

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
    private readonly ConcurrentDictionary<Guid, JobDefinition> _jobDefinitions = new();
    public bool TryGetValue(Guid id, out JobDefinition job) => _jobDefinitions.TryGetValue(id, out job);
    private async Task Given(Metadata m, JobEnabled ev)
    {
        var job = this[m.StreamId<Guid>()];
        job.IsEnabled = true;
        this.JobAvailabilityChanged?.Invoke(this, job);
    }
    public ImmutableArray<JobDefinition> JobDefinitions => [.._jobDefinitions.Values];

    private async Task Given(Metadata m, JobDisabled ev)
    {
        var job = this[m.StreamId<Guid>()];
        job.IsEnabled = false;
        this.JobAvailabilityChanged?.Invoke(this, job);
    }

    private async Task Given(Metadata m, JobNamed ev)
    {
        if (!_jobDefinitions.TryAdd(m.StreamId<Guid>(), new JobDefinition() { Name = ev.Name }))
            throw new InvalidOperationException("Cannot add job definition");
    }

    public JobDefinition this[Guid id]
    {
        get => TryGetValue(id, out var job) ? job : throw new ArgumentOutOfRangeException("Job not found");
    }
    private async Task Given(Metadata m, JobScheduleDefined ev) => this[m.StreamId<Guid>()].Schedule = ev.Schedule;

    private async Task Given(Metadata m, JobProcessDefined ev)
    {
        var jobDef = this[m.StreamId<Guid>()];
        jobDef.CommandType = Type.GetType(ev.CommandType)!;
        jobDef.Recipient = ev.Recipient;
        jobDef.Command = ev.CommandPayload;
        JobSchduleChanged?.Invoke(this, jobDef);
    }
}
class SubjectPool : ISubjectPool
{
    private readonly Dictionary<string, List<Guid>> _index = new();

    public Guid Store(string subject, Guid id)
    {
        if (!_index.TryGetValue(subject, out var l))
            return WithNewList(subject, id)[0];
        if(l.Last() != id)
            l.Add(id);
        return id;
    }

    public Guid A(string subject)
    {
        if (!_index.TryGetValue(subject, out var l)) 
            return WithNewList(subject);
        var r = Guid.NewGuid();
        l.Add(r);
        return r;
    }

    public Guid The(string subject)
    {
        if (_index.TryGetValue(subject, out var l))
            return l.Last();
        throw new ArgumentOutOfRangeException($"Subject named {subject} was not defined.");
    }
    private Guid WithNewList(string key) => WithNewList(key, Guid.NewGuid())[0];

    private Guid[] WithNewList(string key, params Guid[] ids)
    {
        _index.Add(key, [..ids]);
        return ids;
    }
    public Guid GetOrCreate(string subject) => _index.TryGetValue(subject, out var l) ? l.Last() : WithNewList(subject);
}
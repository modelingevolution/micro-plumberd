class TocEntryNode : TocEntryLeaf
{
    private readonly TocEntryLeaf _n;

    public TocEntryNode()
    {
        _n = new TocEntryLeaf();
    }
    public TocEntryNode(TocEntryLeaf n)
    {
        _n = n;
    }

    public TocEntryLeaf Reduce()
    {
        if (Items.Count == 0) return _n;

        for (var index = 0; index < Items.Count; index++)
        {
            var i = Items[index];
            if (i is TocEntryNode n)
                Items[index] = n.Reduce();
        }
        return this;
    }

    public override string Href
    {
        get => _n.Href;
        set => _n.Href = value;
    }

    public override string Name
    {
        get => _n.Name;
        set => _n.Name = value;
    }
    public List<TocEntryLeaf> Items { get; } = new List<TocEntryLeaf>();
}
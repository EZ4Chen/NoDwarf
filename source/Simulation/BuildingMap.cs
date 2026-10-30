using System.Collections;

namespace NoDwarf.Simulation;

// All writes, including editor and test setup, notify navigation. A plain Dictionary
// allowed changes to bypass cache invalidation.
public sealed class BuildingMap : IReadOnlyDictionary<Cell, Building>
{
    private readonly Dictionary<Cell, Building> _items = new();
    private readonly Action<Cell> _changed;
    private readonly Func<Building,Building> _bind;
    public BuildingMap(Action<Cell> changed,Func<Building,Building>? bind=null) { _changed=changed;_bind=bind??(b=>b); }
    public Building this[Cell key]
    {
        get => _items[key];
        set
        {
            value=_bind(value);Validate(key,value);
            if (_items.TryGetValue(key, out var old) && old == value) return;
            _items[key] = value;
            _changed(key);
        }
    }
    private static void Validate(Cell key,Building value)
    {
        int extent=value.Kind is BuildKind.Door or BuildKind.Furnace ? 2 : 1;
        if(key!=value.Cell||!TerrainMap.InBounds(key)||!TerrainMap.InBounds(key+new Cell(0,0,extent-1)))
            throw new ArgumentOutOfRangeException(nameof(key),"Building must fit inside world bounds");
    }
    public IEnumerable<Cell> Keys => _items.Keys;
    public IEnumerable<Building> Values => _items.Values;
    public int Count => _items.Count;
    public bool ContainsKey(Cell key) => _items.ContainsKey(key);
    public bool TryGetValue(Cell key, out Building value) => _items.TryGetValue(key, out value!);
    public void Add(Cell key, Building value) { value=_bind(value);Validate(key,value); _items.Add(key, value); _changed(key); }
    public bool Remove(Cell key) => Remove(key, out _);
    public bool Remove(Cell key, out Building value)
    {
        if (!_items.Remove(key, out value!)) return false;
        _changed(key);
        return true;
    }
    public void Clear()
    {
        var cells = _items.Keys.ToArray();
        _items.Clear();
        foreach (var cell in cells) _changed(cell);
    }
    public IEnumerator<KeyValuePair<Cell, Building>> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

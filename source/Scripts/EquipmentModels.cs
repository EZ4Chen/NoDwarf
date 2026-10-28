using Godot;
using System.Text.Json;
namespace NoDwarf;

// Authored/baked resources are shared by residents. No texture or mesh generation during play.
public static class EquipmentModels
{
    public sealed class Entry
    {
        public string Name {get;set;}="";
        public string Texture {get;set;}="";
        public string Scene {get;set;}="";
        public bool Playable {get;set;}
    }
    private static Dictionary<string,Entry>? _catalog;
    public static IReadOnlyDictionary<string,Entry> Catalog=>_catalog??=JsonSerializer.Deserialize<Dictionary<string,Entry>>(
        Godot.FileAccess.GetFileAsString("res://Assets/Equipment/catalog.json"))!;
    public static Node3D? Create(string item,bool includeReserved=false)
    {
        if(!Catalog.TryGetValue(item,out var entry)||!entry.Playable&&!includeReserved)return null;
        return GD.Load<PackedScene>(entry.Scene).Instantiate<Node3D>();
    }
}

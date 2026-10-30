using System.Collections.ObjectModel;
namespace NoDwarf.Simulation;

// A capability belongs to a kind of item, never to an individual stack. Both UI entrances use this catalog.
public sealed record PlacementDefinition(string Item,string Block,int Count,string Support)
{
    public IReadOnlyDictionary<string,int> Cost=>new ReadOnlyDictionary<string,int>(new Dictionary<string,int>{{Item,Count}});
}
public sealed partial class ContentCatalog
{
    public IReadOnlyList<PlacementDefinition> Placements {get;}
    public PlacementDefinition Placement(string item,string block)=>Item(item).Placements.FirstOrDefault(p=>p.Block==block)??throw new InvalidDataException("物品未授权放置此方块："+item+" → "+block);
}
public sealed partial class DemoWorld
{
    public int AvailableInStack(int id)=>Items.FirstOrDefault(i=>i.Id==id&&i.Owner==""&&i.Slot=="") is {} s?Math.Max(0,s.Count-Reserved(id)):0;
    private bool PlacementSupported(Cell cell,PlacementDefinition p,bool plans)=>p.Support=="adjacent"?Supported(cell,plans):Solid(cell+new Cell(0,0,-1))||plans&&PlannedSolid(cell+new Cell(0,0,-1));
    private bool PlacementSpace(Cell cell,bool ignoreWater=false)=>Inside(cell)&&cell.Level>TerrainMap.MinLevel&&!Occupied(cell)&&!Farms.ContainsKey(cell)&&!Trees.ContainsKey(cell)&&!WaterPoints.Contains(cell)&&(ignoreWater||Fluids.Amount(cell)==0);
    public bool CanPlaceBlock(Cell cell,string item,string block)=>PlacementSpace(cell)&&CanPlace(cell)&&PlacementSupported(cell,Content.Placement(item,block),true);
    private bool PlacementSiteOpen(DemoJob job)=>PlacementSpace(job.Cell,true);
    private bool PlacementReady(DemoJob job)=>PlacementSiteOpen(job)&&Fluids.Amount(job.Cell)==0&&PlacementSupported(job.Cell,Content.Placement(job.PlaceItem,job.PlaceBlock),false)&&!Actors.Any(w=>IntersectsWorker(job.Cell,BuildKind.Wall,w));
    public bool QueuePlaceBlock(Cell cell,string item,string block,int preferredStack=0,bool announce=true)
    {
        var p=Content.Placement(item,block);
        if(Terrain.PlacedBlockCount+Jobs.Count(j=>j.Placing)>=200000){if(announce)Log("放置方块数量已达到存档上限。");return false;}
        if(!CanPlaceBlock(cell,item,block)){if(announce)Log("此处无法放置：需要空位、支撑且没有积水。");return false;}
        var job=new DemoJob{Id=++_nextJob,Cell=cell,PlaceItem=item,PlaceBlock=block};
        if(preferredStack!=0)
        {
            var stack=Items.FirstOrDefault(i=>i.Id==preferredStack&&i.Item==item&&i.Owner==""&&i.Slot=="");
            if(stack is null||AvailableInStack(preferredStack)<p.Count){if(announce)Log("所选物品已移动、被预留或数量不足，请重新选择。");return false;}
            job.Materials.Add(new(){Item=item,StackId=stack.Id,Count=p.Count});
        }
        else if(!Reserve(job,p.Cost)){if(announce)Log("可用材料不足。");return false;}
        Jobs.Add(job);Revision++;if(announce)Log("已规划放置"+Content.Block(block).Name+"，居民将搬料施工。");return true;
    }
}
public sealed partial class TerrainMap
{
    private readonly Dictionary<Cell,string> _placedBlocks=new();
    internal bool IsPlaced(Cell cell)=>_placedBlocks.ContainsKey(cell);
    public int PlacedBlockCount=>_placedBlocks.Count;
    public IEnumerable<TerrainBlock> PlacedBlocks=>_placedBlocks.OrderBy(p=>p.Key.X).ThenBy(p=>p.Key.Z).ThenBy(p=>p.Key.Level).Select(p=>new TerrainBlock(p.Key,p.Value));
    internal void Place(Cell cell,string block,ContentCatalog content)
    {
        if(!InBounds(cell)||cell.Level<=MinLevel||Solid(cell)||Ramp(cell)||content.Block(block).Shape!=BlockShape.Solid||!double.IsFinite(content.Block(block).Durability))throw new InvalidDataException("无法在此处写入放置方块");
        _placedBlocks.Add(cell,block);_changes.Add(cell);Revision++;Changed?.Invoke(cell);
    }
    internal void RestorePlacedBlocks(IEnumerable<TerrainBlock> entries,ContentCatalog content)
    {
        foreach(var e in entries)
        {
            if(e is null||e.DefinitionId is null||!content.Placements.Any(p=>p.Block==e.DefinitionId))throw new InvalidDataException("存档引用了不可放置的方块");
            Place(e.Cell,e.DefinitionId,content);
        }
    }
}

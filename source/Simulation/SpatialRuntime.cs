namespace NoDwarf.Simulation;

public sealed record TerrainBlock(Cell Cell,string DefinitionId);
public sealed partial class DemoWorld
{
    public BuildingDefinition BuildingDefinition(Building b)=>Content.Building(b.DefinitionId.Length==0?BlockCatalog.Id(b.Kind):b.DefinitionId);
    public BuildingDefinition BuildDefinition(DemoJob j)=>Content.Building(j.BuildDefinitionId.Length==0?BlockCatalog.Id(j.Build!.Value):j.BuildDefinitionId);
    public BuildingDefinition BlueprintDefinition(BlueprintPart p)=>Content.Building(p.DefinitionId.Length==0?BlockCatalog.Id(p.Kind):p.DefinitionId);
    public string BuildingName(Building b)=>BuildingDefinition(b).Name;
    public string BuildName(DemoJob j)=>j.Placing?Content.Block(j.PlaceBlock).Name:BuildDefinition(j).Name;
    public BlockDefinition BuildBlock(DemoJob j)=>j.Placing?Content.Block(j.PlaceBlock):Content.Block(BuildDefinition(j).Block);
    public IReadOnlyDictionary<string,int> BuildCost(DemoJob j)=>j.Placing?Content.Placement(j.PlaceItem,j.PlaceBlock).Cost:BuildDefinition(j).Cost;
    public string BuildCostText(BuildingDefinition d)=>string.Join("、",d.Cost.Select(p=>Content.Item(p.Key).Name+" "+p.Value));
    private Building BindBuilding(Building b)
    {
        var d=BuildingDefinition(b);if(d.Handler!=b.Kind)throw new InvalidDataException("建筑定义与处理器不匹配："+d.Id);
        return b with{DefinitionId=d.Id,Definition=d,BlockDefinition=Content.Block(d.Block)};
    }
    private void BindJobBuilding(DemoJob j)
    {
        if(j.Placing)
        {
            if(j.Build is not null||j.BuildDefinitionId.Length!=0)throw new InvalidDataException("放置与建筑定义冲突");
            Content.Placement(j.PlaceItem,j.PlaceBlock);j.ResolvedBuild=Content.Block(j.PlaceBlock);return;
        }
        if(j.PlaceBlock.Length!=0)throw new InvalidDataException("放置缺少来源物品");
        if(j.Build is null){if(j.BuildDefinitionId.Length!=0)throw new InvalidDataException("非建筑任务携带建筑定义");return;}
        var d=BuildDefinition(j);if(d.Handler!=j.Build)throw new InvalidDataException("建筑任务处理器不匹配");
        j.BuildDefinitionId=d.Id;j.ResolvedBuild=Content.Block(d.Block);
    }
    public bool QueueBuild(Cell cell,string definitionId,bool announce=true,int facing=0)
    {
        var d=Content.Building(definitionId);
        if(!CanPlace(cell,d.Handler,facing)){if(announce)Log("此处无法施工。需要空位与支撑。");return false;}
        var job=new DemoJob{Id=++_nextJob,Cell=cell,Build=d.Handler,BuildDefinitionId=d.Id,Facing=facing};
        if(!Reserve(job,d.Cost)){if(announce)Log("可用材料不足。");return false;}
        Jobs.Add(job);Revision++;if(announce)Log("已规划"+d.Name+"，居民会自行施工。");return true;
    }
}
public sealed partial class TerrainMap
{
    private readonly Dictionary<Cell,string> _definitionBlocks=new();
    public IEnumerable<TerrainBlock> DefinitionBlocks=>_definitionBlocks.OrderBy(p=>p.Key.X).ThenBy(p=>p.Key.Z).ThenBy(p=>p.Key.Level).Select(p=>new TerrainBlock(p.Key,p.Value));
    internal void RestoreDefinitionBlocks(IEnumerable<TerrainBlock> blocks,ContentCatalog catalog)
    {
        foreach(var b in blocks)
        {
            if(b is null||b.DefinitionId is null||!InBounds(b.Cell)||b.Cell.Level<=MinLevel||!Solid(b.Cell)||catalog.Block(b.DefinitionId).Shape!=BlockShape.Solid||!_definitionBlocks.TryAdd(b.Cell,b.DefinitionId))throw new InvalidDataException("地形方块记录无效");
        }
    }
    internal void GenerateDefinitionOres(ContentCatalog catalog)
    {
        foreach(var b in catalog.Blocks.Values.Where(b=>b.Generation is not null).OrderBy(b=>b.Id,StringComparer.Ordinal))
        {
            var rule=b.Generation!;uint hash=2166136261;foreach(char c in b.Id)hash=unchecked((hash^c)*16777619);
            var random=new Random(unchecked(Seed^(int)hash));
            for(int n=0;n<rule.Veins;n++)
            {
                int x=random.Next(1,Size-1),z=random.Next(1,Size-1),depth=random.Next(rule.MinDepth,rule.MaxDepth+1);
                var cell=new Cell(x,z,this[x,z].Level-depth);
                for(int i=0;i<rule.Size;i++)
                {
                    if(InBounds(cell)&&cell.Level>MinLevel&&rule.Hosts.Contains(BlockAt(cell)))_definitionBlocks.TryAdd(cell,b.Id);
                    int axis=random.Next(3),step=random.Next(2)==0?-1:1;
                    cell+=axis==0?new Cell(step,0):axis==1?new Cell(0,step):new Cell(0,0,step);
                }
            }
        }
    }
}

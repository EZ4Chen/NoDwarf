namespace NoDwarf.Simulation;

// A scripted construction sequence, not a replacement for the deferred multi-storey blueprint editor.
public sealed class TwoStoreyDemo
{
    private readonly Cell _origin;
    public int Stage { get; private set; }
    internal void RestoreStage(int stage)=>Stage=stage;
    public bool Finished => Stage == 5;
    public TwoStoreyDemo(Cell origin) => _origin = origin;
    public static List<BlueprintPart> Parts(int stage)
    {
        var parts = new List<BlueprintPart>();
        void Add(int x, int z, int y, BuildKind kind, int facing = 0) => parts.Add(new(new Cell(x,z,y),kind,facing));
        if (stage == 0)
        {
            foreach (var p in DemoWorld.ExampleBlueprint().Where(p => p.Kind is BuildKind.Wall or BuildKind.Door))
                parts.Add(p with { Kind = p.Offset.X == 3 && p.Offset.Z == 4 ? BuildKind.Door : BuildKind.StoneBricks });
            Add(2,7,0,BuildKind.PlankStairs,3);
            Add(2,6,0,BuildKind.StoneBricks); Add(2,5,0,BuildKind.StoneBricks);
        }
        if (stage == 1)
        {
            for (int x=0;x<6;x++) for(int z=0;z<5;z++)
                if ((x==0||x==5||z==0||z==4) && !(x==3&&z==4)) Add(x,z,1,BuildKind.StoneBricks);
            Add(2,6,1,BuildKind.PlankStairs,3); Add(2,5,1,BuildKind.StoneBricks);
        }
        if (stage == 2)
        {
            // Perimeter first, then interior slabs: each planned voxel has an existing/planned support.
            for (int ring=0;ring<3;ring++) for(int x=0;x<6;x++) for(int z=0;z<5;z++)
                if(Math.Min(Math.Min(x,5-x),Math.Min(z,4-z))==ring) Add(x,z,2,BuildKind.Planks);
            Add(2,5,2,BuildKind.PlankStairs,3);
        }
        if (stage == 3)
        {
            foreach(var p in DemoWorld.ExampleBlueprint().Where(p=>p.Kind is BuildKind.Wall or BuildKind.Door)) parts.Add(p with { Offset=p.Offset+new Cell(0,0,3), Kind=p.Kind==BuildKind.Wall?BuildKind.Log:p.Kind });
            for(int x=0;x<6;x++) for(int z=0;z<5;z++)
                if((x==0||x==5||z==0||z==4)&&!(x==2&&z==4)) Add(x,z,4,BuildKind.Planks);
        }
        if (stage == 4)
            for(int ring=0;ring<3;ring++) for(int x=0;x<6;x++) for(int z=0;z<5;z++)
                if(Math.Min(Math.Min(x,5-x),Math.Min(z,4-z))==ring) Add(x,z,5,BuildKind.Planks);
        if(stage==4) { Add(1,1,3,BuildKind.Furnace); Add(4,1,3,BuildKind.Anvil); }
        return parts;
    }
    public void Advance(DemoWorld world)
    {
        if(Finished || world.Jobs.Any(j=>j.Construction && j.Cell.X>=_origin.X && j.Cell.X<_origin.X+6 && j.Cell.Z>=_origin.Z && j.Cell.Z<_origin.Z+8)) return;
        foreach(var p in Parts(Stage))
        {
            if(world.Buildings.TryGetValue(_origin+p.Offset,out var existing)&&existing.Kind==p.Kind&&existing.Facing==p.Facing)continue;
            if(!world.QueueBuild(_origin+p.Offset,p.Kind,false,p.Facing))
            { world.Log("两层示例暂停：材料不足或位置被占用；可拆除冲突后重试。"); return; }
        }
        Stage++;
        world.Log(Stage==5 ? "屋顶已规划；上楼工坊可锻造。+ / − 切换楼层，H 查看完整建筑。" : $"两层工坊：正在施工第 {Stage}/5 阶段。");
    }
}

namespace NoDwarf.Simulation;
public static class BlockExposureChecks
{
    public static void Run()
    {
        var w=new DemoWorld(flat:true);var c=new Cell(40,40,4);var view=new WorldView(null);
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        Check(BlockExposure.BuildingFaces(w,view,c)==BlockFaces.All,"Isolated cube faces");
        foreach(var (_,d) in BlockExposure.Neighbors)w.Buildings[c+d]=new(c+d,BuildKind.Wall);
        Check(BlockExposure.BuildingFaces(w,view,c)==BlockFaces.None,"Enclosed cube must have no faces");
        foreach(var (face,d) in BlockExposure.Neighbors)
        {
            w.Buildings.Remove(c+d);Check(BlockExposure.BuildingFaces(w,view,c)==face,"Removal must expose only the shared face: "+face);
            w.Buildings[c+d]=new(c+d,BuildKind.Wall);
        }
        Check(BlockExposure.BuildingFaces(w,new WorldView(c.Level),c)==BlockFaces.Top,"Layer cut restores cap");
        var caps=new Dictionary<Cell,float>{{c+new Cell(1,0),c.Level}};
        Check(BlockExposure.BuildingFaces(w,new WorldView(null,WallCaps:caps),c)==BlockFaces.East,"Hidden front wall exposes neighbor");
        caps[c+new Cell(1,0)]=c.Level+.2f;
        Check((BlockExposure.BuildingFaces(w,new WorldView(null,WallCaps:caps),c)&BlockFaces.East)!=0,"Partial wall cannot hide whole side");
        w.Buildings[c+new Cell(1,0)]=new(c+new Cell(1,0),BuildKind.PlankStairs);
        Check((BlockExposure.BuildingFaces(w,view,c)&BlockFaces.East)!=0,"Stairs are not full opaque neighbors");
        var ground=new Cell(45,45,0);Check((BlockExposure.BuildingFaces(w,view,ground)&BlockFaces.Bottom)==0,"Terrain hides building underside");
        w.Terrain.Excavate(ground+new Cell(0,0,-1));Check((BlockExposure.BuildingFaces(w,view,ground)&BlockFaces.Bottom)!=0,"Excavation exposes underside");
        var preview=new Cell(60,60,4);var plans=BlockExposure.Neighbors.Select(n=>preview+n.Delta).ToHashSet();
        Check(BlockExposure.PlanFaces(w,view,preview,plans)==BlockFaces.None,"Six solid plans hide shared faces");
        Check(BlockExposure.BuildingFaces(w,view,preview)==BlockFaces.All,"Plans cannot hide completed geometry");
        foreach(var (face,d) in BlockExposure.Neighbors)
        {
            plans.Remove(preview+d);Check(BlockExposure.PlanFaces(w,view,preview,plans)==face,"Cancel restores ghost face: "+face);plans.Add(preview+d);
        }
        Check(BlockExposure.PlanFaces(w,new WorldView(preview.Level),preview,plans)==BlockFaces.Top,"Plan layer cut restores cap");
        var planCaps=new Dictionary<Cell,float>{{preview+new Cell(1,0),preview.Level+.2f}};
        Check(BlockExposure.PlanFaces(w,new WorldView(null,WallCaps:planCaps),preview,plans)==BlockFaces.East,"Partial plan cannot hide full neighbor side");
        Console.WriteLine("BLOCK_EXPOSURE_OK: six neighbors, removal, layer caps, cut walls, partial shapes and excavation");
    }
}

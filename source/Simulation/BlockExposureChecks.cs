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
        Console.WriteLine("BLOCK_EXPOSURE_OK: six neighbors, removal, layer caps, cut walls, partial shapes and excavation");
    }
}

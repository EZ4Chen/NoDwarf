namespace NoDwarf.Simulation;
public static class CropProductionChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Until(DemoWorld w,Func<bool> done,int seconds=1200)
    {for(int i=0;i<seconds*30&&!done();i++)w.Update(1d/30);Check(done(),"Crop/processing timeout: "+string.Join(";",w.Events.TakeLast(4)));}
    public static void Run()
    {
        var w=new DemoWorld();
        foreach(var c in w.Trees.Keys)for(int dx=-3;dx<=3;dx++)for(int dz=-3;dz<=3;dz++)
            if(dx!=0||dz!=0){int x=c.X+dx,z=c.Z+dz;if(x>=0&&z>=0&&x<DemoWorld.Width&&z<DemoWorld.Depth)Check(!w.Terrain[x,z].Tree,"Generated forest spacing");}
        Check(w.Trees.Count>100&&w.Trees.Count<2000,"Sparse forest still populated");
        w.Items.Clear();w.Stockpiles.Clear();w.Workers.RemoveRange(1,2);
        var c0=FarmingChecks.SurfacePlot(w);var dwarf=w.Workers[0];dwarf.X=c0.X-1;dwarf.Z=c0.Z;dwarf.Level=c0.Level;dwarf.Person.Schedule=Enumerable.Repeat("anything",24).ToArray();
        w.SpawnItem("birch_sapling",2,c0+new Cell(-1,0));
        Check(w.QueueTree(c0,false,"birch_sapling"),"Birch sapling reserved");
        Check(!w.QueueTree(c0+new Cell(1,0),false,"birch_sapling"),"Pending trees also enforce spacing");
        Until(w,()=>w.Trees.ContainsKey(c0));w=DemoWorld.LoadJson(w.SaveJson());
        Check(w.Trees[c0].Species=="birch_sapling","Tree species persists");
        Until(w,()=>w.Trees[c0].Age>=480);Check(w.QueueTree(c0,true),"Birch harvest");Until(w,()=>!w.Trees.ContainsKey(c0));
        Check(w.Total("birch_sapling")==3&&w.Total("wood")==16,"Birch returns matching seedlings and wood");
        w.Items.Clear();w.Jobs.Clear();
        Check(w.SetFarm(c0,c0,"cotton"),"Cotton field valid");w.SpawnItem("cotton_seed",1,c0+new Cell(-1,0));
        Until(w,()=>w.Farms[c0].Harvests==1);
        Check(w.Total("cotton")==4&&w.Total("cotton_seed")==1&&w.Total("raw_food")==0,"Cotton produces fiber, not food");
        w.ConfigureGrowingZone(w.GrowingZoneAt(c0)!.Id,"cotton",false);
        var loom=c0+new Cell(2,0);w.Buildings[loom]=new(loom,BuildKind.Loom);
        Check(w.QueueProcessing("cloth")&&!w.QueueProcessing("cloth"),"Physical fiber reserved only once");
        w=DemoWorld.LoadJson(w.SaveJson());Until(w,()=>w.Total("cloth")==2);
        Check(w.Total("cotton")==0,"Weaving consumes fiber");
        Check(w.QueueProcessing("bandages"),"Cloth can be processed further");Until(w,()=>w.Total("bandage")==3);
        Check(w.Total("cloth")==1,"Bandage processing consumes actual cloth");
        w.ConfigureGrowingZone(w.GrowingZoneAt(c0)!.Id,"potato",true);w.SpawnItem("potato_seed",1,c0+new Cell(-1,0));
        Until(w,()=>w.Farms[c0].Harvests==2);Check(w.Total("potato")==4,"Potatoes yield separate produce");
        w.ConfigureGrowingZone(w.GrowingZoneAt(c0)!.Id,"potato",false);
        var mill=c0+new Cell(2,1);w.Buildings[mill]=new(mill,BuildKind.Mill);w.SpawnItem("wheat",2,c0);
        Check(w.QueueProcessing("flour"),"Milling order");Until(w,()=>w.Total("flour")==2);Check(w.Total("wheat")==0,"Milling consumes grain");
        var kitchen=new DemoWorld(flat:true);kitchen.Items.Clear();kitchen.Stockpiles.Clear();kitchen.Workers.RemoveRange(1,2);
        var origin=new Cell(20,20);
        for(int x=0;x<=4;x++)for(int z=0;z<=4;z++)if(x==0||z==0||x==4||z==4){var c=origin+new Cell(x,z);kitchen.Buildings[c]=new(c,x==2&&z==4?BuildKind.Door:BuildKind.Wall);}
        var stove=origin+new Cell(1,1);kitchen.Buildings[stove]=new(stove,BuildKind.Stove);
        var stand=origin+new Cell(2,2);var cook=kitchen.Workers[0];cook.X=stand.X;cook.Z=stand.Z;cook.Level=0;cook.Person.Schedule=Enumerable.Repeat("anything",24).ToArray();
        foreach(string item in new[]{"flour","potato","mushroom"})kitchen.SpawnItem(item,2,stand);kitchen.SpawnItem("wood",3,stand);
        Check(kitchen.QueueProcessing("bread")&&kitchen.QueueProcessing("potato_meal")&&kitchen.QueueProcessing("mushroom_meal"),"All food recipes accepted");
        Until(kitchen,()=>!kitchen.Jobs.Any(j=>j.Processing));Check(kitchen.Total("meal")==7&&kitchen.Total("wood")==0,"Food processing consumes fuel and gives distinct recipe yields");
        Check(ClimateRules.TemperatureFactor("cotton",8)==0&&ClimateRules.TemperatureFactor("potato",8)>0,"Crop climate differences");
        Console.WriteLine("CROP_PRODUCTION_OK: sparse forest, pending spacing, species seed return, cotton harvest/weaving/bandages, potatoes, wheat milling, persistence");
    }
}

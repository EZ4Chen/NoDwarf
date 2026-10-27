namespace NoDwarf.Simulation;
public static class SettlementChecks
{
    private static void Check(bool b,string message){if(!b)throw new InvalidOperationException(message);}
    private static void Advance(DemoWorld w,int seconds){for(int i=0;i<seconds*30;i++)w.Update(1d/30);}
    public static void Run()
    {
        var forest=new DemoWorld();forest.Items.Clear();forest.Stockpiles.Clear();forest.Workers.RemoveRange(1,2);
        var p=FarmingChecks.SurfacePlot(forest);var worker=forest.Workers[0];worker.X=p.X-1;worker.Z=p.Z;worker.Level=p.Level;
        Check(!forest.QueueTree(p,false),"No planting without physical sapling");forest.SpawnItem("sapling",1,p+new Cell(-1,0));
        Check(forest.QueueTree(p,false),"Planting queued");Advance(forest,30);
        Check(forest.Trees.ContainsKey(p)&&forest.Total("sapling")==0,"Sapling planted and consumed");
        forest=DemoWorld.LoadJson(forest.SaveJson());Advance(forest,600);
        Check(forest.TreeHeight(p)>=8&&!forest.Walkable(p+new Cell(0,0,5)),"Mature tree occupies multiple vertical cells");
        Check(forest.QueueTree(p,true),"Mature tree chop queued");Advance(forest,60);
        Check(!forest.Trees.ContainsKey(p)&&!forest.Terrain[p.X,p.Z].Tree&&forest.Total("wood")==20&&forest.Total("sapling")==2,"Felling yields wood and renewable saplings once");
        forest=DemoWorld.LoadJson(forest.SaveJson());Check(!forest.Trees.ContainsKey(p)&&!forest.QueueTree(p,true),"Cut tree remains absent after load");

        var w=new DemoWorld(flat:true);w.Items.Clear();w.Stockpiles.Clear();w.Workers.RemoveRange(1,2);
        var a=new Cell(10,10);var b=new Cell(12,10);w.SetStockpile(a,a);w.SetStockpile(b,b);
        var low=w.StorageAt(a)!;var high=w.StorageAt(b)!;w.ConfigureStorage(low.Id,2,new[]{"wood","coal"});w.ConfigureStorage(high.Id,5,new[]{"wood"});
        w.SpawnItem("wood",4,a);w.SpawnItem("coal",3,a);Advance(w,45);
        Check(w.Items.Single(i=>i.Item=="wood").Cell==b&&w.Items.Single(i=>i.Item=="coal").Cell==a,"Higher priority pulls only permitted items");
        Advance(w,10);Check(!w.Jobs.Any(j=>j.Haul),"Equal-priority stock does not shuffle forever");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.StorageAt(b)!.Priority==5&&!w.StorageAt(b)!.Allowed.Contains("coal"),"Storage filters and priorities persist");
        w.ConfigureStorage(w.StorageAt(b)!.Id,5,new[]{"coal"});Advance(w,45);
        Check(w.Items.Single(i=>i.Item=="wood").Cell==a&&w.Items.Single(i=>i.Item=="coal").Cell==b,"Changed filters relocate excluded items");

        var home=new DemoWorld(flat:true);home.Items.Clear();home.Stockpiles.Clear();home.Workers.RemoveRange(1,2);
        var origin=new Cell(20,20);
        for(int x=0;x<=5;x++)for(int z=0;z<=5;z++)
        {
            var c=origin+new Cell(x,z);
            if(x==0||z==0||x==5||z==5)home.Buildings[c]=new(c,x==2&&z==5?BuildKind.Door:BuildKind.Wall);
            if((x==0||z==0||x==5||z==5)&&!(x==2&&z==5))home.Buildings[c+new Cell(0,0,1)]=new(c+new Cell(0,0,1),BuildKind.Wall);
            var roof=c+new Cell(0,0,2);home.Buildings[roof]=new(roof,BuildKind.Planks);
        }
        var stove=origin+new Cell(1,1);var bed=origin+new Cell(4,1);var fire=origin+new Cell(1,4);var stand=origin+new Cell(2,2);
        home.Buildings[stove]=new(stove,BuildKind.Stove);home.Buildings[bed]=new(bed,BuildKind.Bed);home.Buildings[fire]=new(fire,BuildKind.Campfire);
        var person=home.Workers[0];person.X=stand.X;person.Z=stand.Z;person.Level=stand.Level;
        Check(home.KitchenReady(stove)&&!home.WorkshopReady,"Kitchen independent of metallurgy");
        home.SpawnItem("raw_food",2,stand);home.SpawnItem("wood",2,stand);
        Check(home.QueueCook(),"Kitchen accepts physical cooking ingredients");Advance(home,60);
        Check(home.Total("meal")==2&&home.Total("wood")==0&&home.FireFuel.GetValueOrDefault(fire)>0,"Cooking and fire consume separate physical fuel");
        Check(home.HeatingAt(stand)==10&&home.HeatingAt(origin+new Cell(-1,2))==0,"Enclosed room warms, external wall blocks heat");
        home=DemoWorld.LoadJson(home.SaveJson());Check(home.FireFuel.GetValueOrDefault(fire)>0,"Burning fuel survives save/load");
        person=home.Workers[0];person.Person.Rest=5;Advance(home,20);
        Check(person.Person.Activity?.Target.StartsWith("bed:")==true&&person.Person.Rest>20,"Tired resident walks to an available bed and rests faster");
        Advance(home,350);Check(home.FireFuel.Count==0&&home.HeatingAt(stand)==0,"Fire runs out without free fuel");
        Console.WriteLine("SETTLEMENT_OK: tall renewable trees, physical chopping, independent kitchen, bed rest, fuel heating, filtered five-level storage, persistence");
    }
}

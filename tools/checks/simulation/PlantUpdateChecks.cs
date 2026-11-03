using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;
public static class PlantUpdateChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException("PLANT_UPDATES: "+message);}
    private static void Advance(DemoWorld w,double seconds){for(int i=0;i<(int)(seconds*60);i++)w.Update(1d/60);}
    private static void IndependentPlants()
    {
        var w=new DemoWorld();w.DeveloperResidents(false);w.Items.Clear();w.Stockpiles.Clear();
        Check(w.Plants.Count>0&&w.Farms.Count==0,"world generates wild crops without management zones");
        var c=FarmingChecks.SurfacePlot(w);var wild=c+new Cell(1,0);
        Check(w.SetFarm(c,c,"grain")&&w.SpawnPlant(c,"grain",10,true)&&w.SpawnPlant(wild,"grain",10),"managed and wild same species");
        w=CropClimateChecks.AtTime(w,100);Advance(w,4);
        Check(w.Plants[c].Growth==w.Plants[wild].Growth,"wild and planted crops use identical growth rules");
        int count=w.Plants.Count;double growth=w.Plants[c].Growth;
        Check(w.SetFarm(c,c,"grain",remove:true)&&w.Plants.Count==count&&w.Plants[c].Growth==growth,"zone removal preserves plant identity and progress");
        w=DemoWorld.LoadJson(w.SaveJson());Advance(w,4);
        Check(w.Farms.Count==0&&w.Plants[c].Growth>growth&&!w.Jobs.Any(j=>j.Farming),"unmanaged plant grows after save/load without automatic farming");
        w.Plants[c].Growth=DemoWorld.GrowTime("grain");
        Check(w.QueuePlantHarvest(c)&&!w.QueuePlantHarvest(c),"wild harvest is explicit and cannot duplicate");
        w=DemoWorld.LoadJson(w.SaveJson());
        Check(w.Jobs.Count(j=>j.Harvest&&j.Cell==c)==1,"wild harvest order survives load");
        foreach(var j in w.Jobs.Where(j=>j.Harvest).ToArray())w.CancelJob(j);
        Check(w.SetFarm(c,c,"potato")&&w.Plants[c].Crop=="grain","new zone does not replace an existing crop");
        w.ConfigureGrowingZone(w.GrowingZoneAt(c)!.Id,"cotton",false);
        Check(w.Plants[c].Crop=="grain"&&w.Farms[c].Crop=="cotton","desired species and living species stay separate");
        Advance(w,2);Check(w.Jobs.Any(j=>j.Harvest&&j.Cell==c),"managed mature plant harvested even when sowing disabled");
        Check(w.SetFarm(c,c,"grain",remove:true)&&!w.Jobs.Any(j=>j.Harvest&&j.Cell==c)&&w.Plants.ContainsKey(c),"erase cancels pending harvest but keeps mature plant");
        w.DeveloperResidents(true);var worker=w.Workers[0];worker.X=c.X-1;worker.Z=c.Z;worker.Level=c.Level;
        worker.Person.Schedule=Enumerable.Repeat("anything",24).ToArray();
        Check(w.QueuePlantHarvest(c),"queue independent crop for resident");
        for(int i=0;i<120*60&&w.Plants.ContainsKey(c);i++)w.Update(1d/60);
        Check(!w.Plants.ContainsKey(c)&&w.Total("wheat")==3&&w.Total("grain_seed")==1,"unmanaged harvest produces physical crop and seed exactly once");

    }
    public static void Run()
    {
        IndependentPlants();
        var queue=new ScheduledUpdates<int>();queue.Schedule(1,1);queue.Schedule(1,5);queue.Schedule(2,2);
        Check(queue.Count==2&&!queue.TryTake(1,out _)&&queue.TryTake(2,out int key)&&key==2,"dedup replaces deadline without stale work");
        Check(queue.Cancel(1)&&queue.Count==0&&!queue.TryTake(100,out _),"cancel removes queued event");
        var jobs=new JobCollection();var c=new Cell(60,60);var first=new DemoJob{Cell=c,Dig=true};jobs.Add(first);
        Check(jobs.At(c).Count==1,"job cell index add");jobs[0]=new DemoJob{Cell=c+new Cell(1,0),Dig=true};Check(jobs.At(c).Count==0,"index replacement removes old cell");jobs.Clear();Check(jobs.At(c+new Cell(1,0)).Count==0,"index clear");
        var rules=PlantGrowthRules.Default;var wheat=rules.Crop("grain");var potato=rules.Crop("potato");
        Check(wheat.FertilityFactor(.7)<potato.FertilityFactor(.7)&&wheat.FertilityFactor(1.4)>potato.FertilityFactor(1.4),"crop-specific fertility sensitivity");
        Check(wheat.LightFactor(.5)==0&&wheat.Resting(20)&&!wheat.Resting(12)&&!rules.Crop("mushroom").Resting(20),"light threshold and configurable rest, dark fungi exception");
        var w=new DemoWorld(flat:true);w.DeveloperResidents(false);w.Items.Clear();
        for(int x=0;x<16;x++)for(int z=0;z<16;z++){var floor=c+new Cell(x,z,-1);w.Terrain.Excavate(floor);w.Terrain.Place(floor,"nodwarf:soil",w.Content);}
        Check(w.SetFarm(c,c+new Cell(15,15),"grain"),"256-plot field");
        foreach(var p in w.Farms.Values)Check(w.SpawnPlant(p.Cell,p.Crop,1,true),"spawn crop");
        w=CropClimateChecks.AtTime(w,100);long before=w.FarmUpdateVisits;
        Advance(w,2);
        Check(w.LastFarmVisits<=32&&w.FarmUpdateVisits-before<=512,"bounded farm visits and no per-tick whole-field loop");
        Check(w.Plants.Values.All(p=>p.Growth>1),"every plot progresses, no starvation");
        var restored=DemoWorld.LoadJson(w.SaveJson());Advance(w,2);Advance(restored,2);
        Check(w.Plants.Values.Select(p=>p.Growth).SequenceEqual(restored.Plants.Values.Select(p=>p.Growth)),"scheduled growth continues identically after save");
        // No geometry changes: one combined notification for a nested bulk edit; navigation is already invalid inside it.
        w=new DemoWorld(flat:true);w.DeveloperResidents(false);long flushes=w.BlockUpdateFlushes,version=w.Navigation.Version;
        using(w.BatchBlockChanges())using(w.BatchBlockChanges())
        {
            w.Buildings[c]=new(c,BuildKind.Wall);w.Buildings.Remove(c);w.Buildings[c]=new(c,BuildKind.Wall);
            Check(w.Navigation.Version>version&&!w.Walkable(c)&&w.BlockUpdateFlushes==flushes,"immediate collision/nav, deferred secondary work");
        }
        Check(w.BlockUpdateFlushes==flushes+1,"nested block changes flush once");
        // A tree near a growth threshold uses a deadline, retries only once a second if blocked.
        w=new DemoWorld(flat:true);w.DeveloperResidents(false);
        var root=new Cell(70,70);w.Trees[root]=new(){Cell=root,Species="birch_sapling",Age=95,MaximumHeight=8,GrowthUpdatedAt=0,NextGrowthAt=1};w.Terrain.SetTree(root,true);
        w=DemoWorld.LoadJson(w.SaveJson());w.Buildings[root+new Cell(0,0,1)]=new(root+new Cell(0,0,1),BuildKind.Wall);
        Advance(w,.5);Check(w.TreeGrowthVisits==0,"no growth work before deadline");Advance(w,.6);Check(w.Trees[root].Height==1&&w.TreeGrowthVisits==1,"blocked growth stays small with one visit");
        w.Buildings.Remove(root+new Cell(0,0,1));Advance(w,1);Check(w.Trees[root].Height==3,"clearing obstruction permits scheduled retry");
        var mid=DemoWorld.LoadJson(w.SaveJson());Advance(w,2);Advance(mid,2);Check(w.Trees[root].Age==mid.Trees[root].Age,"tree schedule roundtrip");
        Check(w.FellTree(root),"remove scheduled tree");long visits=w.TreeGrowthVisits;Advance(w,2);Check(w.TreeGrowthVisits==visits,"removed trees don't leave ticking entries");
        Console.WriteLine("PLANT_UPDATES_OK: bounded deadlines, indexed jobs, fertility/rest, deterministic growth, bulk notifications, blocked tree retry/cancel");
    }
}

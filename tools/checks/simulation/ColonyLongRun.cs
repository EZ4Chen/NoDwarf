using System.Diagnostics;
using System.Text.Json;
namespace NoDwarf.Simulation;

// Player-equivalent designations only: no spawned supplies, completed jobs, restored needs or mature crops.
public static class ColonyLongRun
{
    public static void Run(string directory,int days=120)
    {
        Directory.CreateDirectory(directory);var timer=Stopwatch.StartNew();var w=new DemoWorld(region:"temperate");
        var c=w.Camp;var initial=w.Items.GroupBy(i=>i.Item).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Count));
        var problems=new List<string>();var rows=new List<object>();var weather=new HashSet<string>();var seasons=new HashSet<int>();
        foreach(var worker in w.Workers){worker.Person.WorkPriorities["construction"]=1;worker.Person.WorkPriorities["cooking"]=1;}
        if(!w.QueueBlueprint(c,DemoWorld.ExampleBlueprint()))throw new InvalidOperationException("Normal blueprint failed");
        foreach(var (offset,kind) in new[]{(new Cell(4,2),BuildKind.Stove),(new Cell(-2,1),BuildKind.Mill),(new Cell(-2,3),BuildKind.Loom),(new Cell(-2,5),BuildKind.Workbench),(new Cell(1,3),BuildKind.Campfire)})
            if(!w.QueueBuild(c+offset,kind))throw new InvalidOperationException("Normal facility plan failed: "+kind);
        for(int x=8;x<=12;x++)for(int z=0;z<=4;z++)if(x==8||x==12||z==0||z==4)w.QueueBuild(c+new Cell(x,z),x==10&&z==4?BuildKind.Door:BuildKind.Wall);
        foreach(int x in new[]{9,10,11})w.QueueBuild(c+new Cell(x,1),BuildKind.Bed);
        for(int x=8;x<=12;x++)for(int z=0;z<=4;z++)if((x==8||x==12||z==0||z==4)&&!(x==10&&z==4))w.QueueBuild(c+new Cell(x,z,1),BuildKind.Wall);
        for(int x=8;x<=12;x++)for(int z=0;z<=4;z++)w.QueueBuild(c+new Cell(x,z,2),BuildKind.Planks);
        for(int x=0;x<6;x++)for(int z=0;z<5;z++)if((x==0||x==5||z==0||z==4)&&!(x==2&&z==4))w.QueueBuild(c+new Cell(x,z,1),BuildKind.Wall);
        for(int x=0;x<6;x++)for(int z=0;z<5;z++)w.QueueBuild(c+new Cell(x,z,2),BuildKind.Planks);
        foreach(var (crop,x) in new[]{("grain",-7),("potato",-6),("cotton",-5)})
            if(!w.SetFarm(c+new Cell(x,0),c+new Cell(x,3),crop))throw new InvalidOperationException("Normal farm plan failed");
        foreach(var (recipe,target) in new[]{("cook",18),("flour",12),("bread",30),("potato_meal",30),("cloth",6),("bandages",6),("shirt",2),("trousers",2),("shield",1),("pickaxe",1),("hammer",1)})w.SetProductionTarget(recipe,target);
        int lastDay=-1;long hungerSamples=0,zeroFoodDays=0;double minHunger=100;int starvationStreak=0,maxStarvation=0;
        int initialLiving=w.Workers.Count;long tick=0;
        while(w.SimulationTime<days*GameCalendar.SecondsPerDay)
        {
            w.Update(1d/30);tick++;
            if(tick%300==0)
            {
                double hunger=w.Workers.Min(p=>p.Person.Hunger);minHunger=Math.Min(minHunger,hunger);
                if(hunger<1){hungerSamples++;starvationStreak++;maxStarvation=Math.Max(maxStarvation,starvationStreak);}else starvationStreak=0;
            }
            int day=(int)(w.SimulationTime/600);if(day==lastDay)continue;lastDay=day;
            weather.Add(w.Weather.Id);seasons.Add(w.Calendar.SeasonIndex);
            // Daily player management: pause overstocked growing zones and designate available trees when wood is low.
            foreach(var zone in w.GrowingZones.Values.ToArray())
            {
                string product=CropCatalog.Get(zone.Crop).Yield;int stock=w.Total(product);
                if(stock>80)w.ConfigureGrowingZone(zone.Id,zone.Crop,false);else if(stock<12)w.ConfigureGrowingZone(zone.Id,zone.Crop,true);
            }
            if(w.Total("wood")<60&&w.Jobs.Count(j=>j.Chop)<3)
                foreach(var tree in w.Trees.Values.Where(t=>t.Age>=DemoWorld.GrowTime(t.Species)&&Math.Abs(t.Cell.X-c.X)+Math.Abs(t.Cell.Z-c.Z)<45).OrderBy(t=>Math.Abs(t.Cell.X-c.X)+Math.Abs(t.Cell.Z-c.Z)).Take(3))w.QueueTree(tree.Cell,true);
            if(w.Total("meal")==0)zeroFoodDays++;
            rows.Add(new{day,date=w.Calendar.DateText,w.SimulationTime,weather=w.Weather.Id,temp=w.OutdoorTemperature,meals=w.Total("meal"),wood=w.Total("wood"),wheat=w.Total("wheat"),potatoes=w.Total("potato"),seeds=w.Total("grain_seed"),potatoSeeds=w.Total("potato_seed"),cottonSeeds=w.Total("cotton_seed"),harvests=w.Farms.Values.Sum(p=>p.Harvests),jobs=w.Jobs.Count,construction=w.Jobs.Count(j=>j.Construction),hunger=w.Workers.Select(p=>p.Person.Hunger).ToArray(),statuses=w.Workers.Select(p=>p.Status).ToArray(),room=w.RoomAt(c+new Cell(2,3)),fireFuel=w.FireFuel.Values.Sum(),targets=w.ProductionOrders.Select(o=>w.ProductionTargetStatus(o)).ToArray()});
            if(day>0&&day%30==0)
            {
                string json=w.SaveJson();File.WriteAllText(Path.Combine(directory,$"colony-day-{day}.json"),json);w=DemoWorld.LoadJson(json);
                Console.WriteLine($"LONG_RUN_DAY: {day}; meals={w.Total("meal")}; wood={w.Total("wood")}; harvests={w.Farms.Values.Sum(p=>p.Harvests)}; jobs={w.Jobs.Count}; wall={timer.Elapsed.TotalSeconds:F1}s");
            }
        }
        if(w.Workers.Count(p=>!p.Person.Dead)!=initialLiving)problems.Add("居民死亡");
        if(maxStarvation>=60)problems.Add("至少一位居民连续饥饿见底超过一天");
        if(w.Jobs.Any(j=>j.Construction))problems.Add("初始施工仍未完成");
        if(w.Farms.Values.Sum(p=>p.Harvests)==0)problems.Add("没有收获");
        if(w.Total("grain_seed")==0||w.Total("potato_seed")==0)problems.Add("种源断绝");
        if(!w.Fluids.Conserved)problems.Add("流体不守恒");
        var result=new{days,ticks=tick,realSeconds=timer.Elapsed.TotalSeconds,initialStock=initial,finalStock=w.Items.GroupBy(i=>i.Item).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Count)),minHunger,hungerSamples,maximumStarvationSeconds=maxStarvation*10,zeroFoodDays,seasons=seasons.Order().ToArray(),weather=weather.Order().ToArray(),problems,daysReport=rows};
        File.WriteAllText(Path.Combine(directory,"colony-long-run.json"),JsonSerializer.Serialize(result,DemoWorld.SaveOptions));
        File.WriteAllText(Path.Combine(directory,"colony-final.json"),w.SaveJson());
        Console.WriteLine($"LONG_RUN_{(problems.Count==0?"OK":"ISSUES")}: {days} days; "+string.Join(";",problems));
        if(problems.Count>0)throw new InvalidOperationException("Long-run acceptance failed: "+string.Join(";",problems));
    }
}

namespace NoDwarf.Simulation;
public static class LifeHospitalChecks
{
    static void Check(bool b,string why){if(!b)throw new InvalidOperationException("HOSPITAL: "+why);}
    public static void Run()
    {
        var w=CombatChecks.Fixture();w.Workers.Clear();
        for(int x=76;x<=80;x++)for(int z=59;z<=61;z++){var c=new Cell(x,z);w.Buildings[c]=new(c,BuildKind.Wall);}
        var stairs=new Cell(75,60);w.Buildings[stairs]=new(stairs,BuildKind.PlankStairs);
        for(int i=0;i<12;i++)
        {
            var r=new DemoWorker{Id="resident"+i,Name="居民"+i,X=60+i%4*2,Z=60+i/4*2};w.Workers.Add(r);
            foreach(var type in ResidentWork.Types)r.Person.WorkPriorities[type.Id]=0;
            r.Person.WorkPriorities["care"]=2;r.Person.WorkPriorities["doctor"]=1;
            if(i<4)w.InjureResident(r.Id,"torso","fracture",90);
            var bed=new Cell(76+i%4*2,60+i/4*2,i<2?1:0);w.Buildings[bed]=new(bed,BuildKind.Bed);if(i<4)w.SetMedicalBed(bed,true);
        }
        var timer=System.Diagnostics.Stopwatch.StartNew();double maxTick=0;var warmTicks=new List<double>();int hungerBottom=0,thirstBottom=0,waiting=0;int delivered=0;
        // Managed hospital: deliver real stock once per day; no direct need/health resets.
        for(int day=0;day<8;day++)
        {
            w.SpawnItem("meal",10,new(66,66));w.SpawnItem("water_bucket",10,new(66,67));w.SpawnItem("bandage",12,new(66,68));w.SpawnItem("splint",4,new(67,68));
            for(int tick=0;tick<1200;tick++)
            {
                var t=System.Diagnostics.Stopwatch.GetTimestamp();w.Update(.5);double ms=System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds;maxTick=Math.Max(maxTick,ms);if(tick>40)warmTicks.Add(ms);
                hungerBottom+=w.Workers.Count(r=>!r.Person.Dead&&r.Person.Hunger<=0);thirstBottom+=w.Workers.Count(r=>!r.Person.Dead&&r.Person.Thirst<=0);
                waiting+=w.Workers.Count(r=>!r.Person.Dead&&HealthCare.Needed(r.Person));
                delivered=Math.Max(delivered,w.Workers.Count(r=>r.Person.MedicalBed is not null));
            }
            w=DemoWorld.LoadJson(w.SaveJson());
            Check(w.Workers.Count==12&&w.Workers.Select(r=>r.Id).Distinct().Count()==12,"no duplicated or lost residents");
            Check(w.Workers.Where(r=>r.Person.MedicalBed is not null).Select(r=>r.Person.MedicalBed).Distinct().Count()==w.Workers.Count(r=>r.Person.MedicalBed is not null),"exclusive beds after daily save/load");
            Check(w.Items.All(s=>s.Count>0),"positive physical inventory");
        }
        Check(w.Workers.All(r=>!r.Person.Dead),"managed hospital survival");Check(delivered>=1,"actual medical delivery during long simulation");
        Check(hungerBottom==0&&thirstBottom==0,"reachable supplies prevent starvation and dehydration");
        warmTicks.Sort();
        Console.WriteLine($"HOSPITAL_LONG_OK: 12 residents / 8 days / two floors / daily archives; hunger-zero ticks={hungerBottom}, thirst-zero ticks={thirstBottom}, care-wait actor-ticks={waiting}, peak beds={delivered}; {timer.Elapsed.TotalSeconds:F1}s elapsed, warm simulation P95={warmTicks[(int)(warmTicks.Count*.95)]:F2}ms / P99={warmTicks[(int)(warmTicks.Count*.99)]:F2}ms; cold-inclusive peak={maxTick:F2}ms (not FPS)");
    }
}

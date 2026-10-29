namespace NoDwarf.Simulation;

public static class WaterSafetyChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException("Water safety: "+message);}
    private static DemoWorld Empty()
    {
        var w=new DemoWorld(flat:true);w.Jobs.Clear();w.Stockpiles.Clear();w.Items.Clear();
        foreach(var p in w.Workers){p.Person.Hunger=100;p.Person.Thirst=100;p.Person.Rest=100;foreach(var key in p.Person.WorkPriorities.Keys.ToArray())p.Person.WorkPriorities[key]=0;}
        return w;
    }
    private static void Tick(DemoWorld w,int count=100){for(int i=0;i<count;i++)w.Update(.02);}
    public static void Run()
    {
        var c=new Cell(40,40,-3);
        foreach(var d in FluidSimulation.Adjacent)
        {
            var w=Empty();w.Terrain.Excavate(c+d);w.Fluids.Add(c+d,600);
            Check(w.QueueDig(c,announce:false),"queue adjacent water excavation");var j=w.Jobs.Single();
            Check(j.WaterPaused && w.LifeAlerts.Any(a=>a.Id=="dig-water:"+j.Id),"all six faces warn and locate");
            Check(w.NaturalSolid(c),"warning never excavates terrain");
            int logs=w.Events.Count;Tick(w,12);Check(w.Events.Count==logs,"paused warning does not spam");
        }
        {
            var w=Empty();w.Fluids.AddSource(new(){Cell=c+new Cell(0,0,2),Capacity=12000,Stored=12000,Head=5,Rate=500});
            Check(w.QueueDig(c,true,announce:false),"queue tall stair pit");var j=w.Jobs.Single();
            Check(j.WaterPaused&&!w.Fluids.Sources.Single().Discovered,"stair volume warns without revealing aquifer");
            string observed=w.DigWaterRisk(j);w.Fluids.Sources.Single().Head=10;
            Check(!w.ResumeWaterDig(j.Id,observed).Ok,"stale risk confirmation rejected");
            Check(w.ResumeWaterDig(j.Id,w.DigWaterRisk(j)).Ok&&!j.WaterPaused,"explicit current risk confirmation");
            var loaded=DemoWorld.LoadJson(w.SaveJson());Check(loaded.Jobs.Single().WaterPaused,"load rechecks rather than persisting permission");
            Check(loaded.NaturalSolid(c),"loading does not excavate");
            w.Fluids.Sources.Single().Stored=0;w.Fluids.Sources.Single().Recharge=0;
            w.CancelJob(j,false);Check(!w.Jobs.Any()&&w.NaturalSolid(c),"cancel keeps terrain");
        }
        {
            var w=Empty();Check(w.QueueDig(c,announce:false),"dry job");Check(!w.Jobs.Single().WaterPaused,"dry job remains available");
            var j=w.Jobs.Single();j.Worker=0;w.Workers[0].Job=j;j.Progress=.2;
            w.Fluids.AddSource(new(){Cell=c,Capacity=3000,Stored=3000,Head=3,Rate=500});w.Update(.01);
            Check(j.WaterPaused&&j.Worker==-1&&w.Workers[0].Job is null&&j.Progress==.2,"new hazard releases assigned worker without work progress");
        }
        {
            var w=Empty();var p=w.Workers[0];var start=p.Tile;
            w.Fluids.Add(start,900);Check(!w.Walkable(start),"deep origin outside ordinary navigation");
            w.Update(.01);Check(p.WaterEvacuating&&p.Path.Count>0,"deep origin can step onto reachable shore");
            Check(!w.Connected(start,p.Path.Peek()),"ordinary pathing cannot enter deep origin");
            long mass=w.Fluids.Mass;var restored=DemoWorld.LoadJson(w.SaveJson());
            Check(restored.Workers[0].WaterEvacuating,"in-flight evacuation saved");
            Tick(restored,150);Check(!restored.Workers[0].WaterEvacuating&&restored.Fluids.Amount(restored.Workers[0].Tile)<200,"evacuation resumes to dry refuge");
            Check(restored.Fluids.Mass==mass,"escape and save preserve water");
            Tick(w,150);Check(!p.WaterEvacuating&&p.Tile!=start,"evacuation finishes without teleport");
        }
        {
            var w=Empty();var p=w.Workers[0];var start=p.Tile;
            foreach(var d in new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)})
                for(int y=0;y<3;y++){var at=start+d+new Cell(0,0,y);w.Buildings[at]=new(at,BuildKind.Cobblestone);}
            w.Fluids.Add(start,900);Tick(w,3);w.RefreshLifeAlerts();
            Check(p.WaterEvacuating&&p.Path.Count==0&&p.Tile==start&&p.WaterIssue.Contains("受困"),"sealed deep cell remains trapped, not teleported");
            Check(w.LifeAlerts.Any(a=>a.Id=="flood:"+p.Id&&a.Location==start),"trapped resident alert locates actual cell");
            var loaded=DemoWorld.LoadJson(w.SaveJson());Tick(loaded,3);Check(loaded.Workers[0].WaterEvacuating,"trapped load stable");
            for(int y=0;y<3;y++)w.Buildings.Remove(start+new Cell(1,0,y));Tick(w,250);
            Check(!p.WaterEvacuating,"opening safe shore recovers without resetting resident");
        }
        {
            var w=Empty();var p=w.Workers[0];var start=p.Tile;w.Fluids.Add(start,250);w.Update(.01);
            Check(p.WaterEvacuating&&p.Path.Count>0,"shallow water triggers before deep cutoff");
            var next=p.Path.Peek();w.Fluids.Add(next,900);w.Update(.01);
            Check(p.Tile!=next&&(!p.Path.Contains(next)),"newly flooded escape edge invalidates before movement");
            // High thirst must not steal the emergency path on the periodic needs tick.
            p.Person.Thirst=0;Tick(w,150);Check(!p.WaterEvacuating,"needs cannot lock emergency retreat");
        }
        {
            var w=Empty();var carrier=w.Workers[0];var patient=w.Workers[1];
            patient.Person.Injuries.Add(new(){Part="left_leg",Kind="missing",Severity=100,Treated=true});
            patient.Person.Injuries.Add(new(){Part="right_leg",Kind="missing",Severity=100,Treated=true});
            patient.X=carrier.X;patient.Z=carrier.Z;patient.Level=carrier.Level;
            var bed=new Cell(20,20);w.Buildings[bed]=new(bed,BuildKind.Bed);w.SetMedicalBed(bed,true);
            carrier.Person.Activity=new(){Kind="rescue_carry",Target=patient.Id,RescueBed=bed,Destination=new Cell(20,19),Origin="work"};
            w.Fluids.Add(carrier.Tile,900);w.Update(.01);
            var loaded=DemoWorld.LoadJson(w.SaveJson());Tick(loaded,150);
            Check(!loaded.Workers[0].WaterEvacuating&&loaded.Workers[1].Tile==loaded.Workers[0].Tile,"carried patient and evacuation survive in-flight save");
            Tick(w,150);
            Check(!carrier.WaterEvacuating&&patient.Tile==carrier.Tile&&w.Workers.Count==3,"carried patient reaches same refuge, no lost or duplicate resident");
        }
        {
            var w=Empty();var p=w.Workers[0];var start=p.Tile;
            var j=new DemoJob{Id=999,Cell=start+new Cell(5,0),Haul=true,Worker=0,Materials=new(){new(){Item="wood",Count=2,Stage=1}}};
            w.Jobs.Add(j);p.Job=j;w.Fluids.Add(start,900);w.Update(.01);
            Check(p.Job is null&&j.Worker==-1&&w.Total("wood")==2&&j.Materials.All(r=>r.Stage!=1),"retreat releases carried job cargo without losing or duplicating stock");
            var loaded=DemoWorld.LoadJson(w.SaveJson());Check(loaded.Total("wood")==2,"released cargo survives save");
        }
        {
            var w=Empty();var p=w.Workers[0];var start=p.Tile;
            var ledge=start+new Cell(1,0);w.Buildings[ledge]=new(ledge,BuildKind.Cobblestone);
            foreach(var d in new[]{new Cell(-1,0),new Cell(0,1),new Cell(0,-1)})
                for(int y=0;y<3;y++){var at=start+d+new Cell(0,0,y);w.Buildings[at]=new(at,BuildKind.Cobblestone);}
            w.Fluids.AddSource(new(){Cell=start+new Cell(0,0,-1),Head=1,Capacity=12000,Stored=12000,Rate=3000});w.Update(.2);
            Check(w.Fluids.Sources.Single().Discovered&&w.Fluids.Amount(start)>=200,"actual pressurized source triggers evacuation");
            Check(p.Path.Count>0&&p.Path.Peek()==ledge+new Cell(0,0,1),"escape respects actual one-metre climb geometry");
            Tick(w,160);Check(!p.WaterEvacuating&&p.Level==1,"resident actually climbs to upper shore");
        }
        Console.WriteLine("WATER_SAFETY_OK: six faces, tall digs, aquifers, pause/confirm/cancel, stale confirmation, save recheck, assigned work, shallow/deep escape, trapped/recovery, route flooding, needs and carried patient");
    }
}

using System.Numerics;
using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;
public static class EquipmentDimensionsChecks
{
    private static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("EQUIPMENT_DIMENSIONS: "+why);}
    public static void Run()
    {
        var sword=MeleeProfiles.Weapon("sword");var geometry=sword.Geometry;
        Check(Math.Abs(geometry.Length-.745)<.002&&Math.Abs(geometry.End.Z-.58)<.002,"authored full length differs from blade length");
        var longer=geometry with{Size=[geometry.Size[0],geometry.Size[1],geometry.Size[2]*1.8f]};longer.Validate();
        var longSword=sword with{Geometry=longer};
        Check(Math.Abs(longer.Scale.Z-1.8)<.0001&&longer.Scale.X==1&&longer.Scale.Y==1,"one-axis model scale");
        Check(longSword.Reach>sword.Reach+.3&&longSword.Radius==sword.Radius,"reach grows from actual trajectory, blade does not get fatter");
        var target=MeleeProfiles.Blade(longSword,.6).B;bool shortHit=false,longHit=false;
        for(int i=0;i<=90;i++)
        {
            double time=sword.Windup+sword.Active*i/90;
            var a=MeleeProfiles.Blade(sword,time);var b=MeleeProfiles.Blade(longSword,time);
            shortHit|=MeleeGeometry.Sphere(a.A,a.B,target,sword.Radius+.04f)!=null;
            longHit|=MeleeGeometry.Sphere(b.A,b.B,target,longSword.Radius+.04f)!=null;
            Check(new Vector2(b.A.X,b.A.Z).Length()<=longSword.Reach&&new Vector2(b.B.X,b.B.Z).Length()<=longSword.Reach,"derived range encloses blade");
        }
        Check(!shortHit&&longHit,"long sword contacts distant target, short sword cannot");
        var shield=EquipmentPhysicalCatalog.Get("shield");var largeShield=shield with{Size=shield.Size.Select(n=>n*2).ToArray()};
        var point=new Vector3(shield.Max.X+.1f,shield.Max.Y*.5f,0);
        Check(MeleeGeometry.Box(point,point,shield.Min,shield.Max)==null&&MeleeGeometry.Box(point,point,largeShield.Min,largeShield.Max)!=null,"shield geometry grows with model");
        var w=CombatChecks.Fixture();var actor=w.Workers[0];
        var attributes=new ItemProperties{MaterialId="steel",QualityId="fine",Durability=73,MaxDurability=100};
        var stack=w.SpawnItem("sword",20,actor.Tile,attributes);
        var ordinary=w.SpawnItem("sword",1,actor.Tile);
        Check(stack.Id!=ordinary.Id&&ordinary.EffectiveMaterial=="iron","different properties never merge; defaults from definition");
        Check(w.ActResident(new(actor.Id,"pickup",ItemId:stack.Id)).Ok,"pickup custom equipment");
        for(int i=0;i<120;i++)w.Update(1d/30);
        Check(w.Inventory(actor).Any(i=>i.Properties==attributes)&&w.Items.Any(i=>i.Owner==""&&i.Properties==attributes),"split preserves attributes");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.Items.Where(i=>i.Properties==attributes).Sum(i=>i.Count)==20,"property save roundtrip");
        var malformed=JsonNode.Parse(w.SaveJson())!;malformed["Items"]![0]!["Properties"]!["Durability"]=101;
        bool rejected=false;try{DemoWorld.LoadJson(malformed.ToJsonString());}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid durability rejected");
        w=CombatChecks.Fixture();actor=w.Workers[0];actor.Person.WorkPriorities["hauling"]=1;
        w.SpawnItem("sword",2,actor.Tile,attributes);w.SetStockpile(new(70,60),new(70,60));
        for(int i=0;i<600&&!w.Jobs.Any(j=>j.Materials.Any(m=>m.Stage==1));i++)w.Update(1d/30);
        Check(w.Jobs.Any(j=>j.Materials.Any(m=>m.Stage==1&&m.Properties==attributes)),"in-transit attributes");
        var snapshot=w.SaveJson();var cancelled=DemoWorld.LoadJson(snapshot);cancelled.CancelJob(cancelled.Jobs.First(j=>j.Materials.Any(m=>m.Stage==1)));
        Check(cancelled.Items.Where(i=>i.Properties==attributes).Sum(i=>i.Count)==2,"cancel drops same condition");
        w=DemoWorld.LoadJson(snapshot);for(int i=0;i<600;i++)w.Update(1d/30);
        Check(w.Items.Any(i=>i.Cell==new Cell(70,60)&&i.Properties==attributes&&i.Count==2),"haul delivery keeps condition");
        Console.WriteLine("EQUIPMENT_DIMENSIONS_OK: model axes, blade/derived reach, shield size, property split/merge, haul/cancel, old saves and validation");
    }
}

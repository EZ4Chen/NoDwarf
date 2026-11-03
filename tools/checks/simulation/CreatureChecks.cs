using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
namespace NoDwarf.Simulation;
public static class CreatureChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void Bad(Action action,string message){try{action();}catch(InvalidDataException){return;}throw new InvalidOperationException(message);}
    private static XDocument Definition()=>XDocument.Parse("""
        <Creatures version="1" populationLimit="16" updateBudget="2">
          <Bodies><Body id="test:body"><Part id="core" health="20" vital="true"/><Part id="foot" health="8" vital="false"/></Body></Bodies>
          <Movements><Movement id="test:ground" speed="1" stepUp="0" dropDown="0" ladders="false" maxWater="0.1"/></Movements>
          <Species><Species id="test:creature" name="框架测试生物" body="test:body" movement="test:ground" behavior="core:wander" thinkSeconds="1" limit="16"><View kind="none" scale="1"/></Species></Species>
        </Creatures>
        """);
    private sealed class Environment:ICreatureEnvironment
    {
        public bool Blocked;
        public bool CanStand(Cell c,CreatureMovementDefinition m)=>!Blocked&&DemoWorld.Inside(c);
        public bool CanTraverse(Cell a,Cell b,CreatureMovementDefinition m)=>CanStand(b,m)&&a.Level==b.Level&&Math.Abs(a.X-b.X)+Math.Abs(a.Z-b.Z)==1;
    }
    public static void Run()
    {
        Check(CreatureCatalog.Default.Species.Values.All(s=>s.Controller=="resident"),"Framework must not inject animals into new games");
        var xml=Definition();var catalog=CreatureCatalog.Parse(xml);var environment=new Environment();var population=new CreaturePopulation(catalog,environment);
        long id=population.Spawn("test:creature",new(10,10),0,55);population.Advance(1);
        Check(population.Observe(id,1).Destination is not null,"Configured wander behavior must produce a legal move");
        Check(Math.Abs(population.Observe(id,1.5).MoveFraction-.5)<1e-6,"Rendering movement uses continuous interpolation");
        var snapshot=population.Save();var loaded=new CreaturePopulation(catalog,environment);loaded.Restore(snapshot,1.5);
        population.Advance(2);loaded.Advance(2);Check(JsonSerializer.Serialize(population.Save())==JsonSerializer.Serialize(loaded.Save()),"Mid-move save must preserve random state and continuation");
        environment.Blocked=true;loaded.Advance(2.1);Check(loaded.Observe(id,2.1).Destination is null,"Dynamic blocked terrain must cancel travel, not teleport");environment.Blocked=false;
        population.Damage(id,"foot",8,2);Check(!population.Observe(id,2).Dead,"Non-vital part loss is not instant death");
        population.Damage(id,"core",20,2);Check(population.Observe(id,2).Dead&&!population.Damage(id,"core",1,2),"Death is idempotent and retained as an instance");
        var death=population.Save();loaded.Restore(death,2);Check(loaded.Observe(id,2).Dead,"Dead state must survive save/load");
        for(int i=0;i<10;i++)population.Spawn("test:creature",new(10+i,10),2,(uint)(i+1));
        population.Advance(100);Check(population.LastVisited==2&&population.LastDecisions<=2,"Population update has a fixed per-tick budget");
        var frozen=population.Save().Creatures.Select(s=>s.NextThink).ToArray();for(int i=0;i<6;i++)population.Advance(100);
        Check(population.Save().Creatures.Skip(1).All(s=>s.NextThink>=101),"Round-robin must not starve late instances");
        var altered=Definition();altered.Root!.Element("Bodies")!.Element("Body")!.Element("Part")!.SetAttributeValue("health",30);
        Bad(()=>new CreaturePopulation(CreatureCatalog.Parse(altered),environment).Restore(death,2),"Changed anatomy must not silently heal a save");
        var corrupt=loaded.Save();corrupt.Creatures[0].Health["core"]=double.NaN;Bad(()=>loaded.Restore(corrupt,2),"NaN health accepted");Check(loaded.Observe(id,2).Dead,"Failed restore must be atomic");
        var duplicate=Definition();duplicate.Root!.Element("Species")!.Add(new XElement(duplicate.Root.Element("Species")!.Element("Species")!));Bad(()=>CreatureCatalog.Parse(duplicate),"Duplicate species ID accepted");
        var behavior=Definition();behavior.Root!.Element("Species")!.Element("Species")!.SetAttributeValue("behavior","extension:test");var extension=CreatureCatalog.Parse(behavior);
        Bad(()=>new CreaturePopulation(extension,environment),"Unknown behavior must fail at registration boundary");
        var registry=CreatureBehaviorRegistry.Standard().Register("extension:test",(c,w)=>new(c.Cell+new Cell(10,0),c.RandomState));var custom=new CreaturePopulation(extension,environment,registry);long customId=custom.Spawn("test:creature",new(10,10),0);custom.Advance(1);Check(custom.Observe(customId,1).Destination is null,"Plugin decision must not bypass step validation");
        using(var unsafeXml=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY ext SYSTEM 'file:///etc/passwd'>]><Creatures>&ext;</Creatures>")))
        {try{CreatureCatalog.Load(unsafeXml);throw new Exception("External entity accepted");}catch(System.Xml.XmlException){}}
        var world=new DemoWorld(flat:true);world.DeveloperResidents(false);world.ConfigureCreatures(catalog);long worldId=world.SpawnCreature("test:creature",world.Camp+new Cell(12,12),123);world.Update(1);
        var restored=DemoWorld.LoadJson(world.SaveJson(),catalog);Check(restored.Creatures.Observe(worldId,restored.SimulationTime).Species=="test:creature","World save must include the population");
        Bad(()=>DemoWorld.LoadJson(world.SaveJson()),"Missing mod species must not be silently discarded");
        UnifiedCreatureChecks.Run();
        Console.WriteLine("CREATURE_FRAMEWORK_OK: XML/behavior extension, body damage/death, legal movement, bounded scheduling, deterministic save continuation, malformed saves, world integration");
    }
}

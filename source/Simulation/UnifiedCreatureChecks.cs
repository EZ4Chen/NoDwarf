using System.Xml.Linq;
using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;
public static class UnifiedCreatureChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException("UNIFIED_CREATURE: "+message);}
    private static void Reject(Action action,string message){try{action();}catch(InvalidDataException){return;}throw new InvalidOperationException(message);}
    private static XDocument Config()
    {
        using var stream=typeof(CreatureCatalog).Assembly.GetManifestResourceStream("NoDwarf.creatures.xml")!;
        return XDocument.Load(stream);
    }
    public static void Run()
    {
        var w=CombatChecks.Fixture();var dwarf=w.Workers[0];
        var handle=new CreatureHandle("resident",dwarf.Id);
        Check(w.AllCreatureHandles().Count()==w.Workers.Count,"residents exposed once, no shadow population");
        Check(w.Creatures.Count==0&&w.ObserveCreature(handle).Species=="base:dwarf","default dwarf uses shared catalog without duplicate state");
        Check(w.InjureCreature(handle,"left_hand","cut",20).Ok,"unified injury reaches original health pipeline");
        Check(dwarf.Person.Injuries.Count==1&&w.ObserveCreature(handle).PartIntegrity["left_hand"]==dwarf.Person.Integrity("left_hand"),"single health source");
        dwarf.X+=2;Check(w.ObserveCreature(handle).Cell==dwarf.Tile,"single position source");
        var enemy=new DemoWorker{Id="shared-species-enemy",Name="敌对矮人",X=61,Z=60};w.Enemies.Add(enemy);
        Check(w.ObserveCreature(new("resident",enemy.Id)).Role=="hostile"&&w.ObserveCreature(handle).Role=="colonist"&&enemy.Person.SpeciesId==dwarf.Person.SpeciesId,"species independent of colony affiliation");
        var loaded=DemoWorld.LoadJson(w.SaveJson());
        Check(loaded.ObserveCreature(handle).PartIntegrity["left_hand"]==w.ObserveCreature(handle).PartIntegrity["left_hand"],"unified health roundtrip");
        Check(loaded.ObserveCreature(new("resident",enemy.Id)).Role=="hostile","enemy affiliation preserved");
        var invalid=JsonNode.Parse(w.SaveJson())!;invalid["Workers"]![0]!["Person"]!["SpeciesId"]="missing:species";
        Reject(()=>DemoWorld.LoadJson(invalid.ToJsonString()),"unknown species accepted");
        var xml=Config();var species=xml.Root!.Element("Species")!.Element("Species")!;
        var variant=new XElement(species);variant.SetAttributeValue("id","test:peaceful");variant.SetAttributeValue("name","能力测试物种");variant.Element("Capabilities")!.RemoveNodes();xml.Root.Element("Species")!.Add(variant);
        var catalog=CreatureCatalog.Parse(xml);
        Check(catalog.Species["base:dwarf"].Fingerprint==CreatureCatalog.Default.Species["base:dwarf"].Fingerprint,"embedded and disk default definitions differ");
        w=CombatChecks.Fixture();dwarf=w.Workers[0];dwarf.Person.SpeciesId="test:peaceful";w.ConfigureCreatures(catalog);
        dwarf.Person.WorkPriorities["mining"]=3;
        Check(ResidentWork.Rank(dwarf.Person,"mining")==int.MaxValue&&!w.DraftResident(dwarf.Id,true).Ok,"configuration gates work and recruitment");
        Check(!w.ActResident(new(dwarf.Id,"social",w.Workers[1].Id)).Ok&&!w.ActResident(new(dwarf.Id,"research")).Ok&&!w.ActResident(new(dwarf.Id,"treat",w.Workers[1].Id)).Ok,"configuration gates manual and AI shared actions");
        Check(!w.RescueResident(dwarf.Id,w.Workers[1].Id).Ok,"configuration gates rescue");
        w.SpawnItem("sword",1,dwarf.Tile);var sword=w.Items.First(i=>i.Item=="sword");sword.Owner=dwarf.Id;sword.Slot="";
        Check(!w.ActResident(new(dwarf.Id,"equip",ItemId:sword.Id)).Ok&&sword.Slot=="","configuration gates equipment");
        var target=new DemoWorker{Id="target",Name="目标",X=dwarf.X,Z=dwarf.Z+1};w.Enemies.Add(target);
        Check(!w.BeginMelee(dwarf,target),"capability gates attack execution, not only UI");
        Check(w.ActResident(new(dwarf.Id,"rest")).Ok,"loss of work/combat does not disable basic rest");
        loaded=DemoWorld.LoadJson(w.SaveJson(),catalog);Check(loaded.Workers[0].Person.SpeciesId=="test:peaceful"&&!loaded.Workers[0].Person.Can("combat"),"custom species roundtrip");
        Reject(()=>DemoWorld.LoadJson(w.SaveJson()),"missing custom resident species silently defaulted");
        variant.Element("Capabilities")!.Add(new XElement("Capability",new XAttribute("id","combat")));
        Reject(()=>DemoWorld.LoadJson(w.SaveJson(),CreatureCatalog.Parse(xml)),"changed capabilities silently accepted for existing save");
        var badBody=Config();badBody.Root!.Element("Bodies")!.Elements("Body").First(b=>(string?)b.Attribute("id")=="base:humanoid").Element("Part")!.SetAttributeValue("health",50);
        Reject(()=>CreatureCatalog.Parse(badBody),"incompatible resident health schema accepted");
        bool duplicateRejected=false;try{w.SpawnCreature("base:dwarf",w.Camp);}catch(InvalidOperationException){duplicateRejected=true;}
        Check(duplicateRejected,"resident scheduler must not double-spawn in wildlife population");
        Console.WriteLine("UNIFIED_CREATURE_OK: species/affiliation, shared live health/position, XML capability enforcement, save roundtrip and controller isolation");
    }
}

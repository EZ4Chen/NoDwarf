using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NoDwarf.Simulation;
static void Check(bool ok,string why){if(!ok)throw new Exception("SPATIAL: "+why);}
static void Bad(Action action,string why){try{action();}catch(InvalidDataException){return;}throw new Exception("SPATIAL accepted "+why);}
static void Advance(DemoWorld w,double seconds){for(int i=0;i<seconds*30;i++)w.Update(1d/30);}
static ContentPack Defs(string xml)=>new("test","1.0",1,[],[new("test.xml","defs",XDocument.Parse(xml))]);
string root=Path.Combine(Path.GetTempPath(),"nodwarf-spatial-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root,"copper/Defs/nested"));
try
{
    File.WriteAllText(Path.Combine(root,"content.xml"),"<Content><Pack path='copper'/></Content>");
    File.WriteAllText(Path.Combine(root,"copper/pack.xml"),"<Pack id='copper' version='1.1' api='1'><Depends id='base' minVersion='1.0'/><Scan path='Defs'/></Pack>");
    foreach(string name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
    {using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;using var reader=new StreamReader(stream);File.WriteAllText(Path.Combine(root,"copper/Defs",name[7..]),reader.ReadToEnd());}
    var catalog=ContentCatalog.LoadDirectory(root);var core=ContentCatalog.Load([]);
    Check(catalog.Item("copper:ore").Info.Tags.Contains("copper")&&!catalog.ItemsById.ContainsKey("copper:resource_template"),"item template and abstract exclusion");
    Check(catalog.Building("copper:chair").Handler==BuildKind.Chair&&catalog.Building("copper:chair").Cost.Count==1&&catalog.Building("copper:chair").Cost["copper:ingot"]==2,"building inherited handler, replaced costs");
    Check(catalog.Entries.Count(e=>e.Id=="copper:ore_block")==1,"one registry entry per type, not instance");
    string extra=Path.Combine(root,"copper/Defs/nested/new.xml");
    File.WriteAllText(extra,"<Defs><Item id='copper:new' parent='copper:resource_template' name='新材料'/></Defs>");
    Check(ContentCatalog.LoadDirectory(root).Item("copper:new").Info.Tags.Contains("metal"),"new nested file discovered without manifest edit");File.Delete(extra);
    Bad(()=>ContentCatalog.Load([Defs("<Defs><Block id='test:a' parent='test:b'/><Block id='test:b' parent='test:a'/></Defs>")]),"template cycle");
    Bad(()=>ContentCatalog.Load([Defs("<Defs><Item id='test:a' parent='test:missing'/></Defs>")]),"missing parent");
    Bad(()=>ContentCatalog.Load([Defs("<Defs><Item id='test:a' abstract='true' typo='x'/></Defs>")]),"unknown unused template field");
    Bad(()=>ContentCatalog.Load([Defs("<Defs><Block id='test:a' name='bad' shape='Solid' color='ffffff'><Drop item='missing'/></Block></Defs>")]),"missing drop reference");
    Bad(()=>ContentCatalog.Load([Defs("<Defs><Building id='test:a' name='bad' block='nodwarf:stone' handler='Teleport'><Cost item='wood'/></Building></Defs>")]),"unregistered behavior");
    Bad(()=>ContentCatalog.Load([Defs("<Defs><Building id='test:a' name='bad' block='nodwarf:stone' handler='Chair'><Cost item='wood'/></Building></Defs>")]),"incompatible handler geometry");
    File.WriteAllText(extra,"<Defs><Item id='copper:ore' parent='copper:resource_template'/></Defs>");Bad(()=>ContentCatalog.LoadDirectory(root),"duplicate IDs");File.Delete(extra);
    if(!OperatingSystem.IsWindows())
    {File.CreateSymbolicLink(extra,Path.Combine(root,"copper/Defs/production.xml"));Bad(()=>ContentCatalog.LoadDirectory(root),"symlink in scan");File.Delete(extra);}
    var w=new DemoWorld(flat:true,content:catalog);w.Items.Clear();w.Stockpiles.Clear();w.Jobs.Clear();w.Workers.RemoveRange(1,w.Workers.Count-1);
    var worker=w.Workers[0];worker.Person.Hunger=90;worker.Person.Rest=90;worker.Person.Recreation=90;
    foreach(var t in ResidentWork.Types)worker.Person.WorkPriorities[t.Id]=0;
    worker.Person.WorkPriorities["mining"]=1;worker.Person.WorkPriorities["construction"]=1;worker.Person.WorkPriorities["smelting"]=1;
    var chosen=new List<TerrainBlock>();
    foreach(var b in w.Terrain.DefinitionBlocks.Where(b=>b.Cell.Level is <=-2 and >=-5&&b.Cell.X>2&&b.Cell.X<252&&b.Cell.Z>2&&b.Cell.Z<252))
        if(chosen.All(o=>Math.Abs(o.Cell.X-b.Cell.X)+Math.Abs(o.Cell.Z-b.Cell.Z)>8)){chosen.Add(b);if(chosen.Count==5)break;}
    var ore=chosen.ToArray();
    Check(ore.Length==5,"registered copper generates accessible natural ore");
    Check(w.Terrain.DefinitionBlocks.SequenceEqual(new DemoWorld(flat:true,content:catalog).Terrain.DefinitionBlocks),"generation deterministic");
    Check(!new DemoWorld(flat:true,content:core).Terrain.DefinitionBlocks.Any(),"disabled mod does not generate ore");
    for(int n=0;n<ore.Length;n++)
    {
        var c=ore[n].Cell;
        for(int dx=1;dx<=2;dx++)for(int dz=-1;dz<=1;dz++)for(int dy=0;dy<=1;dy++)w.Terrain.Excavate(c+new Cell(dx,dz,dy));
        worker.X=c.X+1;worker.Z=c.Z;worker.Level=c.Level;worker.Path.Clear();
        Check(w.QueueDig(c),"mine registered ore");
        if(n==0){Advance(w,.5);w=DemoWorld.LoadJson(w.SaveJson(),content:catalog);worker=w.Workers[0];}
        Advance(w,30);Check(!w.NaturalSolid(c)&&!w.QueueDig(c),"mined once with real worker");
    }
    Check(w.Total("copper:ore")==10,"configured drops conserved");
    // Consolidate mined stacks at the test workshop; copper is obtained exclusively from mining.
    worker.X=60;worker.Z=60;worker.Level=0;worker.Path.Clear();foreach(var i in w.Items.Where(i=>i.Owner==""))i.Cell=new(60,61);
    w.SpawnItem("cobblestone",8,new(60,61));w.SpawnItem("coal",5,new(60,61));
    Check(w.QueueBuild(new(63,60),BuildKind.Furnace),"build initial furnace");Advance(w,60);
    for(int n=0;n<4;n++){Check(w.QueueProcessing("copper:smelt"),"smelt mined ore");Advance(w,35);}
    Check(w.Total("copper:ingot")==4,"four physical copper ingots");
    var chair=new Cell(60,63);var furnace=new Cell(63,63);
    Check(w.QueueBuild(chair,"copper:chair")&&w.QueueBuild(furnace,"copper:furnace"),"new building IDs accept copper cost");
    Advance(w,1);w=DemoWorld.LoadJson(w.SaveJson(),content:catalog);worker=w.Workers[0];Advance(w,90);
    Check(w.Buildings[chair].DefinitionId=="copper:chair"&&w.BlockAt(chair)=="copper:chair_block"&&w.Total("copper:ingot")==0,"construction retains type and consumes custom material");
    Check(w.RemainingDurability(chair)==160,"custom durability uses world catalog");
    w.Buildings.Remove(new(63,60));Check(w.QueueProcessing("copper:smelt"),"mod furnace discovered by production");Advance(w,40);
    Check(w.Total("copper:ingot")==1&&w.Total("copper:ore")==0,"mod furnace completes final copper");
    w.Buildings[new(61,63)]=new(new(61,63),BuildKind.Table);worker.Person.Hunger=10;worker.X=60;worker.Z=62;
    var food=w.SpawnItem("meal",1,worker.Tile);food.Owner=worker.Id;Check(w.ActResident(new(worker.Id,"eat")).Ok,"eat near custom chair");Advance(w,20);
    Check(worker.Person.MoodEffects.Any(f=>f.Text.Contains("坐在餐桌")),"resident actually uses registered chair");
    Check(w.ObserveResident(worker.Id).Contains("copper:chair"),"AI observation contains new facility");
    w.SaveBlueprint();Check(w.Blueprint.Any(p=>p.DefinitionId=="copper:chair"),"blueprint retains definition ID");
    string saved=w.SaveJson();var loaded=DemoWorld.LoadJson(saved,content:catalog);Advance(loaded,2);
    Check(loaded.Total("copper:ingot")==1&&loaded.BlockAt(chair)=="copper:chair_block","reload does not duplicate outputs");
    Bad(()=>DemoWorld.LoadJson(saved,content:core),"missing content pack");
    File.WriteAllText(extra,"<Patches><Set domain='spatial' section='Blocks' id='copper:chair_block' field='durability' expect='160' value='200'/></Patches>");
    var changed=ContentCatalog.LoadDirectory(root);File.Delete(extra);
    Bad(()=>DemoWorld.LoadJson(saved,content:changed),"changed spatial rules");
    var other=new DemoWorld(flat:true,content:changed);other.Buildings[chair]=new(chair,BuildKind.Chair){DefinitionId="copper:chair"};
    Check(other.RemainingDurability(chair)==200&&w.RemainingDurability(chair)==160,"two worlds retain their own spatial rules");

    var tampered=JsonNode.Parse(saved)!;tampered["Buildings"]!.AsArray().First(b=>b!["DefinitionId"]!.GetValue<string>()=="copper:chair")!["Kind"]="Furnace";Bad(()=>DemoWorld.LoadJson(tampered.ToJsonString(),content:catalog),"handler tampering");
    loaded.Erase(chair);Check(loaded.Total("copper:ingot")==3,"demolition refunds actual copper cost");
    Console.WriteLine("SPATIAL_CONTENT_OK: scan/templates/references, generated copper mining to smelting/building/use, AI observation, blueprint, current save roundtrip and refunds");
}
finally{Directory.Delete(root,true);}

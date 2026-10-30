using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using NoDwarf.Simulation;
static void Check(bool ok,string why){if(!ok)throw new Exception("PLACEMENT: "+why);}
static void Bad(Action f,string why){try{f();}catch(InvalidDataException){return;}throw new Exception("PLACEMENT accepted "+why);}
static void Until(DemoWorld w,Func<bool> done,string why,int seconds=90)
{for(int i=0;i<seconds*30&&!done();i++)w.Update(1d/30);Check(done(),why+" | "+string.Join(";",w.Workers.Select(a=>a.Status))+" | "+string.Join(";",w.Events.TakeLast(4)));}
static DemoWorld World(ContentCatalog c)
{
 var w=new DemoWorld(flat:true,content:c);w.Jobs.Clear();w.Items.Clear();w.Stockpiles.Clear();w.Workers.RemoveRange(1,w.Workers.Count-1);
 var a=w.Workers[0];a.X=20;a.Z=20;a.Level=0;a.Person.Hunger=95;a.Person.Rest=95;a.Person.Recreation=95;
 foreach(var t in ResidentWork.Types)a.Person.WorkPriorities[t.Id]=0;
 a.Person.WorkPriorities["mining"]=1;a.Person.WorkPriorities["construction"]=1;return w;
}
static ContentPack Def(string xml)=>new("test","1.0",1,[],[new("defs.xml","defs",XDocument.Parse(xml))]);
string path=Path.Combine(Path.GetTempPath(),"nodwarf-placement-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(path,"copper/Defs"));
try
{
 File.WriteAllText(Path.Combine(path,"content.xml"),"<Content><Pack path='copper'/></Content>");File.WriteAllText(Path.Combine(path,"copper/pack.xml"),"<Pack id='copper' version='1.1' api='1'><Depends id='base' minVersion='1.0'/><Scan path='Defs'/></Pack>");
 foreach(string name in Assembly.GetExecutingAssembly().GetManifestResourceNames()){using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;using var reader=new StreamReader(stream);File.WriteAllText(Path.Combine(path,"copper/Defs",name[7..]),reader.ReadToEnd());}
 var core=ContentCatalog.Load([]);var catalog=ContentCatalog.LoadDirectory(path);
 Check(core.Item("sand").Placements.Single().Block=="nodwarf:sand"&&catalog.Item("copper:ingot").Placements.Count==2,"both core and scanned Mod capabilities");
 Bad(()=>ContentCatalog.Load([Def("<Defs><Item id='test:x' parent='sand'><Place block='missing'/></Item></Defs>")]),"unknown block");
 Bad(()=>ContentCatalog.Load([Def("<Defs><Item id='test:x' parent='sand'><Place block='nodwarf:bedrock'/></Item></Defs>")]),"indestructible target");
 Bad(()=>ContentCatalog.Load([Def("<Defs><Item id='test:x' parent='sand'><Place block='nodwarf:sand' count='0'/></Item></Defs>")]),"zero cost");
 Bad(()=>ContentCatalog.Load([Def("<Defs><Item id='test:x' parent='sand'><Place block='nodwarf:sand'/><Place block='nodwarf:sand'/></Item></Defs>")]),"duplicate target");
 var inherited=ContentCatalog.Load([Def("<Defs><Item id='test:x' parent='sand' name='模组沙'/></Defs>")]);Check(inherited.Item("test:x").Placements.Single().Item=="test:x","inherited capability binds child source ID");
 var safety=World(core);var occupied=safety.Workers[0].Tile;safety.SpawnItem("sand",2,new(21,20));
 Check(safety.QueuePlaceBlock(occupied,"sand","nodwarf:sand")&&safety.DeveloperFinishPlans()==0&&safety.Total("sand")==2,"never complete inside a resident");
 safety.CancelJob(safety.Jobs.Single());var wet=new Cell(22,20);safety.Fluids.Add(wet,300);
 Check(!safety.QueuePlaceBlock(wet,"sand","nodwarf:sand")&&safety.Total("sand")==2,"wet placement does not consume or erase water");
 var pendingWet=new Cell(23,20);Check(safety.QueuePlaceBlock(pendingWet,"sand","nodwarf:sand"),"dry plan first");safety.Fluids.Add(pendingWet,200);
 safety=DemoWorld.LoadJson(safety.SaveJson(),content:core);Check(safety.DeveloperFinishPlans()==0&&safety.Fluids.Amount(pendingWet)==200,"water entering a plan pauses safely across save");
 var w=World(catalog);var target=new Cell(22,20);var sand=w.SpawnItem("sand",3,new(20,21));int id=sand.Id;long nav=w.Navigation.Version;
 Check(w.QueuePlaceBlock(target,"sand","nodwarf:sand",id),"queue selected stack");Check(w.AvailableInStack(id)==2&&w.Jobs.Single().Materials.Single().StackId==id,"reservation of clicked stack");
 Check(!w.QueuePlaceBlock(target,"sand","nodwarf:sand")&&!w.QueuePlaceBlock(new(22,20,3),"sand","nodwarf:sand"),"duplicate and unsupported targets refused");
 Bad(()=>w.QueuePlaceBlock(new(23,20),"copper:ore","copper:ore_block"),"drop relation cannot authorize placement");
 Until(w,()=>w.Jobs.Single().Materials.Any(r=>r.Stage==1),"real pickup");
 w=DemoWorld.LoadJson(w.SaveJson(),content:catalog);Until(w,()=>w.Jobs.Count==0,"continue carried placement after save");
 Check(w.Terrain.Solid(target)&&w.BlockAt(target)=="nodwarf:sand"&&!w.Buildings.ContainsKey(target)&&w.Total("sand")==2,"real terrain and material conservation");
 Check(!w.Walkable(target)&&w.Walkable(target+new Cell(0,0,1))&&w.Navigation.Version>nav,"navigation invalidation and support");
 w=DemoWorld.LoadJson(w.SaveJson(),content:catalog);Check(w.BlockAt(target)=="nodwarf:sand","above-surface terrain survives load");
 Check(w.QueueDig(target),"placed block accepts same mining command");Until(w,()=>w.Jobs.Count==0,"mine placed sand");Check(w.Total("sand")==3&&!w.Terrain.Solid(target),"mine refunds configured drop exactly once");
 w=DemoWorld.LoadJson(w.SaveJson(),content:catalog);Check(!w.Terrain.Solid(target)&&w.Total("sand")==3,"removed fill stays air after load");
 // Refill excavated original terrain, then mine it again: original stone must not reappear.
 var hole=new Cell(23,20,-1);Check(w.Terrain.Excavate(hole),"prepare excavated cavity");
 Check(w.QueuePlaceBlock(hole,"sand","nodwarf:sand"),"queue below-surface refill");Until(w,()=>w.Jobs.Count==0,"fill hole");
 w=DemoWorld.LoadJson(w.SaveJson(),content:catalog);Check(w.BlockAt(hole)=="nodwarf:sand","refill beats excavated base record");
 Check(w.QueueDig(hole),"dig refilled hole");Until(w,()=>w.Jobs.Count==0,"mine refill");Check(!w.Terrain.Solid(hole)&&w.Total("sand")==3,"original terrain does not resurrect");
 var ingots=w.SpawnItem("copper:ingot",6,new(20,21));Check(w.QueuePlaceBlock(target,"copper:ingot","copper:solid_block",ingots.Id),"Mod placement by ID");
 Until(w,()=>w.Jobs.Count==0,"place Mod block");Check(w.BlockAt(target)=="copper:solid_block"&&w.RemainingDurability(target)==150&&w.Total("copper:ingot")==4,"Mod appearance/strength/cost");
 Check(w.QueuePlaceBlock(new(22,21),"copper:ingot","copper:compact_block"),"second option from same item");
 Until(w,()=>w.Jobs.Single().Materials.Any(r=>r.Stage==1),"pick up second option");
 w=DemoWorld.LoadJson(w.SaveJson(),content:catalog);w.CancelJob(w.Jobs.Single());Check(w.Total("copper:ingot")==4,"cancel carried placement returns material");
 Check(w.QueuePlaceBlock(new(22,21),"copper:ingot","copper:compact_block"),"requeue canceled option");
 var queued=w.CaptureSnapshot();queued.Jobs.Single().PlaceBlock="copper:solid_block";Bad(()=>DemoWorld.LoadSnapshot(queued,content:catalog),"tampered cost/target mismatch");
 w.CancelJob(w.Jobs.Single());
 string saved=w.SaveJson();Bad(()=>DemoWorld.LoadJson(saved,content:core),"missing Mod");
 var overlap=w.CaptureSnapshot();overlap.Buildings=[new(target,BuildKind.Wall)];Bad(()=>DemoWorld.LoadSnapshot(overlap,content:catalog),"building overlaps placed terrain");
 var duplicate=w.CaptureSnapshot();duplicate.PlacedBlocks=[..duplicate.PlacedBlocks,duplicate.PlacedBlocks[0]];Bad(()=>DemoWorld.LoadSnapshot(duplicate,content:catalog),"duplicate placed cell");
 var forged=w.CaptureSnapshot();forged.PlacedBlocks=[new(new(24,20),"copper:ore_block")];Bad(()=>DemoWorld.LoadSnapshot(forged,content:catalog),"unregistered placement target in save");
 // Mod count changes remain incompatible, even when IDs stay the same.
 var xml=Path.Combine(path,"copper/Defs/production.xml");File.WriteAllText(xml,File.ReadAllText(xml).Replace("block=\"copper:solid_block\" count=\"2\"","block=\"copper:solid_block\" count=\"3\""));
 var changed=ContentCatalog.LoadDirectory(path);Bad(()=>DemoWorld.LoadJson(saved,content:changed),"changed placement cost rejected");
 Check(catalog.Placement("copper:ingot","copper:solid_block").Count==2&&changed.Placement("copper:ingot","copper:solid_block").Count==3,"world catalog isolation");
 Console.WriteLine("PLACEMENT_OK: core/Mod scan, inherited/multiple capabilities, reservations, worker delivery, terrain refill/remine, cancellation, navigation, saves and validation");
}
finally{Directory.Delete(path,true);}

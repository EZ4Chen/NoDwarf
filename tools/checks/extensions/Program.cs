using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NoDwarf.Simulation;

if(args.Length>1&&args[0]=="--sample")Environment.SetEnvironmentVariable("NODWARF_CONTENT_ROOT",args[1]);
static void Check(bool condition,string why){if(!condition)throw new Exception("EXTENSIONS: "+why);}
static void Bad(Action action,string contains)
{try{action();}catch(InvalidDataException e){Check(e.Message.Contains(contains),"expected "+contains+", got "+e.Message);return;}throw new Exception("Accepted invalid data: "+contains);}
static void Advance(DemoWorld w,double seconds){for(int i=0;i<seconds*30;i++)w.Update(1d/30);}
static ContentPack Pack(string id,string xml,string domain="production",params ContentDependency[] deps)=>new(id,"1.0",1,deps,new[]{new ContentFile(id+".xml",domain,XDocument.Parse(xml))});
static DemoWorld Feeding()
{
    var w=CombatChecks.Fixture();var helper=w.Workers[0];var patient=w.Workers[1];patient.Person.Hunger=1;
    w.InjureResident(patient.Id,"torso","fracture",90);w.SpawnItem("meal",1,helper.Tile);
    Check(w.StartResidentTask(new(helper.Id,"base:feeding",patient.Id)).Ok,"manual task intent");return w;
}
if(args.Length>0&&args[0]=="--sample")
{
    var w=CombatChecks.Fixture();var station=new Cell(62,61);w.Buildings[station]=new(station,BuildKind.Furnace);
    w.SpawnItem("copper:ore",2,new(61,61));w.SpawnItem("coal",1,new(61,61));
    foreach(var r in w.Workers){r.Person.WorkPriorities["crafting"]=1;r.Person.WorkPriorities["smelting"]=1;}
    Check(w.QueueProcessing("copper:smelt"),"XML recipe queued");Advance(w,60);
    Check(w.Total("copper:ingot")==1&&w.Total("copper:ore")==0,"XML recipe executed");
    Check(ValueCatalog.Base("copper:ingot")==10&&w.Values(true).Items>=10,"new content valuation");
    long creature=w.SpawnCreature("copper:grazer",new(75,75));Advance(w,4);
    var loaded=DemoWorld.LoadJson(w.SaveJson());
    Check(loaded.Total("copper:ingot")==1&&loaded.Creatures.Observe(creature,loaded.SimulationTime).Species=="copper:grazer","content save roundtrip");
    Console.WriteLine("EXTENSION_SAMPLE_OK: configured items, production, valuation, species and save");return;
}
var basic=Pack("herb","""
<ProductionDefinitions><Items><Item id="herb:leaf" name="草叶" value="3" texture="nodwarf:log" color="00ff00"/></Items><Recipes><Recipe id="herb:recipe" name="草叶加工" station="Loom" output="herb:leaf" count="1" work="2"><Ingredient item="wood" count="1"/></Recipe></Recipes></ProductionDefinitions>
""");
var catalog=ContentCatalog.Load(new[]{basic});
Check(catalog.Items.Any(i=>i.Id=="herb:leaf"&&i.BaseValue==3)&&catalog.Recipes.Any(r=>r.Id=="herb:recipe"),"add definitions without code");
Check(catalog.Sources["production/Recipes/herb:recipe"].File=="herb.xml","definition provenance");
var cosmetic=Pack("rename","<Patches><Set domain='creatures' section='Species' id='base:dwarf' field='name' expect='矮人' value='山民'/></Patches>","patches");
var renamed=ContentCatalog.Load(new[]{cosmetic});
Check(renamed.GameplayHash==ContentRuntime.Current.GameplayHash&&renamed.Creatures.Species["base:dwarf"].Fingerprint==CreatureCatalog.Default.Species["base:dwarf"].Fingerprint,"cosmetic semantics stable");
var cosmeticWorld=CombatChecks.Fixture();var cosmeticSave=cosmeticWorld.SaveJson();
Check(DemoWorld.LoadJson(cosmeticSave,renamed.Creatures).Workers[0].Person.Species.Name=="山民","cosmetic restore actually succeeds");
var patch=Pack("patch","<Patches><Set domain='production' section='Items' id='herb:leaf' field='value' expect='3' value='5'/><Append domain='production' section='Recipes' id='herb:recipe'><Ingredient item='coal' count='1'/></Append></Patches>","patches",new ContentDependency("herb","1.0"));
var patched=ContentCatalog.Load(new[]{patch,basic});
Check(patched.Packs.Select(p=>p.Id).SequenceEqual(new[]{"base","herb","patch"})&&patched.Items.Single(i=>i.Id=="herb:leaf").BaseValue==5&&patched.Patches.Count==2,"dependency order and explicit patches");
Bad(()=>ContentCatalog.Load(new[]{patch}),"缺少依赖");
Bad(()=>ContentCatalog.Load(new[]{basic,basic}),"重复");
var conflict=patch with{Id="zconflict"};Bad(()=>ContentCatalog.Load(new[]{patch,basic,conflict}),"前值冲突");
Bad(()=>ContentCatalog.Load(new[]{basic with{Dependencies=new[]{new ContentDependency("missing","1.0")}}}),"缺少依赖");
Check(ContentCatalog.Load(new[]{basic with{Dependencies=new[]{new ContentDependency("missing","1.0",true)}}}).Items.Count>0,"optional missing dependency");
Bad(()=>ContentCatalog.Load(new[]{basic with{Dependencies=new[]{new ContentDependency("patch","1.0")}},patch}),"循环");
Bad(()=>ContentCatalog.Load(new[]{basic with{Api=2}}),"API");
Bad(()=>ContentCatalog.Load(new[]{Pack("bad","<ProductionDefinitions><Recipes><Recipe id='bad:recipe' name='bad' station='Loom' output='missing' count='1' work='1'><Ingredient item='wood' count='1'/></Recipe></Recipes></ProductionDefinitions>")}),"bad.xml");
Bad(()=>ContentCatalog.Load(new[]{Pack("bad","<ProductionDefinitions><Items><Item id='bad:item' name='bad' texture='x' color='ffffff'/></Items></ProductionDefinitions>")}),"value");
var disabled=Pack("disable","<Patches><Disable domain='production' section='Items' id='herb:leaf'/></Patches>","patches",new ContentDependency("herb","1.0"));Bad(()=>ContentCatalog.Load(new[]{basic,disabled}),"output");
var allowedDisable=Pack("disable","<Patches><Disable domain='production' section='Recipes' id='herb:recipe'/><Disable domain='production' section='Items' id='herb:leaf'/></Patches>","patches",new ContentDependency("herb","1.0"));Check(!ContentCatalog.Load(new[]{basic,allowedDisable}).Items.Any(i=>i.Id=="herb:leaf"),"explicit disable unused definition");
bool immutable=false;try{((IList<ItemDefinition>)catalog.Items)[0]=catalog.Items[0];}catch(NotSupportedException){immutable=true;}Check(immutable,"runtime catalog immutable");

var world=Feeding();string taskId=world.Workers[0].Person.Activity!.TaskId;
Check(taskId!=""&&world.Available("meal")==0,"task reserves public meal");
world=DemoWorld.LoadJson(world.SaveJson());Check(world.Workers[0].Person.Activity!.TaskId==taskId,"pickup phase identity restored");
for(int i=0;i<600&&world.Workers[0].Person.Activity?.Kind!="feed";i++)world.Update(1d/30);
Check(world.Workers[0].Person.Activity?.Kind=="feed"&&world.Workers[0].Person.Activity!.TaskId==taskId,"same task through pickup/delivery");
world=DemoWorld.LoadJson(world.SaveJson());Advance(world,20);
Check(world.Total("meal")==0&&world.Workers[1].Person.Hunger>30&&world.TaskReceipts.Count(r=>r.Id==taskId&&r.Outcome=="completed")==1,"resume consumes once with receipt");
var fact=world.DomainEvents.Recent.Single(e=>e.Type=="resident.fed");
Check(fact.Scope=="participants"&&world.Workers[1].Person.Memories.Any(m=>m.EventId==fact.EventId)&&!world.Workers[2].Person.Memories.Any(m=>m.EventId==fact.EventId),"participant memory privacy");
int memories=world.Workers[1].Person.Memories.Sum(m=>m.Count);world.DomainEvents.Dispatch();world=DemoWorld.LoadJson(world.SaveJson());world.DomainEvents.Dispatch();
Check(world.Workers[1].Person.Memories.Sum(m=>m.Count)==memories,"event delivery dedup across reload");
world=Feeding();world.ActResident(new(world.Workers[0].Id,"cancel"));Check(world.Available("meal")==1&&world.TaskReceipts.Last().Outcome=="cancelled","cancel releases reservation");
world=Feeding();for(int i=0;i<600&&world.Workers[0].Person.Activity?.Kind!="feed";i++)world.Update(1d/30);
world.Workers[1].Person.Blood=0;Advance(world,.2);Check(world.Total("meal")==1&&world.Workers[0].Person.Activity is null&&world.TaskReceipts.Last().Outcome=="cancelled","target death retains carried ration");
world=Feeding();for(int i=0;i<600&&world.Workers[0].Person.Activity?.Kind!="feed";i++)world.Update(1d/30);
world.ActResident(new(world.Workers[0].Id,"cancel"));Check(world.Items.Any(i=>i.Item=="meal"&&i.Owner==world.Workers[0].Id)&&world.Total("meal")==1,"manual interruption retains inventory");
world=Feeding();world.Items.Clear();Advance(world,.2);Check(world.Total("meal")==0&&world.Workers[0].Person.Activity is null,"missing source does not create food");
world=Feeding();var a=world.Workers[0];a.Path.Clear();a.X=80;a.Z=80;Advance(world,.1);Check(a.Person.Activity is null&&world.Total("meal")==1,"route termination cancels without teleport or consumption");

// Care subscribers are independent, ordered, failure-isolated, and commands wait until next tick.
world=CombatChecks.Fixture();var medic=world.Workers[0];var patient=world.Workers[1];medic.X=58;medic.Z=60;
world.InjureResident(patient.Id,"left_arm","cut",20);world.SpawnItem("bandage",1,medic.Tile).Owner=medic.Id;
int seen=0;world.DomainEvents.Subscribe("example:journal",f=>{seen++;return Array.Empty<ResidentCommand>();});
world.DomainEvents.Subscribe("example:broken",f=>throw new Exception("journal offline"));
Check(world.ActResident(new(medic.Id,"treat",patient.Id)).Ok,"real medical task");Advance(world,20);
Check(seen==1&&world.DomainEvents.Errors.Count==1&&patient.Person.Relations.Single(r=>r.Target==medic.Id).Opinion==8,"committed care updates memory/relations once despite optional listener failure");
world=DemoWorld.LoadJson(world.SaveJson());world.DomainEvents.Dispatch();Check(world.Workers[1].Person.Relations.Single(r=>r.Target==medic.Id).Opinion==8,"care relation replay idempotence");
world=Feeding();var actorId=world.Workers[0].Id;
world.DomainEvents.Subscribe("example:followup",f=>new[]{new ResidentCommand(actorId,"relax")});
for(int i=0;i<600&&world.DomainEvents.Recent.Count==0;i++)world.Update(1d/30);
Check(world.Workers[0].Person.Activity is null,"event command not executed recursively");world=DemoWorld.LoadJson(world.SaveJson());world.Update(.01);Check(world.Workers[0].Person.Activity?.Kind=="relax","queued event command survives save and revalidates next tick");

var save=JsonNode.Parse(world.SaveJson())!.AsObject();save["Content"]!["Packs"]!.AsArray().Add(JsonSerializer.SerializeToNode(new ContentPackStamp("missing","1.0")));
Bad(()=>DemoWorld.LoadJson(save.ToJsonString()),"缺少存档所需内容包");
save=JsonNode.Parse(world.SaveJson())!.AsObject();save["Content"]!["GameplayHash"]=new string('0',64);Bad(()=>DemoWorld.LoadJson(save.ToJsonString()),"配置不一致");
int restoredCounter=0;var registry=WorldModuleRegistry.Standard().Register(new("example:counter",2,_=>JsonSerializer.SerializeToElement(new{Count=4}),(_,data)=>restoredCounter=data.GetProperty("Count").GetInt32()));
save=JsonNode.Parse(world.SaveJson())!.AsObject();save["Modules"]!.AsArray().Add(JsonSerializer.SerializeToNode(new ModuleArchive("example:counter",2,JsonSerializer.SerializeToElement(new{Count=7}))));
Bad(()=>DemoWorld.LoadJson(save.ToJsonString()),"缺少存档模块");var moduleWorld=DemoWorld.LoadJson(save.ToJsonString(),modules:registry);
Check(restoredCounter==7&&JsonNode.Parse(moduleWorld.SaveJson())!["Modules"]!.AsArray().Last()!["Version"]!.GetValue<int>()==2,"current module roundtrip");
foreach(int version in new[]{1,3}){save["Modules"]!.AsArray().Last()!["Version"]=version;Bad(()=>DemoWorld.LoadJson(save.ToJsonString(),modules:registry),"模块版本不兼容");}

// A separately registered task can finish and resume without touching the activity switch.
var customTask=new ResidentTaskHandler("example:pause",new[]{"example:pause"},
    (w,r,i)=>{r.Person.Activity=new(){Kind="example:pause",Origin=i.Origin,Destination=r.Tile};return true;},
    (w,r,a)=>!r.Person.Dead,
    (DemoWorld w,DemoWorker r,ResidentActivity a,double dt,ref int budget)=>{a.Progress+=dt;return a.Progress>=1?ResidentTaskStep.Completed:ResidentTaskStep.Running;},
    (w,r,a,reason)=>{});
var customModules=WorldModuleRegistry.Standard().Register(new("example:task",1,_=>JsonSerializer.SerializeToElement(1),(w,j)=>w.TaskRegistry.Register(customTask)));
world=CombatChecks.Fixture();world.TaskRegistry.Register(customTask);
world.Modules.Register(new("example:task",1,_=>JsonSerializer.SerializeToElement(1),(w,j)=>{}));
Check(world.StartResidentTask(new(world.Workers[0].Id,"example:pause","")).Ok,"registered task starts");
world.Update(.4);world=DemoWorld.LoadJson(world.SaveJson(),modules:customModules);world.Update(.7);
Check(world.Workers[0].Person.Activity is null&&world.TaskReceipts.Single().Outcome=="completed","registered task resumes and completes through contract");
world=Feeding();for(int i=0;i<600&&world.Workers[0].Person.Activity?.Kind!="feed";i++)world.Update(1d/30);
for(int x=55;x<=59;x++)for(int z=58;z<=62;z++)if(x==55||x==59||z==58||z==62)
    for(int y=0;y<3;y++){var c=new Cell(x,z,y);world.Buildings[c]=new(c,BuildKind.Wall);}
Advance(world,12);Check(world.Total("meal")==1&&world.Workers[0].Person.Activity is null,"real mid-delivery wall change cancels while retaining ration");
var stream=new DomainEventStream();int deliveries=0;stream.Subscribe("test:count",e=>{deliveries++;return Array.Empty<ResidentCommand>();});
for(int i=0;i<300;i++)stream.Commit("resident.fed",i,"a","b","meal:1");stream.Dispatch();
Check(deliveries==300&&stream.Recent.Count==256,"bounded event history does not drop pending delivery");
var eventSave=stream.Save();stream.Restore(eventSave);stream.Dispatch();Check(deliveries==300,"evicted history does not reset delivery watermark");
Check(ValueCatalog.Base("rot")==0&&ValueCatalog.Base("soil")==.2&&ValueCatalog.Base("wood")==2&&ValueCatalog.Base("meal")==6&&ValueCatalog.Base("sword")==65,"balance preserved after moving values to XML");
ItemEventChecks.Run();
ResidentLivingChecks.Run();CreatureChecks.Run();ProductionBillChecks.Run();
Console.WriteLine("EXTENSIONS_OK: packs, explicit patches, definition validation, feeding lifecycle, committed events and current module roundtrip");

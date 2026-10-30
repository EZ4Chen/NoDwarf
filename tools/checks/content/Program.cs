using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NoDwarf.Simulation;

// Explicitly supplied catalogs must work even when the ambient startup catalog is unavailable.
Environment.SetEnvironmentVariable("NODWARF_CONTENT_ROOT",Path.Combine(Path.GetTempPath(),"nodwarf-absent-"+Guid.NewGuid().ToString("N")));

static void Check(bool ok,string why){if(!ok)throw new Exception("CONTENT: "+why);}
static void Reject(Action action,string part)
{try{action();}catch(InvalidDataException e){Check(e.Message.Contains(part),"expected "+part+", got "+e.Message);return;}throw new Exception("Accepted invalid content: "+part);}
static ContentPack Pack(string id,string xml,string domain="patches",string version="1.0",params ContentDependency[] dependencies)=>new(id,version,1,dependencies,new[]{new ContentFile(id+".xml",domain,XDocument.Parse(xml))});
static DemoWorld World(ContentCatalog content)
{
    var w=new DemoWorld(flat:true,content:content);w.Items.Clear();w.Jobs.Clear();w.Stockpiles.Clear();w.StorageZones.Clear();
    for(int i=0;i<w.Workers.Count;i++){var actor=w.Workers[i];actor.X=60-i;actor.Z=60;actor.Level=0;foreach(var type in ResidentWork.Types)actor.Person.WorkPriorities[type.Id]=0;actor.Person.WorkPriorities["crafting"]=1;}
    return w;
}
static void FinishCloth(DemoWorld w,int count)
{for(int i=0;i<4000&&w.Total("cloth")<count;i++)w.Update(1d/30);Check(w.Total("cloth")==count,"in-flight output mismatch");}
static JsonObject Node(string json)=>JsonNode.Parse(json)!.AsObject();
var core=ContentCatalog.Load([]);
Console.WriteLine("CONTENT_BASELINE: "+core.GameplayHash);
var cosmetic=Pack("appearance","<Patches><Set domain='creatures' section='Species' id='base:dwarf' field='name' expect='矮人' value='山民'/><Set domain='production' section='Items' id='cloth' field='name' expect='布料' value='新布料'/></Patches>");
// Read the original name from the catalog: localization is not a fixture invariant.
cosmetic.Files[0].Document.Root!.Elements().Last().SetAttributeValue("expect",core.Item("cloth").Name);
var renamed=ContentCatalog.Load([cosmetic]);
Check(core.GameplayHash==renamed.GameplayHash&&core.Revision!=renamed.Revision,"presentation changes must not invalidate gameplay");
var w=World(core);w.InjureResident(w.Workers[0].Id,"left_arm","cut",20);string saved=w.SaveJson();
var restored=DemoWorld.LoadJson(saved,content:renamed);
Check(restored.Workers[0].Person.Species.Name=="山民"&&restored.Workers[0].Person.Injuries.Count==w.Workers[0].Person.Injuries.Count,"cosmetic load preserves injuries");
Check(ReferenceEquals(restored.Content,renamed)&&w.Workers[0].Person.Species.Name=="矮人","worlds must not share mutable catalog binding");

var price=Pack("balance","<Patches><Set domain='production' section='Items' id='cloth' field='value' expect='7' value='70'/></Patches>");
var priced=ContentCatalog.Load([price]);var other=World(priced);
w.Items.Clear();w.SpawnItem("cloth",2,w.Camp);other.SpawnItem("cloth",2,other.Camp);
Check(w.Values(true).Items==14&&other.Values(true).Items==140,"valuation uses the owning world catalog");
Check(core.Item("cloth").BaseValue==7,"a second catalog mutated the first");

// Production jobs carry the exact recipe they reserved, including input/output/work.
w=World(core);w.Buildings[new(64,60)]=new(new(64,60),BuildKind.Loom);w.SpawnItem("cotton",4,new(60,61));
Check(w.QueueProcessing("cloth"),"weaving starts");saved=w.SaveJson();
var yieldPatch=Pack("yield","<Patches><Set domain='production' section='Recipes' id='cloth' field='count' expect='2' value='20'/></Patches>");
var higherYield=ContentCatalog.Load([yieldPatch]);
Reject(()=>DemoWorld.LoadJson(saved,content:higherYield),"配置不一致");
restored=DemoWorld.LoadJson(saved,content:core);
Check(restored.PendingOutput("cloth")==2,"restored batch changed output");FinishCloth(restored,2);
var fresh=World(higherYield);fresh.Buildings[new(64,60)]=new(new(64,60),BuildKind.Loom);fresh.SpawnItem("cotton",4,new(60,61));
Check(fresh.QueueProcessing("cloth"),"current recipe queues");FinishCloth(fresh,20);
var corrupt=Node(saved);corrupt["Jobs"]![0]!["RecipeSnapshot"]!["Count"]=99;
Reject(()=>DemoWorld.LoadJson(corrupt.ToJsonString(),content:core),"配方版本");
Check(Node(saved)["Jobs"]![0]!["RecipeSnapshot"]!["Count"]!.GetValue<int>()==2,"load mutated input JSON");
var nullMaterial=Node(saved);nullMaterial["Jobs"]![0]!["Materials"]![0]=null;
Reject(()=>DemoWorld.LoadJson(nullMaterial.ToJsonString(),content:core),"材料");
var nullOutput=Node(saved);nullOutput["Jobs"]![0]!["RecipeSnapshot"]!["Output"]=null;
Reject(()=>DemoWorld.LoadJson(nullOutput.ToJsonString(),content:core),"配方快照");

// An unsupported format must fail before restoring or mutating world data.
foreach(int version in new[]{WorldSnapshot.CurrentVersion-1,WorldSnapshot.CurrentVersion+1})
{
    var old=Node(saved);old["Version"]=version;
    Reject(()=>DemoWorld.LoadJson(old.ToJsonString(),content:core),"存档版本");
}
var missingVersion=Node(saved);missingVersion.Remove("Version");
bool missingRejected=false;try{DemoWorld.LoadJson(missingVersion.ToJsonString(),content:core);}catch(JsonException){missingRejected=true;}
Check(missingRejected,"missing format version accepted");
Console.WriteLine("CURRENT_FORMAT_ONLY_OK: current roundtrip, mismatched and missing versions rejected");

// Definition stamps are required evidence, even if the top-level hash was left unchanged.
foreach(bool empty in new[]{false,true})
{
    var malformed=Node(saved);
    if(empty)malformed["Content"]!["Definitions"]=new JsonArray();
    else malformed["Content"]!["Definitions"]![0]!["Hash"]=new string('0',64);
    Reject(()=>DemoWorld.LoadJson(malformed.ToJsonString(),content:core),"定义修订清单");
}
var unknownSchema=Node(saved);unknownSchema["Content"]!["Schema"]=2;
Reject(()=>DemoWorld.LoadJson(unknownSchema.ToJsonString(),content:core),"内容包清单");

// Namespaced content, dependencies, explicit conflicts and canonical numeric formatting.
var item=Pack("ore","<ProductionDefinitions><Items><Item id='ore:raw' name='矿' texture='nodwarf:log' color='ffffff' value='4'/></Items></ProductionDefinitions>","production");
var added=ContentCatalog.Load([item]);var modWorld=World(added);modWorld.SpawnItem("ore:raw",3,modWorld.Camp);
Check(modWorld.Values(true).Items==12,"new item missing valuation");
Check(DemoWorld.LoadJson(modWorld.SaveJson(),content:added).Total("ore:raw")==3,"mod item save roundtrip");
modWorld.Stockpiles.Add(modWorld.Camp);
var modZone=modWorld.EditableStorageAt(modWorld.Camp)!;
Check(modZone.Allowed.Contains("ore:raw")&&modWorld.ConfigureStorage(modZone.Id,4,new[]{"ore:raw"}),"storage uses global rather than world item definitions");
Check(DemoWorld.LoadJson(modWorld.SaveJson(),content:added).StorageAt(modWorld.Camp)!.Allowed.SetEquals(new[]{"ore:raw"}),"mod storage filter changed after loading");
// Isolate item-definition mismatch from storage filters.
modWorld.StorageZones.Clear();modWorld.Stockpiles.Clear();
Reject(()=>DemoWorld.LoadJson(modWorld.SaveJson(),content:core),"缺少存档所需内容包");
var renamedItem=item with {Version="2.0",Files=new[]{new ContentFile("ore.xml","production",XDocument.Parse(item.Files[0].Document.ToString().Replace("ore:raw","ore:refined")))}};
var newItems=ContentCatalog.Load([renamedItem]);
Reject(()=>DemoWorld.LoadJson(modWorld.SaveJson(),content:newItems),"配置不一致");
var modRecipe=Pack("smelting","<ProductionDefinitions><Recipes><Recipe id='smelting:ore' name='矿物冶炼' station='Furnace' output='ore:raw' count='1' work='1'><Ingredient item='wood' count='1'/></Recipe></Recipes></ProductionDefinitions>","production","1.0",new ContentDependency("ore","1.0"));
var recipeWorld=World(ContentCatalog.Load([item,modRecipe]));recipeWorld.Buildings[new(64,60)]=new(new(64,60),BuildKind.Furnace);recipeWorld.SpawnItem("wood",1,new(60,61));
Check(recipeWorld.QueueProcessing("smelting:ore")&&ResidentWork.JobType(recipeWorld.Jobs[0])=="smelting","new recipe uses global work category");
recipeWorld.Jobs.Clear();
foreach(var actor in recipeWorld.Workers){actor.Person.WorkPriorities["crafting"]=0;actor.Person.WorkPriorities["smelting"]=1;}
for(int i=0;i<4000&&recipeWorld.Total("ore:raw")==0;i++)recipeWorld.Update(1d/30);
Check(recipeWorld.Total("ore:raw")==1,"automatic production cannot schedule a new recipe for its specialized workers");
Reject(()=>ContentCatalog.Load([item,item]),"重复");
Reject(()=>ContentCatalog.Load([item with{Dependencies=new[]{new ContentDependency("missing","1.0")}}]),"缺少依赖");
Reject(()=>ContentCatalog.Load([item with{Dependencies=new[]{new ContentDependency("base","1.0"){MaxVersionExclusive="1.0"}}}]),"兼容范围");
Reject(()=>ContentCatalog.Load([item with{Dependencies=new[]{new ContentDependency("absent","1.0",true){MaxVersionExclusive="bad"}}}]),"兼容范围");
var firstPatch=Pack("z_first","<Patches><Set domain='production' section='Items' id='cloth' field='name' expect='"+core.Item("cloth").Name+"' value='first'/></Patches>");
var lastPatch=Pack("a_last","<Patches><Set domain='production' section='Items' id='cloth' field='name' expect='first' value='last'/></Patches>");
Check(ContentCatalog.Load([firstPatch,lastPatch]).Item("cloth").Name=="last","explicit load order was alphabetized");
Check(ContentCatalog.Load([lastPatch with{Dependencies=new[]{new ContentDependency("z_first","1.0")}},firstPatch]).Item("cloth").Name=="last","dependency did not precede dependent patch");
Reject(()=>ContentCatalog.Load([firstPatch with{Dependencies=new[]{new ContentDependency("a_last","1.0")}},lastPatch with{Dependencies=new[]{new ContentDependency("z_first","1.0")}}]),"循环");
var numeric=Pack("format","<Patches><Set domain='production' section='Recipes' id='cloth' field='count' expect='2' value='02'/></Patches>");
Check(ContentCatalog.Load([numeric]).GameplayHash==core.GameplayHash,"numeric formatting changed semantic hash");
var typo=Pack("typo","<ProductionDefinitions><Items><Item id='typo:raw' name='矿' texture='nodwarf:log' color='ffffff' value='4'><Mystery/></Item></Items></ProductionDefinitions>","production");
Reject(()=>ContentCatalog.Load([typo]),"子项");
Reject(()=>ContentCatalog.Load([Pack("typo","<ProductionDefinitions typo='1'/>","production")]),"字段");
Reject(()=>ContentCatalog.Load([Pack("typo","<Patches><Set domain='production' section='Items' id='cloth' field='value' expect='7' value='9' typo='1'/></Patches>")]),"字段");
var future=Node(saved);future["Version"]=999;
Reject(()=>DemoWorld.LoadJson(future.ToJsonString(),content:higherYield),"存档版本");

// Directory loading is independent of cwd and publication is transactional.
string folder=Path.Combine(Path.GetTempPath(),"nodwarf-content-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(folder,"ore"));
try
{
    File.WriteAllText(Path.Combine(folder,"content.xml"),"<Content><Pack path='ore'/></Content>");
    File.WriteAllText(Path.Combine(folder,"ore/pack.xml"),"<Pack id='ore' version='1.0' api='1'><Depends id='base' minVersion='1.0' maxVersionExclusive='2.0'/><Definitions domain='production' file='items.xml'/></Pack>");
    File.WriteAllText(Path.Combine(folder,"ore/items.xml"),item.Files[0].Document.ToString());
    var store=new ContentCatalogStore(core);Check(store.TryLoadDirectory(folder,out _),"directory load failed");
    var published=store.Current;var live=World(published);
    File.WriteAllText(Path.Combine(folder,"ore/items.xml"),"<broken>");
    Check(!store.TryLoadDirectory(folder,out var error)&&error.Contains("items.xml")&&ReferenceEquals(store.Current,published)&&ReferenceEquals(live.Content,published),"failed reload changed published/active catalog");
    File.WriteAllText(Path.Combine(folder,"content.xml"),"<Content><Pack path='../outside'/></Content>");
    Check(!store.TryLoadDirectory(folder,out error)&&error.Contains("越出"),"directory traversal accepted");
    File.WriteAllText(Path.Combine(folder,"content.xml"),"<Content><Pack path='ore'/></Content>");
    File.WriteAllText(Path.Combine(folder,"ore/items.xml"),"<!DOCTYPE ProductionDefinitions [<!ENTITY external SYSTEM 'file:///never-read'>]><ProductionDefinitions><Items>&external;</Items></ProductionDefinitions>");
    Check(!store.TryLoadDirectory(folder,out error)&&error.Contains("items.xml"),"DTD was accepted");
    if(!OperatingSystem.IsWindows())
    {
        File.WriteAllText(Path.Combine(folder,"outside.xml"),item.Files[0].Document.ToString());
        File.Delete(Path.Combine(folder,"ore/items.xml"));
        File.CreateSymbolicLink(Path.Combine(folder,"ore/items.xml"),Path.Combine(folder,"outside.xml"));
        Check(!store.TryLoadDirectory(folder,out error)&&error.Contains("符号链接"),"symlink crossed a pack boundary");
        File.Delete(Path.Combine(folder,"ore/items.xml"));
    }
    Check(ReferenceEquals(store.Current,published),"invalid XML/path changed the last usable catalog");
}
finally{Directory.Delete(folder,true);}
Console.WriteLine("CONTENT_VALIDATION_OK: isolated worlds, immutable catalogs, mod loading, transactional publication, current saves, recipe revisions and changed-content rejection");

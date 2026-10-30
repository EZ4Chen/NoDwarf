using System.Diagnostics;
using System.Text.Json;
using NoDwarf.Simulation;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static string Json<T>(T value)=>JsonSerializer.Serialize(value,DemoWorld.SaveOptions);
await SaveService.Ready;
var w=new DemoWorld(flat:true);
var snapshot=w.CaptureSnapshot();
Check(Json(snapshot)==w.SaveJson(),"Detached snapshot must preserve every serialized field");
var original=Json(snapshot);
w.Workers[0].Person.Traits.Add("snapshot-test");w.Workers[0].Person.SkillXp["mining"]=98765;
w.Workers[0].Combat.Target="changed";w.Items[0].Count++;
w.Story.Pending.Add(new(){Id="snapshot-test"});w.ResidentMind.History.Add(new("test","test",0,"test","test","test",["test"]));
w.Update(.01);
Check(Json(snapshot)==original,"Live mutations leaked into detached archive");
var restored=DemoWorld.LoadSnapshot(snapshot);
Check(!restored.Workers[0].Person.Traits.Contains("snapshot-test"),"Detached archive restored new rather than captured state");

// Mutate nested fields that may be absent in a fresh colony, including Mod JSON.
using(var document=JsonDocument.Parse("{\"mod\":[1,2]}"))
{
    var deep=new WorldSnapshot{
        Jobs=[new(){Materials=[new(){Count=3}],RecipeSnapshot=new("test","test",BuildKind.Loom,new Dictionary<string,int>{{"cotton",4}},"cloth",2)}],
        Story=new(){Turn=new(){Messages=[new(){Content="before"}],Receipts=[new(){Result="before"}]},Pending=[new(){Count=4}]},
        ResidentMind=new(){Turn=new(){MemoryIds=["before"],Choices=[new("a","b","c","d",1)]},History=[new("a","b",1,"c","d","e",["before"])]},
        Modules=[new("test",1,document.RootElement)],StorageZones=[new(){Cells=[new(1,2)],Allowed=["wood"]}]
    };
    var copied=SnapshotCopy.Copy(deep);string before=Json(copied);
    deep.Jobs[0].Materials[0].Count=99;((Dictionary<string,int>)deep.Jobs[0].RecipeSnapshot!.Inputs)["cotton"]=99;
    deep.Story.Turn!.Messages[0].Content="after";deep.Story.Turn.Receipts[0].Result="after";deep.Story.Pending.Clear();
    deep.ResidentMind.Turn!.MemoryIds[0]="after";deep.ResidentMind.History[0].Memories[0]="after";deep.StorageZones[0].Cells.Clear();
    document.Dispose();Check(Json(copied)==before,"Nested DTO or JsonDocument ownership was shared");
}

string root=Path.Combine(Path.GetTempPath(),"nodwarf-save-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
    string path=Path.Combine(root,"session.json");var service=new SaveService();
    var first=service.Write(path,new{Sequence=1});var second=service.Write(path,new{Sequence=2});
    await Task.WhenAll(first,second);
    Check(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("Sequence").GetInt32()==2,"Writes completed out of order");
    Check(JsonDocument.Parse(File.ReadAllText(path+".bak")).RootElement.GetProperty("Sequence").GetInt32()==1,"Previous committed save was not backed up");
    string previous=File.ReadAllText(path);
    try{SaveFiles.Write(path,stream=>{stream.WriteByte(1);throw new IOException("injected write failure");});throw new Exception("Expected write failure");}catch(IOException){}
    Check(File.ReadAllText(path)==previous&&!Directory.EnumerateFiles(root,"*.tmp").Any(),"Failed write damaged the save or leaked temporary files");
    string blocked=Path.Combine(root,"directory");Directory.CreateDirectory(blocked);
    try{await service.Write(blocked,new{Sequence=3});throw new Exception("Expected commit failure");}catch(IOException){}catch(UnauthorizedAccessException){}
    await service.Write(path,new{Sequence=4});Check(File.ReadAllText(path).Contains('4'),"Failed write poisoned later saves");
    var released=EnqueueEphemeral(service,path);await service.Drain();
    for(int i=0;i<3;i++){GC.Collect();GC.WaitForPendingFinalizers();}
    Check(!released.IsAlive,"Completed receipts retain a snapshot graph");

    // Same data and formatting, warm median of 7 runs. Baseline reproduces the previous two-pass encoder, not a format compatibility path.
    var large=new RealtimeStressScenario().World;
    for(int i=0;i<20000;i++)large.Items.Add(new(){Id=100000+i,Item="wood",Count=1,Cell=large.Camp});
    object BaselineDoubleEncoding()=>Json(new{Version=1,World=large.SaveJson()});
    object Capture()=>large.CaptureSnapshot();
    static (double ms,long bytes) Measure(Func<object> action)
    {
        action();var runs=new List<(double ms,long bytes)>();
        for(int i=0;i<7;i++){long b=GC.GetAllocatedBytesForCurrentThread();long t=Stopwatch.GetTimestamp();GC.KeepAlive(action());runs.Add((Stopwatch.GetElapsedTime(t).TotalMilliseconds,GC.GetAllocatedBytesForCurrentThread()-b));}
        return runs.OrderBy(r=>r.ms).ElementAt(3);
    }
    var old=Measure(BaselineDoubleEncoding);var now=Measure(Capture);
    var state=large.CaptureSnapshot();long start=Stopwatch.GetTimestamp();await service.Write(path,new{Version=2,WorldState=state});double write=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    Check(now.bytes<old.bytes,"Snapshot did not reduce main-thread allocation");
    Console.WriteLine($"SAVE_MEASURE: residents={large.Workers.Count} items={large.Items.Count} legacy_main_ms={old.ms:F3} legacy_main_bytes={old.bytes} snapshot_main_ms={now.ms:F3} snapshot_main_bytes={now.bytes} background_encode_commit_ms={write:F3}");
}
finally{Directory.Delete(root,true);}

var items=new ItemCollection();var loose=new ItemStack{Item="wood"};items.Add(loose);long version=items.EquipmentVersion;
loose.Count++;loose.Cell=new(1,2);loose.Owner="resident";loose.Item="meal";
Check(items.EquipmentVersion==version,"Loose inventory invalidated equipment");
loose.Slot="main";Check(items.EquipmentVersion>version&&items.EquippedItems.Single()==loose,"Equipping missed invalidation/index");version=items.EquipmentVersion;
loose.Item="sword";Check(items.EquipmentVersion>version,"Changing equipped item missed invalidation");version=items.EquipmentVersion;
loose.Owner="other";Check(items.EquipmentVersion>version,"Transfer missed invalidation");version=items.EquipmentVersion;
loose.Slot="";Check(items.EquipmentVersion>version&&!items.EquippedItems.Any(),"Unequipping missed invalidation/index");
items[0]=new(){Owner="resident",Slot="off",Item="shield"};version=items.EquipmentVersion;
items.RemoveAt(0);Check(items.EquipmentVersion>version,"Removal missed invalidation");version=items.EquipmentVersion;
loose.Slot="off";Check(items.EquipmentVersion==version,"Removed item retained subscription");
items.Add(loose);items.Add(loose);items.Remove(loose);version=items.EquipmentVersion;loose.Item="shield";
Check(items.EquipmentVersion>version,"Duplicate membership lost subscription");items.Clear();version=items.EquipmentVersion;loose.Item="sword";
Check(items.EquipmentVersion==version&&!items.EquippedItems.Any(),"Clear retained subscription/index");
Console.WriteLine("SAVE_SERVICE_OK: detached graphs, ordered commits, backups, encode/write failures and queue recovery");
Console.WriteLine("EQUIPMENT_VERSION_OK: loose inventory, equip, transfer, replacement, removal and subscription cleanup");

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static WeakReference EnqueueEphemeral(SaveService service,string path)
{
    var value=new WorkerState{Name="temporary",Path=[new(1,2)]};
    var reference=new WeakReference(value);_=service.Write(path,value);return reference;
}

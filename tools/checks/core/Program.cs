using System.Diagnostics;
using System.Reflection;
using NoDwarf.Collections;
using NoDwarf.Simulation;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
var discarded=new List<string>();
var cache=new ByteBudgetCache<string,string>(20,discarded.Add);
string Acquire(string key)=>cache.Acquire(key,()=>(key,10));
Acquire("a");cache.Release("a");Acquire("b");cache.Release("b");Acquire("a");cache.Release("a");
Acquire("c");cache.Release("c");
Check(discarded.SequenceEqual(new[]{"b"}),"LRU must evict only the oldest unused entry");
Acquire("a");Acquire("a");cache.Budget=0;
Check(cache.Bytes==10&&discarded.SequenceEqual(new[]{"b","c"}),"Pinned resources survive pressure");
cache.Release("a");Check(cache.Bytes==10,"Second lease still owns resource");cache.Release("a");
Check(cache.Bytes==0&&discarded.Count==3,"Last lease releases over-budget resources");
cache.Budget=1;Acquire("oversized");Check(cache.Bytes==10,"An active oversized resource is usable");cache.Release("oversized");
Check(cache.Count==0,"An oversized unused entry must not linger");
try{cache.Acquire("failed",()=>throw new IOException("fixture"));throw new Exception("Expected factory error");}catch(IOException){}
Check(cache.Count==0&&cache.Bytes==0,"Failed creation cannot corrupt cache accounting");
Console.WriteLine("BYTE_CACHE_OK: LRU, shared pins, over-budget live values, release, failed creation");

var world=new DemoWorld(flat:true);world.DeveloperResidents(false);world.Jobs.Clear();
var ordered=typeof(DemoWorld).GetMethod("OrderedJobs",BindingFlags.Instance|BindingFlags.NonPublic)!
    .CreateDelegate<Func<DemoJob[]>>(world);
world.Jobs.Add(new(){Id=5,Build=BuildKind.Wall,Cell=world.Camp});
world.Jobs.Add(new(){Id=3,Recipe="cloth",Cell=world.Camp});
world.Jobs.Add(new(){Id=1,Haul=true,Cell=world.Camp});
var first=ordered();Check(first.Select(j=>j.Id).SequenceEqual(new[]{5,3,1}),"Construction/production/hauling order changed");
world.Update(.001);Check(ReferenceEquals(first,ordered()),"Unchanged tick rebuilt ordering");
world.SpawnItem("wood",1,world.Camp);Check(ReferenceEquals(first,ordered()),"Inventory mutation rebuilt ordering");
first[0].Progress=.5;first[0].Worker=7;Check(ReferenceEquals(first,ordered())&&ordered()[0].Worker==7,"Ownership must remain live");
world.Jobs.Remove(first[0]);world.Jobs.Add(new(){Id=4,Build=BuildKind.Wall,Cell=world.Camp});
Check(ordered()[0].Id==4&&!ReferenceEquals(first,ordered()),"Same-count replacement retained stale ordering");
world.Jobs[0]=new(){Id=2,Build=BuildKind.Wall,Cell=world.Camp};Check(ordered()[0].Id==2,"Indexer bypassed invalidation");
world.Jobs.Clear();Check(ordered().Length==0,"Clear retained removed jobs");
for(int i=0;i<1000;i++)world.Jobs.Add(new(){Id=1000-i,Build=BuildKind.Wall,Cell=world.Camp});
DemoJob[] Legacy()=>world.Jobs.OrderBy(j=>j.Haul?2:j.Production?1:0).ThenBy(j=>j.Id).ToArray();
static (double Ms,long Bytes) Measure(Func<DemoJob[]> action)
{
    for(int i=0;i<10;i++)action();long bytes=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();
    for(int i=0;i<1000;i++)GC.KeepAlive(action());timer.Stop();return(timer.Elapsed.TotalMilliseconds,GC.GetAllocatedBytesForCurrentThread()-bytes);
}
Check(Legacy().SequenceEqual(ordered()),"Cached order differs from reference");
var before=Measure(Legacy);var after=Measure(ordered);
Check(after.Bytes<4096,"Warm ordering allocates per call");
Console.WriteLine($"JOB_CACHE_MEASURE: jobs=1000 reads=1000 legacy_ms={before.Ms:F3} legacy_bytes={before.Bytes} cached_ms={after.Ms:F3} cached_bytes={after.Bytes}");
Console.WriteLine("JOB_CACHE_OK: stable ticks, live ownership, unrelated revision, add/remove, same-count replacement, clear");

// These types resolve all four shared resources from the referenced core assembly.
Check(ItemCatalog.Get("cloth") is not null&&ProductionCatalog.Get("cloth") is not null,"Production resource missing");
Check(CreatureCatalog.Default.Species.ContainsKey("base:dwarf"),"Creature resource missing");
ToolEquipmentChecks.Run();CreatureChecks.Run();ProductionBillChecks.Run();
Console.WriteLine("CORE_CHECKS_OK: shared core resources, job cache, resource cache, creatures and production");

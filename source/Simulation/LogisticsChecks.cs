namespace NoDwarf.Simulation;
public static class LogisticsChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Advance(DemoWorld w,int seconds){for(int i=0;i<seconds*30;i++)w.Update(1d/30);}
    public static void Run()
    {
        foreach(var (block,item) in new[]{("stone","cobblestone"),("stone_ramp","cobblestone"),("deepslate","deep_rubble"),("grass","soil"),("soil","soil"),("sand","sand"),("iron_ore","raw_iron"),("deepslate_iron_ore","raw_iron"),("coal_ore","coal"),("deepslate_coal_ore","coal")})
            Check(ItemCatalog.Drop("nodwarf:"+block)==item,"Drop identity: "+block);
        Check(ItemCatalog.Drop("nodwarf:bedrock") is null,"Bedrock never drops");
        var w=new DemoWorld();w.Items.Clear();w.Stockpiles.Clear();w.Workers.RemoveRange(1,w.Workers.Count-1);
        var ore=w.Terrain.Ores.First(p=>p.Value==GroundKind.Iron&&p.Key.Level is >-50 and <-5&&p.Key.X>10&&p.Key.X<240&&p.Key.Z>10&&p.Key.Z<240&&w.Terrain.Ores.TryGetValue(p.Key+new Cell(0,1),out var next)&&next==GroundKind.Iron).Key;
        var origin=ore+new Cell(0,-2);
        for(int x=1;x<=4;x++)for(int z=1;z<=3;z++)for(int y=0;y<2;y++)w.Terrain.Excavate(origin+new Cell(x,z,y));
        for(int y=0;y<2;y++)w.Terrain.Excavate(origin+new Cell(2,4,y));
        var resident=w.Workers[0];var stand=origin+new Cell(2,2);resident.X=stand.X;resident.Z=stand.Z;resident.Level=stand.Level;
        Check(w.DeveloperPlace(origin+new Cell(2,4),BuildKind.Door),"Test workshop door");
        Check(w.DeveloperPlace(origin+new Cell(1,1),BuildKind.Furnace)&&w.DeveloperPlace(origin+new Cell(4,1),BuildKind.Anvil)&&w.WorkshopReady,"Test workshop prepared");
        var stock=origin+new Cell(3,3);Check(w.SetStockpile(stock,stock),"Stockpile marked");
        w.SpawnItem("coal",2,stock);
        Check(w.QueueDig(ore)&&w.QueueDig(ore+new Cell(0,1)),"Actual vein mining queued");Advance(w,30);
        Check(w.Total("raw_iron")==2&&w.Iron==0,"Mining yields raw ore, never ingots");
        Check(!w.QueueDig(ore),"Cannot claim a mined block twice");Advance(w,30);
        Check(w.Items.Where(s=>s.Item=="raw_iron").All(s=>s.Cell==stock),"Ore physically hauled into stockpile");
        Check(w.QueueSmelt(),"Smelting reserves actual raw iron and fuel: "+string.Join("|",w.Events));
        Check(!w.QueueSmelt(),"Reservation prevents duplicate use");
        bool carrying=false;
        for(int i=0;i<900;i++){w.Update(1d/30);if(w.Jobs.Any(j=>j.Materials.Any(r=>r.Stage==1))){carrying=true;break;}}
        Check(carrying,"Worker physically collects smelting input");
        var saved=w.SaveJson();var loaded=DemoWorld.LoadJson(saved);
        Check(loaded.Total("raw_iron")==2&&loaded.Jobs.Any(j=>j.Materials.Any(r=>r.Stage==1)),"Save restores in-transit cargo and reservation");
        var cancelled=DemoWorld.LoadJson(saved);cancelled.CancelJob(cancelled.Jobs.Single(j=>j.Smelt));
        Check(cancelled.Total("raw_iron")==2&&cancelled.Available("raw_iron")==2&&cancelled.Total("coal")==2,"Cancellation returns cargo at resident position without duplication");
        Advance(loaded,60);Check(loaded.Iron==2&&loaded.Total("raw_iron")==0&&loaded.Total("coal")==1,"Delivered ore smelts into physical ingots");
        Check(loaded.QueueSword(),"Forge consumes produced ingots");Advance(loaded,60);
        Check(loaded.Swords==1&&loaded.Total("iron")==0&&loaded.Total("coal")==0,"Ore to sword full chain");
        var again=DemoWorld.LoadJson(loaded.SaveJson());Advance(again,30);Check(again.Swords==1,"Reload does not repeat completed output");
        var unsupported=System.Text.Json.Nodes.JsonNode.Parse(saved)!;unsupported["Version"]=99;
        bool bad=false;try{DemoWorld.LoadJson(unsupported.ToJsonString());}catch(InvalidDataException){bad=true;}Check(bad,"Unsupported save rejected");
        var blocked=new DemoWorld(flat:true);blocked.Stockpiles.Clear();
        var site=new Cell(20,20);Check(blocked.QueueBuild(site,BuildKind.Wall),"Delivery construction planned");
        for(int i=0;i<300;i++){blocked.Update(1d/30);if(blocked.Jobs[0].Materials.Any(r=>r.Stage==1))break;}
        int total=blocked.Total("cobblestone");var job=blocked.Jobs[0];
        foreach(var c in new[]{site+new Cell(1,0),site+new Cell(-1,0),site+new Cell(0,1),site+new Cell(0,-1)})blocked.Buildings[c]=new Building(c,BuildKind.Wall);
        Advance(blocked,5);blocked.CancelJob(job);
        Check(blocked.Total("cobblestone")==total,"Route change and cancellation preserve physical stock");
        var unrelated=new DemoWorld(flat:true);
        foreach(var c in new[]{new Cell(1,0),new Cell(0,1),new Cell(1,1)})unrelated.Buildings[c]=new Building(c,BuildKind.Wall);
        unrelated.QueueBuild(new Cell(0,0),BuildKind.Wall);unrelated.SpawnItem("raw_iron",3,new Cell(10,10));
        unrelated.SetStockpile(new Cell(11,10),new Cell(11,10));Advance(unrelated,60);
        Check(unrelated.Items.Where(s=>s.Item=="raw_iron").All(s=>unrelated.Stockpiles.Contains(s.Cell)),"Unreachable construction does not block idle hauling");
        var sourcing=new DemoWorld(flat:true);sourcing.Items.Clear();sourcing.Stockpiles.Clear();
        var source=new Cell(25,25);var alternative=new Cell(12,12);
        var trapped=sourcing.SpawnItem("cobblestone",20,source);sourcing.SpawnItem("cobblestone",20,alternative);
        Check(sourcing.QueueBuild(new Cell(28,25),BuildKind.Wall),"Reserve nearest materials before blockage");
        Check(sourcing.Jobs[0].Materials.Any(r=>r.StackId==trapped.Id),"Regression begins with nearer source reserved");
        // A 5x5 two-metre enclosure prevents collecting from adjacent cells as well.
        for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)
            if(Math.Abs(x)==2||Math.Abs(z)==2){var c=source+new Cell(x,z);sourcing.Buildings[c]=new Building(c,BuildKind.Wall);}
        Advance(sourcing,120);
        Check(sourcing.Buildings.ContainsKey(new Cell(28,25))&&trapped.Count==20,"Unreachable reservation replaced by reachable material without consuming sealed stock");
        sourcing.SetStockpile(new Cell(15,15),new Cell(15,15));Advance(sourcing,60);
        Check(!sourcing.Jobs.Any(j=>j.Haul&&j.Materials.Any(r=>r.StackId==trapped.Id)),"Sealed stock does not fill hauling queue");
        string saveRoot=Path.Combine(Path.GetTempPath(),"nodwarf-save-check-"+Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(saveRoot);string legacy=Path.Combine(saveRoot,"old.json"),current=Path.Combine(saveRoot,"saves","colony.json");
            File.WriteAllText(legacy,"old");Check(SaveFiles.ReadPath(current,legacy)==legacy,"Legacy save remains readable");
            SaveFiles.Write(current,"new",legacy);
            Check(File.ReadAllText(legacy)=="old"&&File.ReadAllText(current+".bak")=="old"&&SaveFiles.ReadPath(current,legacy)==current,"Migration retains legacy and previous backup");
            SaveFiles.Write(current,"newer");Check(File.ReadAllText(current+".bak")=="new"&&!File.Exists(current+".tmp"),"Replacement retains latest backup and no temporary file");
        }
        finally{Directory.Delete(saveRoot,true);}
        var durable=new DemoWorld(flat:true);var timber=new Cell(30,30);
        durable.Buildings[timber]=new(timber,BuildKind.Planks);
        double max=durable.RemainingDurability(timber);Check(!durable.DamageBlock(timber,15)&&durable.RemainingDurability(timber)==max-15,"Partial damage retained");
        durable=DemoWorld.LoadJson(durable.SaveJson());Check(durable.RemainingDurability(timber)==max-15,"Block damage survives save/load");
        Check(BlockCatalog.Get("nodwarf:sand").Durability<BlockCatalog.Get("nodwarf:stone").Durability&&BlockCatalog.Get("nodwarf:stone").Durability<BlockCatalog.Get("nodwarf:deepslate").Durability,"Hard materials take more mining work");
        int before=durable.Total("wood");Check(durable.DamageBlock(timber,max)&&!durable.Buildings.ContainsKey(timber),"Damage destroys built block");
        Check(!durable.DamageBlock(timber,max)&&durable.Total("wood")==before+2,"Destroyed block refunds once");
        Check(!durable.DamageBlock(new Cell(30,30,TerrainMap.MinLevel),double.MaxValue),"Bedrock remains indestructible");
        durable.Buildings[timber]=new(timber,BuildKind.Planks);Check(durable.RemainingDurability(timber)==max,"Replacement block starts intact");
        durable.QueueDemolish(timber);Advance(durable,90);Check(!durable.Buildings.ContainsKey(timber),"Normal demolition progresses through durability");
        loaded.SpawnItem("raw_food",2,stock);loaded.SpawnItem("wood",1,stock);
        Check(loaded.DeveloperPlace(origin+new Cell(3,1),BuildKind.Stove),"Independent cooking stove installed");
        int foodBefore=loaded.Total("meal");Check(loaded.QueueCook(),"Cooking reserves real ingredients and fuel");
        loaded=DemoWorld.LoadJson(loaded.SaveJson());Advance(loaded,60);
        Check(loaded.Total("meal")==foodBefore+2&&loaded.Total("raw_food")==0&&loaded.Total("wood")==0,"Cooking survives save and produces physical meals");
        loaded.Items.RemoveAll(i=>i.Item=="meal"&&i.Owner!="");var hungry=loaded.Workers[0];hungry.Person.Hunger=5;
        int meals=loaded.Total("meal");Advance(loaded,30);
        Check(hungry.Person.Hunger>25&&loaded.Total("meal")==meals-1,"Hungry resident walks to collect and consume exactly one physical meal");
        Console.WriteLine("DURABILITY_FOOD_OK: persistent damage, material hardness, demolition, indestructible bedrock, cooking and autonomous food collection");
        var absent=new DemoWorld(flat:true);absent.DeveloperResidents(false);var restored=DemoWorld.LoadJson(absent.SaveJson());
        Check(restored.Workers.Count==0&&restored.DeveloperResidents(true)&&restored.Workers.Count==3,"No-worker editor saves resident roster");
        Console.WriteLine("LOGISTICS_OK: vein drops, stockpile hauling, reservations, cancellation, mid-carry save, smelting, forging and no duplicate output");
    }
}

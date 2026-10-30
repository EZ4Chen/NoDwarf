using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NoDwarf.Simulation;
using NoDwarf.Services.Llm;

static class ItemEventChecks
{
    static void Check(bool ok,string why){if(!ok)throw new Exception("ITEM_EVENTS: "+why);}
    static void Reject(Action action,string message)
    {try{action();}catch(Exception e) when(e is InvalidDataException or LlmException){return;}throw new Exception("ITEM_EVENTS accepted "+message);}
    static void Advance(DemoWorld w,double seconds){for(int i=0;i<Math.Ceiling(seconds*30);i++)w.Update(1d/30);}
    static ContentPack Sample()
    {
        XDocument Read(string file){using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Example."+file)!;return ContentCatalog.ReadXml(stream);}
        return new("copper","1.0",1,new[]{new ContentDependency("base","1.0")},new[]{new ContentFile("production.xml","production",Read("production.xml")),new ContentFile("creatures.xml","creatures",Read("creatures.xml")),new ContentFile("events.xml","events",Read("events.xml")),new ContentFile("spatial.xml","defs",Read("spatial.xml"))});
    }
    static DemoWorld World(ContentCatalog content)
    {
        var w=new DemoWorld(flat:true,content:content);w.Workers.Clear();w.Items.Clear();w.Jobs.Clear();w.Stockpiles.Clear();
        w.Workers.Add(new(){Id="helper",Name="帮手",X=60,Z=60});w.Workers.Add(new(){Id="patient",Name="病人",X=57,Z=60});
        foreach(var worker in w.Workers){worker.Person.Hunger=80;worker.Person.Rest=90;worker.Person.Recreation=80;foreach(var key in ResidentWork.Types)worker.Person.WorkPriorities[key.Id]=0;}
        return w;
    }
    static StoryTurn Accept(DemoWorld w,string tool,string item,int count=1,double delay=.1)
    {
        var t=new StoryTurn{ObservedAt=w.SimulationTime,VitalState=w.StoryVitalState(),Status="received",Requests=1,
            AcceptedJson=JsonSerializer.Serialize(new{tool,arguments=new{item,count,delay_hours=delay,reason="验证新内容"}})};
        t.AcceptedCallId=t.Id+":0";w.Story.Turn=t;return t;
    }
    public static void Run()
    {
        var sample=Sample();var content=ContentCatalog.Load(new[]{sample});var bread=content.Item("copper:bread");
        Check(bread.Category=="food"&&bread.Edible!.Nutrition==30&&bread.ShelfLife==600&&bread.StorySupply,"typed item capabilities parsed");
        Check(content.StoryEvents["base:supplies"].Items.Contains(bread.Id)&&content.StoryEvents["copper:food"].Items.Contains(bread.Id)&&!content.StoryEvents["copper:food"].Items.Contains("copper:ingot"),"compiled event selectors");
        using(var catalog=JsonDocument.Parse(StorytellerTools.CatalogFor(content)))
        {
            var tool=catalog.RootElement.EnumerateArray().Single(t=>t.GetProperty("name").GetString()=="discover_food");
            Check(tool.GetProperty("parameters").GetProperty("properties").GetProperty("item").GetProperty("enum").EnumerateArray().Any(e=>e.GetString()==bread.Id),"AI schema generated from same event selector");
        }
        Check(!StorytellerTools.Catalog.Contains("copper:bread"),"independent worlds do not use global mod catalog");
        var w=World(content);var actor=w.Workers[0];actor.Person.Hunger=40;
        var ration=w.SpawnItem(bread.Id,1,actor.Tile);ration.Owner=actor.Id;
        using(var observation=JsonDocument.Parse(w.ObserveResident(actor.Id)))
            Check(observation.RootElement.GetProperty("inventory").GetProperty("items")[0].GetProperty("edible").GetProperty("Nutrition").GetDouble()==30,"resident observation includes usable nutrition");
        Check(w.ActResident(new(actor.Id,"eat",ItemId:ration.Id)).Ok,"explicit food action accepts new item");
        w=DemoWorld.LoadJson(w.SaveJson(),content:content);Advance(w,2);
        Check(w.Total(bread.Id)==0&&w.Workers[0].Person.Hunger>69&&w.Workers[0].Person.Hunger<71&&w.Workers[0].Person.MoodEffects.Any(e=>e.Key=="meal"&&e.Amount==6),"new food consumes once and applies configured nutrition/mood across load");
        w=World(content);w.Workers[0].Person.Hunger=1;w.SpawnItem(bread.Id,1,new(62,60));Advance(w,15);
        Check(w.Total(bread.Id)==0&&w.Workers[0].Person.Hunger>25,"automatic hunger recognizes new public food");
        w=World(content);actor=w.Workers[0];var patient=w.Workers[1];patient.Person.Hunger=1;w.InjureResident(patient.Id,"torso","fracture",90);actor.Person.WorkPriorities["care"]=1;
        w.SpawnItem(bread.Id,1,actor.Tile);
        Check(w.StartResidentTask(new(actor.Id,"base:feeding",patient.Id)).Ok,"new food in shared feeding task");
        for(int i=0;i<600&&actor.Person.Activity?.Kind!="feed";i++)w.Update(1d/30);
        Check(actor.Person.Activity?.Kind=="feed","new food physically carried");w=DemoWorld.LoadJson(w.SaveJson(),content:content);Advance(w,15);
        Check(w.Total(bread.Id)==0&&w.Workers[1].Person.Hunger>25&&w.DomainEvents.Recent.Single(e=>e.Type=="resident.fed").Result==bread.Id+":1","feeding applies new nutrition and committed item fact");
        w=World(content);actor=w.Workers[0];actor.Person.Hunger=40;var expired=w.SpawnItem(bread.Id,1,actor.Tile,new(){ExpiresAt=.5});expired.Owner=actor.Id;
        Check(w.ActResident(new(actor.Id,"eat",ItemId:expired.Id)).Ok,"food valid when starting");Advance(w,3);
        Check(actor.Person.Hunger<41&&w.Total("rot")==1,"expires during eating: no nutrition or phantom consumption");
        w=World(content);actor=w.Workers[0];actor.Person.Hunger=15;w.SpawnItem(bread.Id,1,actor.Tile).Owner=actor.Id;
        w.SetRallyPoint(actor.Tile);w.SetCivilianAlert(true);Advance(w,5);
        Check(w.Total(bread.Id)==0&&actor.Person.Hunger>43&&actor.Person.Hunger<46,"rallied civilians also consume configured food nutrition");
        var fake=sample with{Files=sample.Files.Concat(new[]{new ContentFile("display.xml","production",XDocument.Parse("<ProductionDefinitions><Items><Item id='copper:display' name='展示面包' value='1' texture='nodwarf:planks' color='ffffff' category='food'/></Items></ProductionDefinitions>"))}).ToArray()};
        var fakeCatalog=ContentCatalog.Load(new[]{fake});w=World(fakeCatalog);actor=w.Workers[0];actor.Person.Hunger=10;var display=w.SpawnItem("copper:display",1,actor.Tile);display.Owner=actor.Id;
        Check(!w.CanEat(display)&&!w.ActResident(new(actor.Id,"eat",ItemId:display.Id)).Ok,"classification alone never authorizes eating");
        Check(w.AvailableFood()==0,"non-edible category not counted as ready food");

        // Produce real food with a configured recipe in a real enclosed kitchen.
        w=World(content);var origin=new Cell(58,58);RoomEnvironmentChecks.Shell(w,origin);var stove=new Cell(59,59);w.Buildings[stove]=new(stove,BuildKind.Stove);
        actor=w.Workers[0];actor.X=60;actor.Z=60;actor.Person.WorkPriorities["cooking"]=1;actor.Person.WorkPriorities["crafting"]=1;
        w.SpawnItem("flour",2,actor.Tile);w.SpawnItem("wood",1,actor.Tile);
        Check(w.QueueProcessing("copper:bake"),"configured food recipe queued in kitchen");w=DemoWorld.LoadJson(w.SaveJson(),content:content);Advance(w,60);
        Check(w.Total(bread.Id)==2&&w.Total("flour")==0&&w.Total("wood")==0,"new food recipe output and inputs conserved");

        w=World(content);w.EnableStory(true);var turn=Accept(w,"discover_food",bread.Id,2);string receipt=StorytellerTools.Apply(w,turn);
        Check(w.Story.Pending.Single().DefinitionId=="copper:food","custom event resolved by loaded definition");
        Check(StorytellerTools.Apply(w,turn)==receipt&&w.Story.Pending.Count==1,"custom event receipt is idempotent");
        w=DemoWorld.LoadJson(w.SaveJson(),content:content);Advance(w,3);
        Check(w.Total(bread.Id)==2&&w.Story.Pending.Count==0,"custom event saved and executed");
        Check(StorytellerTools.Apply(w,w.Story.Turn!)==receipt&&w.Total(bread.Id)==2,"executed event not replayed after load");
        Check(!w.ScheduleStoryEvent(new(){Id="cooldown",DefinitionId="base:supplies",Kind="supplies",Item="wood",Count=1,Due=w.SimulationTime+.1*DemoWorld.StoryHour}).Ok,"event variants share effect cooldown");
        w=World(content);w.EnableStory(true);Reject(()=>StorytellerTools.Apply(w,Accept(w,"discover_food","copper:ingot")),"category-filter bypass");
        Reject(()=>StorytellerTools.Apply(w,Accept(w,"schedule_supplies","copper:ore")),"item lacking supply permission");
        Reject(()=>StorytellerTools.Apply(w,Accept(w,"discover_copper","copper:ingot",6)),"definition count limit");
        Reject(()=>StorytellerTools.Apply(w,Accept(w,"discover_food",bread.Id,1,13)),"definition delay limit");
        var budgetResult=StorytellerTools.Apply(w,Accept(w,"schedule_supplies",bread.Id,100));
        Check(!JsonDocument.Parse(budgetResult).RootElement.GetProperty("ok").GetBoolean()&&w.Story.Pending.Count==0,"new item cannot bypass valuation budget");
        Check(JsonDocument.Parse(StorytellerTools.Apply(w,Accept(w,"discover_copper","copper:ingot",1))).RootElement.GetProperty("ok").GetBoolean(),"custom copper tool accepted through shared dispatcher");
        var corrupt=JsonNode.Parse(w.SaveJson())!;corrupt["Story"]!["Pending"]![0]!["Item"]="copper:ore";Reject(()=>DemoWorld.LoadJson(corrupt.ToJsonString(),content:content),"archive bypass of event selector");
        Reject(()=>DemoWorld.LoadJson(w.SaveJson()),"missing content on restore");
        var copperRestored=DemoWorld.LoadJson(w.SaveJson(),content:content);Advance(copperRestored,3);
        Check(copperRestored.Total("copper:ingot")==1&&copperRestored.Story.Pending.Count==0,"copper discovery actually executes after reload");

        foreach(string mutation in new[]{"nutrition=\"-1\"","nutrition=\"NaN\"","nutrition=\"1000\""})
        {
            var bad=Sample();var files=bad.Files.Select(f=>f.Domain=="production"?f with{Document=XDocument.Parse(f.Document.ToString().Replace("nutrition=\"30\"",mutation))}:f).ToArray();
            Reject(()=>ContentCatalog.Load(new[]{bad with{Files=files}}),"invalid food definition");
        }
        foreach(string mutation in new[]{"handler=\"teleport\"","tool=\"wait\""})
        {
            var bad=Sample();var file=bad.Files.Single(f=>f.Domain=="events");var xml=file.Document.ToString().Replace(mutation.StartsWith("handler")?"handler=\"supplies\"":"tool=\"discover_copper\"",mutation);
            Reject(()=>ContentCatalog.Load(new[]{bad with{Files=bad.Files.Select(f=>f==file?f with{Document=XDocument.Parse(xml)}:f).ToArray()}}),"unknown behavior or reserved tool");
        }
        var stale=JsonNode.Parse(World(content).SaveJson())!;var changed=Sample();var prod=changed.Files.Single(f=>f.Domain=="production");
        var changedCatalog=ContentCatalog.Load(new[]{changed with{Files=changed.Files.Select(f=>f==prod?f with{Document=XDocument.Parse(prod.Document.ToString().Replace("nutrition=\"30\"","nutrition=\"31\""))}:f).ToArray()}});
        Reject(()=>DemoWorld.LoadJson(stale.ToJsonString(),content:changedCatalog),"changed food effect silently changes saves");
        // Exercise the actual director against an offline model that discovers the custom tool from its prompt.
        w=World(content);w.EnableStory(true);var client=new DiscoverClient();using var director=new StorytellerDirector(client);
        var config=new LlmConfiguration{ApiKey="offline-test",Settings=new(){SessionRequestLimit=10}};
        director.Pump(w,config,true);Check(client.Started.Task.Wait(5000),"offline model received prompt");
        for(int i=0;i<500&&w.Story.Pending.Count==0;i++){director.Pump(w,config,true);Thread.Sleep(5);}
        Check(w.Story.Pending.Single().Item==bread.Id&&client.Found,"model found custom event and food metadata without fixed whitelist");
        Advance(w,3);Check(w.Total(bread.Id)==1,"offline director executed custom content");
        CombatChecks.Run();
        Console.WriteLine("ITEM_EVENTS_OK: classification/capabilities, real cooking/eating/feeding/spoilage, catalog-generated tools, offline AI discovery, budgets, save roundtrip and dedup");
    }
    sealed class DiscoverClient:ILlmClient
    {
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);public bool Found;
        public Task<LlmReply> CompleteAsync(LlmConfiguration config,IReadOnlyList<LlmMessage> messages,CancellationToken token)
        {
            var system=messages.First(m=>m.role=="system").content;var tools=system[(system.IndexOf("Tools: ",StringComparison.Ordinal)+7)..];using var json=JsonDocument.Parse(tools);
            var definition=json.RootElement.EnumerateArray().Single(e=>e.GetProperty("name").GetString()=="discover_food");
            Check(definition.GetProperty("parameters").GetProperty("properties").GetProperty("item").GetProperty("enum").EnumerateArray().Any(e=>e.GetString()=="copper:bread"),"dynamic enum passed to model");
            using var observe=JsonDocument.Parse(messages.First(m=>m.role=="user").content);
            var bread=observe.RootElement.GetProperty("supply_items").EnumerateArray().Single(e=>e.GetProperty("Id").GetString()=="copper:bread");
            Found=bread.GetProperty("edible").GetProperty("Nutrition").GetDouble()==30;Started.TrySetResult();
            return Task.FromResult(new LlmReply("{\"tool\":\"discover_food\",\"arguments\":{\"item\":\"copper:bread\",\"count\":1,\"delay_hours\":0.1,\"reason\":\"食物机会\"}}",0,0,1));
        }
    }
}

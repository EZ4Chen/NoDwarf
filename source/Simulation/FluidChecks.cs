namespace NoDwarf.Simulation;
public static class FluidChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static FluidSimulation Pool(IEnumerable<Cell> cells,HashSet<Cell>? closed=null,Func<Cell,int>? capacity=null)
    {var air=cells.ToHashSet();return new(c=>air.Contains(c)?capacity?.Invoke(c)??1000:0,c=>air.Contains(c)&&closed?.Contains(c)!=true,_=>{},_=>{});}
    private static void Advance(FluidSimulation f,int steps)
    {
        for(int i=0;i<steps;i++)
        {f.Advance(.2);Check(f.Conserved,"Fluid mass must be conserved at every step");Check(f.Cells.Values.All(c=>c.Amount is >0 and <=1000&&double.IsFinite(c.Heat)&&c.Heat>=0),"No negative/nonfinite fluid state");}
    }
    public static void Run()
    {
        var row=Enumerable.Range(1,4).Select(x=>new Cell(x,1,10)).ToArray();var f=Pool(row);
        f.Add(row[0],1000,20);double heat=f.Cells.Values.Sum(c=>c.Heat);Advance(f,120);
        Check(row.All(c=>Math.Abs(f.Amount(c)-250)<=1),"Water levels horizontally: "+string.Join(",",row.Select(f.Amount)));
        Check(f.Pending==0,"Stable pool sleeps");Check(Math.Abs(f.Cells.Values.Sum(c=>c.Heat)-heat)<.01,"Transfer conserves heat");
        var shaft=Enumerable.Range(10,5).Select(y=>new Cell(8,8,y)).ToArray();f=Pool(shaft);f.Add(shaft[^1],1000);Advance(f,30);
        Check(f.Amount(shaft[0])==1000&&f.Cells.Count==1,"Gravity fills bottom and respects sealed boundaries");
        var u=new List<Cell>{new(1,1,10),new(2,1,10),new(3,1,10)};
        for(int y=11;y<=13;y++){u.Add(new(1,1,y));u.Add(new(3,1,y));}
        f=Pool(u);foreach(var start in new[]{new Cell(1,1,10),new(2,1,10),new(3,1,10),new(1,1,11),new(1,1,12)})f.Add(start,1000);
        Advance(f,500);Check(f.Amount(new(3,1,11))>900,"Connected U-tube rises on opposite side: "+string.Join(";",f.Cells.Values.Select(x=>$"{x.Cell}:{x.Amount}")));Check(f.Amount(new(3,1,13))==0,"Pressure cannot raise water above available head");
        var plug=new HashSet<Cell>{new(2,1,10)};f=Pool(u,plug);
        foreach(var start in new[]{new Cell(1,1,10),new(1,1,11),new(1,1,12),new(3,1,10)})f.Add(start,1000);
        Advance(f,60);Check(f.Amount(new(3,1,11))==0,"Pressure cannot cross closed connection");
        var finite=Pool(new[]{new Cell(40,40,11)});var finiteSource=new Aquifer{Cell=new(40,40,12),Head=13,Capacity=1000,Stored=1000,Rate=500};finite.AddSource(finiteSource);
        Advance(finite,30);Check(finiteSource.Stored==0&&finite.Mass==1000,"Finite source can exhaust into lower outlet");
        var boundary=Pool(new[]{new Cell(0,0,TerrainMap.MinLevel)});boundary.Add(new(0,0,TerrainMap.MinLevel),1000);Advance(boundary,20);Check(boundary.Mass==1000,"Map boundary does not delete liquid");
        long frozen=finite.Step;finite.Advance(0);Check(finite.Step==frozen,"Paused fluid receives no simulation time");
        var barrier=new HashSet<Cell>{row[1]};f=Pool(row,barrier);f.Add(row[0],1000);Advance(f,20);Check(f.Amount(row[2])==0,"Closed gate blocks water");
        barrier.Clear();f.GeometryChanged(row[1]);Advance(f,100);Check(f.Amount(row[3])>200,"Open gate wakes adjacent sleeping water");
        barrier.Add(row[1]);f.GeometryChanged(row[1]);long mass=f.Mass;int trapped=f.Amount(row[1]);Advance(f,30);Check(f.Mass==mass&&f.Amount(row[1])==trapped,"Closing wet gate retains trapped water");
        var slope=new Cell(20,20,10);f=Pool(new[]{slope},capacity:_=>500);Check(f.Add(slope,1000)==500&&f.Add(slope,100)==0,"Stair capacity bounded");
        f=Pool(row);f.Add(row[0],500,0);f.Add(row[0],500,80);Check(Math.Abs(f.Cells[row[0]].Temperature-40)<1e-6,"Mixing keeps mass-weighted temperature");
        var source=new Aquifer{Cell=new(30,30,14),Head=18,Capacity=30000,Stored=30000,Rate=500};
        var reservoirRoom=Enumerable.Range(1,4).Select(x=>new Cell(30+x,30,14)).Concat(Enumerable.Range(15,5).Select(y=>new Cell(31,30,y))).ToArray();
        f=Pool(reservoirRoom);f.AddSource(source);Advance(f,300);
        Check(source.Discovered&&source.Stored<30000&&f.Amount(new(31,30,15))>0,"Breach supplies pressure-driven upward flooding");
        Check(f.Amount(new(31,30,18))==0,"Aquifer respects maximum water head");
        var sealedCells=new HashSet<Cell>(reservoirRoom);var blocked=new HashSet<Cell>(new[]{new Cell(31,30,14)});f=Pool(sealedCells,blocked);
        var s=new Aquifer{Cell=new(30,30,14),Head=18,Capacity=1000,Stored=500,Rate=500,Recharge=10};f.AddSource(s);Advance(f,10);
        Check(!s.Discovered&&f.Cells.Count==0&&s.Stored==520&&f.Recharged==20,"Sealed source cannot leak; replenishment accounted");
        blocked.Clear();f.GeometryChanged(new(31,30,14));Advance(f,15);Check(f.Cells.Count>0&&s.Discovered,"Mining wakes source");
        var archive=f.Save(Array.Empty<Cell>());var restored=Pool(sealedCells,blocked);restored.Restore(System.Text.Json.JsonSerializer.Deserialize<FluidArchive>(System.Text.Json.JsonSerializer.Serialize(archive))!);
        Advance(f,20);Advance(restored,20);Check(f.Save(Array.Empty<Cell>()).Cells.SequenceEqual(restored.Save(Array.Empty<Cell>()).Cells)&&f.Mass==restored.Mass,"Save restores active queue and deterministic continuation");
        var world=new DemoWorld(flat:true);world.DeveloperResidents(false);var c=world.Camp+new Cell(2,2);
        Check(world.DeveloperPlace(c,BuildKind.Floodgate)&&!world.GateOpen(c),"Construct default closed gate");world.ToggleFloodgate(c);Check(world.Walkable(c),"Open gate passable");
        world.Fluids.Add(c,800);Check(!world.Walkable(c),"Deep water blocks walking");
        world.ToggleFloodgate(c);var saved=DemoWorld.LoadJson(world.SaveJson());Check(saved.Fluids.Amount(c)==800&&!saved.GateOpen(c)&&saved.Fluids.Conserved,"World archive preserves wet closed gate");
        world.Fluids.Add(c+new Cell(1,0),300);Check(!world.CanPlace(c+new Cell(1,0))&&!world.CanDeveloperPlace(c+new Cell(1,0),BuildKind.Wall),"Building cannot erase water");
        var bad=System.Text.Json.Nodes.JsonNode.Parse(saved.SaveJson())!;bad["Fluids"]!["Cells"]![0]!["Amount"]=-1;
        try{DemoWorld.LoadJson(bad.ToJsonString());throw new Exception("Invalid fluid archive accepted");}catch(InvalidDataException){}
        var mine=new DemoWorld();mine.DeveloperResidents(false);var rock=mine.Camp+new Cell(10,10,-5);var opening=rock+new Cell(1,0);
        var deposit=new Aquifer{Cell=rock,Capacity=10000,Stored=10000,Head=rock.Level+4,Rate=500};mine.Fluids.AddSource(deposit);
        Advance(mine.Fluids,10);Check(mine.Fluids.Cells.Count==0,"Buried aquifer does not leak through rock");
        Check(mine.DeveloperDig(opening),"Player excavation opens aquifer outlet");Advance(mine.Fluids,10);
        Check(deposit.Discovered&&mine.Fluids.Amount(opening)>0,"Excavation triggers actual underground breach");
        mine.Fluids.Drain(opening);Check(mine.DeveloperPlace(opening,BuildKind.Wall),"Dry outlet can be sealed with wall");int sealedStorage=deposit.Stored;Advance(mine.Fluids,10);Check(deposit.Stored==sealedStorage,"Wall reseal stops aquifer output");
        var limitCells=Enumerable.Range(0,FluidSimulation.MaxCells+1).Select(i=>new Cell(i/255,i%255,0)).ToArray();var limited=Pool(limitCells);
        for(int i=0;i<FluidSimulation.MaxCells;i++)limited.Add(limitCells[i],1);
        Check(limited.AtLimit&&limited.Add(limitCells[^1],1)==0&&limited.Conserved,"Cell limit retains volume instead of deleting water");
        limited.Drain(limitCells[0]);Check(limited.Add(limitCells[^1],1)==1&&limited.Conserved,"Freed capacity becomes usable again");
        var a=new DemoWorld(region:"temperate");var b=new DemoWorld(region:"temperate");Check(a.Fluids.Sources.Count>0&&a.Fluids.Sources.Select(s=>s.Cell).SequenceEqual(b.Fluids.Sources.Select(s=>s.Cell)),"Aquifers seeded reproducibly");
        Console.WriteLine("FLUID_OK: conservation, sleep/wake, gravity, U-tube, gates, pressure, recharge, heat, navigation, deterministic saves");
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;

public static class RoomHeatTransferChecks
{
    private static readonly Cell Origin=new(20,20),Left=new(22,22);
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Advance(DemoWorld world,int seconds){for(int i=0;i<30*seconds;i++)world.Update(1d/30);}
    private static long Builds(DemoWorld world)=>JsonSerializer.SerializeToElement(world.Probe!.Report()).GetProperty("counters").GetProperty("rooms.heat-network-builds").GetInt64();
    private static DemoWorld Pair(int separation=5,bool door=false)
    {
        var world=new DemoWorld(flat:true);world.DeveloperResidents(false);world.Items.Clear();world.Stockpiles.Clear();
        RoomEnvironmentChecks.Shell(world,Origin);
        RoomEnvironmentChecks.Shell(world,Origin+new Cell(separation,0));
        if(door)
        {
            var opening=Origin+new Cell(5,2);world.Buildings[opening]=new(opening,BuildKind.Door);
            world.Buildings.Remove(opening+new Cell(0,0,1));
        }
        world.RoomAt(Left);world.RoomAt(Left+new Cell(separation,0));
        var state=JsonNode.Parse(world.SaveJson())!;
        foreach(var room in state["RoomClimates"]!.AsArray())room!["Temperature"]=room["Anchor"]!["X"]!.GetValue<int>()<25?40:10;
        return DemoWorld.LoadJson(state.ToJsonString());
    }
    public static void Run()
    {
        var right=Left+new Cell(5,0);var wall=Pair();wall.Probe=new();wall.Update(4);
        Check(wall.TemperatureAt(Left)==40&&wall.TemperatureAt(right)==10,"Temperature waits for the five-second update interval");wall.Update(1);
        double retention=Math.Exp(-5d/62);
        double leftOutdoor=wall.OutdoorTemperatureAt(Left)+(40-wall.OutdoorTemperatureAt(Left))*retention;
        double rightOutdoor=wall.OutdoorTemperatureAt(right)+(10-wall.OutdoorTemperatureAt(right))*retention;
        Check(wall.TemperatureAt(Left)<leftOutdoor&&wall.TemperatureAt(right)>rightOutdoor,"Shared wall transfers warmth from hot to cold room");
        Check(Math.Abs((wall.TemperatureAt(Left)+wall.TemperatureAt(right))-(leftOutdoor+rightOutdoor))<1e-9,"Room-to-room exchange conserves heat after outdoor exchange");
        var unrelated=Pair();
        foreach(var origin in new[]{new Cell(50,50),new Cell(52,50)})
        for(int x=0;x<3;x++)for(int z=0;z<3;z++)
        {
            var c=origin+new Cell(x,z);
            if(x==0||x==2||z==0||z==2)
                for(int y=0;y<2;y++){var b=c+new Cell(0,0,y);unrelated.Buildings[b]=new(b,BuildKind.Wall);}
            var roof=c+new Cell(0,0,2);unrelated.Buildings[roof]=new(roof,BuildKind.Planks);
        }
        unrelated.RoomAt(new Cell(51,51));unrelated.RoomAt(new Cell(53,51));unrelated.Update(5);
        Check(Math.Abs(unrelated.TemperatureAt(Left)-wall.TemperatureAt(Left))<1e-9&&Math.Abs(unrelated.TemperatureAt(right)-wall.TemperatureAt(right))<1e-9,"A distant heat network cannot change local transfer rates");
        var door=Pair(door:true);door.Update(5);
        Check(door.TemperatureAt(right)>wall.TemperatureAt(right),"Door conducts faster than a solid wall");
        var twoDoors=Pair(door:true);var secondDoor=Origin+new Cell(5,3);
        twoDoors.Buildings[secondDoor]=new(secondDoor,BuildKind.Door);
        twoDoors.Buildings.Remove(secondDoor+new Cell(0,0,1));
        twoDoors.RoomAt(Left);twoDoors.RoomAt(right);twoDoors.Update(5);
        Check(Math.Abs(twoDoors.TemperatureAt(Left)-door.TemperatureAt(Left))<1e-9&&
            Math.Abs(twoDoors.TemperatureAt(right)-door.TemperatureAt(right))<1e-9,
            "Multiple doors use one fixed room-pair connection instead of adding conductance");
        var thick=Pair(6);thick.Update(5);
        Check(Math.Abs(thick.TemperatureAt(Left+new Cell(6,0))-rightOutdoor)<1e-9,"Simplified model does not search through multiple wall cells");
        var gap=Pair(7);gap.Update(5);
        Check(Math.Abs(gap.TemperatureAt(Left+new Cell(7,0))-rightOutdoor)<1e-9,"Outdoor gap does not directly couple separate rooms");
        var unequal=new DemoWorld(flat:true);unequal.DeveloperResidents(false);unequal.Items.Clear();RoomEnvironmentChecks.Shell(unequal,Origin);
        for(int x=0;x<4;x++)for(int z=0;z<6;z++)
        {
            var c=Origin+new Cell(x+5,z);
            if(x==0||x==3||z==0||z==5)
                for(int y=0;y<2;y++){var b=c+new Cell(0,0,y);unequal.Buildings[b]=new(b,BuildKind.Wall);}
            var roof=c+new Cell(0,0,2);unequal.Buildings[roof]=new(roof,BuildKind.Planks);
        }
        unequal.RoomAt(Left);unequal.RoomAt(right);
        var unequalState=JsonNode.Parse(unequal.SaveJson())!;
        foreach(var room in unequalState["RoomClimates"]!.AsArray())room!["Temperature"]=room["Anchor"]!["X"]!.GetValue<int>()<25?40:10;
        unequal=DemoWorld.LoadJson(unequalState.ToJsonString());unequal.Update(5);
        double smallOutdoor=unequal.OutdoorTemperatureAt(right)+(10-unequal.OutdoorTemperatureAt(right))*Math.Exp(-5d/46);
        Check(unequal.RoomAt(right).Area==8&&Math.Abs(16*(unequal.TemperatureAt(Left)-leftOutdoor)+8*(unequal.TemperatureAt(right)-smallOutdoor))<1e-9,"Different-sized rooms exchange equal energy, not equal temperature changes");
        var order=Pair();var reverseState=JsonNode.Parse(order.SaveJson())!;
        reverseState["RoomClimates"]=new JsonArray(reverseState["RoomClimates"]!.AsArray().Select(n=>n!.DeepClone()).Reverse().ToArray());
        var reverse=DemoWorld.LoadJson(reverseState.ToJsonString());Advance(order,20);Advance(reverse,20);
        Check(Math.Abs(order.TemperatureAt(Left)-reverse.TemperatureAt(Left))<1e-9&&Math.Abs(order.TemperatureAt(right)-reverse.TemperatureAt(right))<1e-9,"Heat transfer is independent of room registration order");
        long builds=Builds(wall);Advance(wall,10);
        Check(Builds(wall)==builds,"Stable rooms reuse the heat graph across temperature ticks");
        var remoteEdit=new Cell(220,220);wall.Buildings[remoteEdit]=new(remoteEdit,BuildKind.Wall);Advance(wall,6);
        Check(Builds(wall)==builds,"Construction far from all room dependencies must not rebuild heat connections");
        var pending=Pair();var farWall=new Cell(30,22);pending.Buildings.Remove(farWall);pending.Probe=new();pending.Update(5);
        long beforeClose=Builds(pending);
        pending.Buildings[farWall]=new(farWall,BuildKind.Wall);pending.Update(5);
        Check(Builds(pending)>beforeClose&&pending.RoomAt(right).Enclosed&&
            JsonSerializer.SerializeToElement(pending.Probe!.Report()).GetProperty("counters").GetProperty("rooms.heat-network-links").GetInt64()>0,
            "Completing a neighbour's far wall must invalidate its cached outdoor witness and reconnect heat");
        wall.Update(.37);var loaded=DemoWorld.LoadJson(wall.SaveJson());Advance(wall,15);Advance(loaded,15);
        Check(Math.Abs(wall.TemperatureAt(Left)-loaded.TemperatureAt(Left))<1e-9&&Math.Abs(wall.TemperatureAt(right)-loaded.TemperatureAt(right))<1e-9,"Save/load rebuilds connections without changing the heat trajectory");
        var openingWall=Origin+new Cell(0,2);wall.Buildings.Remove(openingWall);Advance(wall,6);
        Check(Builds(wall)>builds&&!wall.RoomAt(Left).Enclosed&&wall.TemperatureAt(Left)==wall.OutdoorTemperatureAt(Left),"Opening the shell discards stale links and uses outdoor temperature");
        var jump=Pair(door:true);jump.Update(600);
        Check(new[]{jump.TemperatureAt(Left),jump.TemperatureAt(right)}.All(t=>double.IsFinite(t)&&t>=10&&t<=40),"Large time jumps remain bounded");

        var unseen=new DemoWorld(flat:true);unseen.DeveloperResidents(false);unseen.Items.Clear();unseen.Stockpiles.Clear();
        RoomEnvironmentChecks.Shell(unseen,Origin);RoomEnvironmentChecks.Shell(unseen,Origin+new Cell(5,0));RoomEnvironmentChecks.Shell(unseen,Origin+new Cell(10,0));
        var farOrigin=new Cell(45,20);var far=farOrigin+new Cell(2,2);RoomEnvironmentChecks.Shell(unseen,farOrigin);unseen.RoomAt(far);
        var fire=Origin+new Cell(1,1);unseen.Buildings[fire]=new(fire,BuildKind.Campfire);unseen.FireFuel[fire]=300;
        Advance(unseen,150);
        Check(JsonNode.Parse(unseen.SaveJson())!["RoomClimates"]!.AsArray().Count==4,"Heating discovers an entire adjacent-room chain without UI selection");
        Check(unseen.TemperatureAt(right)>unseen.TemperatureAt(far)+.5&&unseen.TemperatureAt(Left+new Cell(10,0))>unseen.TemperatureAt(far)+.05,"Warmth reaches unselected neighbouring rooms along the chain");
        Console.WriteLine("ROOM_HEAT_TRANSFER_OK: outdoor exchange, five-second cadence, fixed wall/door links, conservation, cache invalidation, unseen neighbours, stable time jumps and save/load");
    }
}

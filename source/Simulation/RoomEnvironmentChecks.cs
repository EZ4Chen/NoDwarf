using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;

public static class RoomEnvironmentChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Advance(DemoWorld world,int seconds){for(int i=0;i<30*seconds;i++)world.Update(1d/30);}
    public static void Shell(DemoWorld world,Cell origin,bool roof=true)
    {
        for(int x=0;x<6;x++)for(int z=0;z<6;z++)
        {
            var c=origin+new Cell(x,z);
            if(x==0||x==5||z==0||z==5)
            {
                bool door=x==2&&z==5;
                world.Buildings[c]=new(c,door?BuildKind.Door:BuildKind.Wall);
                if(!door)world.Buildings[c+new Cell(0,0,1)]=new(c+new Cell(0,0,1),BuildKind.Wall);
            }
            if(roof)world.Buildings[c+new Cell(0,0,2)]=new(c+new Cell(0,0,2),BuildKind.Planks);
        }
    }
    public static void Run()
    {
        RoomHeatTransferChecks.Run();
        var w=new DemoWorld(flat:true);w.DeveloperResidents(false);w.Items.Clear();
        var origin=new Cell(20,20);var c=origin+new Cell(2,2);Shell(w,origin,false);
        var outside=w.RoomAt(c);
        Check(!outside.Enclosed&&outside.Area==16&&outside.Temperature==w.OutdoorTemperatureAt(c),"Unroofed courtyard is outdoors");
        Shell(w,origin);var plain=w.RoomAt(c);
        Check(plain.Enclosed&&plain.Area==16&&plain.Roofed==16,"Walls, two-metre door and roof enclose a room");
        var hole=origin+new Cell(0,2,1);w.Buildings.Remove(hole);
        Check(!w.RoomAt(c).Enclosed,"Gap above a low wall opens the room");w.Buildings[hole]=new(hole,BuildKind.Wall);
        var roof=c+new Cell(0,0,2);w.Buildings.Remove(roof);
        Check(!w.RoomAt(c).Enclosed&&w.RoomAt(c).Roofed==15,"Roof hole invalidates enclosure immediately");w.Buildings[roof]=new(roof,BuildKind.Planks);
        var door=origin+new Cell(2,5);w.Buildings.Remove(door);
        Check(!w.RoomAt(c).Enclosed,"Missing doorway is outdoors");w.Buildings[door]=new(door,BuildKind.Door);
        for(int x=1;x<5;x++)for(int z=1;z<5;z++)
        {var floor=origin+new Cell(x,z,-1);w.Buildings[floor]=new(floor,BuildKind.Planks);}
        var bed=origin+new Cell(1,1);w.Buildings[bed]=new(bed,BuildKind.Bed);
        Check(w.RoomAt(c).Quality>plain.Quality&&w.RoomAt(c).Use=="卧室"&&w.RoomAt(c).FinishedFloor==16,"Quality reflects usable space, floors and furniture");
        var other=new Cell(35,20);Shell(w,other);var otherCell=other+new Cell(2,2);w.RoomAt(otherCell);
        var fire=origin+new Cell(1,4);w.Buildings[fire]=new(fire,BuildKind.Campfire);w.FireFuel[fire]=300;
        double initial=w.TemperatureAt(c);Advance(w,6);
        Check(w.TemperatureAt(c)>initial&&w.TemperatureAt(c)<initial+2,"Heating warms gradually");Advance(w,90);
        double warm=w.TemperatureAt(c);
        Check(warm>w.TemperatureAt(otherCell)+4,"Separate rooms keep independent temperatures");
        var loaded=DemoWorld.LoadJson(w.SaveJson());
        Check(Math.Abs(loaded.TemperatureAt(c)-warm)<.000001&&loaded.RoomAt(c).Quality==w.RoomAt(c).Quality,"Room temperature and quality survive save/load");
        w.Update(.37);loaded=DemoWorld.LoadJson(w.SaveJson());Advance(w,7);Advance(loaded,7);
        Check(Math.Abs(loaded.TemperatureAt(c)-w.TemperatureAt(c))<.000001,"Saving between thermal ticks preserves subsequent heating");
        warm=w.TemperatureAt(c);
        for(int z=1;z<5;z++)for(int y=0;y<2;y++)
        {var part=origin+new Cell(3,z,y);w.Buildings[part]=new(part,BuildKind.Wall);}
        var right=origin+new Cell(4,2);
        Check(w.RoomAt(c).Area==8&&w.RoomAt(right).Area==4&&Math.Abs(w.TemperatureAt(right)-warm)<.0001,"Splitting rooms preserves inherited temperature");
        for(int z=1;z<5;z++)for(int y=0;y<2;y++)w.Buildings.Remove(origin+new Cell(3,z,y));
        Check(w.RoomAt(c).Area==16&&Math.Abs(w.TemperatureAt(c)-warm)<.0001,"Merging rooms preserves temperature");
        w.FireFuel.Clear();double before=w.TemperatureAt(c);Advance(w,30);
        Check(w.TemperatureAt(c)<before,"Unheated room cools gradually");
        w.Buildings.Remove(roof);
        Check(w.TemperatureAt(c)==w.OutdoorTemperatureAt(c)&&w.HeatingAt(c)==0,"Open room uses outdoor temperature immediately");
        var openFire=other+new Cell(10,0);w.Buildings[openFire]=new(openFire,BuildKind.Campfire);w.FireFuel[openFire]=300;
        Check(w.TemperatureAt(openFire)==w.OutdoorTemperatureAt(openFire),"Outdoor fire does not create an indoor climate");
        var gate=other+new Cell(2,5);w.Buildings[gate]=new(gate,BuildKind.Floodgate);
        w.Buildings[gate+new Cell(0,0,1)]=new(gate+new Cell(0,0,1),BuildKind.Wall);
        Check(w.RoomAt(otherCell).Enclosed,"Closed floodgate seals a room");w.ToggleFloodgate(gate);
        Check(!w.RoomAt(otherCell).Enclosed,"Opening a floodgate invalidates the room");w.ToggleFloodgate(gate);
        Check(w.RoomAt(otherCell).Enclosed,"Closing floodgate restores enclosure");
        var invalid=JsonNode.Parse(loaded.SaveJson())!;invalid["RoomClimates"]![0]!["Temperature"]=999;
        bool rejected=false;try{DemoWorld.LoadJson(invalid.ToJsonString());}catch(InvalidDataException){rejected=true;}
        Check(rejected,"Invalid room temperature is rejected");
        Console.WriteLine("ROOM_ENVIRONMENT_OK: roofs, doorways, wall gaps, quality, independent temperatures, heating/cooling, split/merge, save roundtrip");
    }
}

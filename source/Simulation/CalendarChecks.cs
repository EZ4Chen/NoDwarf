namespace NoDwarf.Simulation;
public static class CalendarChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static void Run()
    {
        var start=GameCalendar.At(0);Check(start.Year==1&&start.SeasonIndex==0&&start.Day==1&&start.TimeText=="08:00","Calendar starts at spring day 1 08:00");
        Check(GameCalendar.At(399.9).Day==1&&GameCalendar.At(400).Day==2&&GameCalendar.At(400).TimeText=="00:00","Midnight changes date exactly once");
        for(int season=0;season<4;season++)
        {
            double boundary=(30*(season+1)-1d/3)*600;
            var before=GameCalendar.At(boundary-.01);var after=GameCalendar.At(boundary);
            Check(before.SeasonIndex==season&&before.Day==30,"Season has thirty days");
            Check(after.SeasonIndex==(season+1)%4&&after.Day==1&&after.Year==(season==3?2:1),"Season/year rollover");
        }
        Check(GameCalendar.At(30*600).DaylightHours>14&&GameCalendar.At(90*600).DaylightHours<10,"Summer day longer than winter");
        Check(GameCalendar.At(100).Sunlight>.7&&GameCalendar.At(400).Sunlight<.001,"Noon and midnight light");
        var clock=new SimulationClock();var w=new DemoWorld(flat:true);w.DeveloperResidents(false);
        void Frame(double realSeconds){int ticks=clock.Advance(realSeconds);for(int i=0;i<ticks;i++)w.Update(SimulationClock.TickSeconds);}
        Frame(1);double normal=w.SimulationTime;clock.SetSpeed(3);Frame(1);
        Check(Math.Abs(w.SimulationTime-normal*4)<1e-7,"3x advances three simulation seconds, independent of frame count");
        clock.Paused=true;long minute=w.Calendar.TotalMinutes;double time=w.SimulationTime;Frame(10);
        Check(w.Calendar.TotalMinutes==minute&&w.SimulationTime==time,"Pause freezes calendar and simulation");
        var loaded=DemoWorld.LoadJson(w.SaveJson());Check(loaded.Calendar==w.Calendar,"Save/load preserves calendar without resetting date");
        Console.WriteLine("CALENDAR_OK: day, 30-day seasons, 120-day years, daylight, speed, pause and save/load");
    }
}

using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private string _scheduleBrush="work";
    private string[]? _copiedSchedule;
    private int _residentRosterPage;
    private readonly Dictionary<(string Resident,int Hour),Button> _scheduleCells=new();
    private readonly Dictionary<(string Resident,string Work),Button> _priorityCells=new();
    private readonly Dictionary<string,Button> _scheduleBrushes=new();
    private const int RosterPageSize=12;
    private static string ScheduleColor(string mode)=>mode switch{"sleep"=>"3b586c","work"=>"78643d","recreation"=>"655077",_=>"424c4d"};
    private static string ScheduleGlyph(string mode)=>mode switch{"sleep"=>"休","work"=>"工","recreation"=>"娱",_=>"自"};
    private IEnumerable<DemoWorker> PlanningRoster()
    {
        _residentRosterPage=Math.Clamp(_residentRosterPage,0,Math.Max(0,(_world.Workers.Count-1)/RosterPageSize));
        return _world.Workers.Skip(_residentRosterPage*RosterPageSize).Take(RosterPageSize);
    }
    private void PlanningPager(Node parent)
    {
        var row=RH(parent);RT(row,_world.Calendar.DateText+" · "+_world.Calendar.TimeText,13,RMuted).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        int pages=Math.Max(1,(_world.Workers.Count+RosterPageSize-1)/RosterPageSize);
        if(pages<=1)return;
        var previous=RB(row,"‹",()=>{_residentRosterPage--;RenderResidentPanel();});previous.Disabled=_residentRosterPage==0;
        RT(row,$"{_residentRosterPage+1} / {pages}",13,RMuted);
        var next=RB(row,"›",()=>{_residentRosterPage++;RenderResidentPanel();});next.Disabled=_residentRosterPage==pages-1;
    }
    private void PlanningName(Node parent,DemoWorker worker,int height=30)
    {
        var name=RB(parent,worker.Name,()=>SelectDossierResident(_world.Workers.IndexOf(worker)),"选择居民，保留当前页签",92);
        name.CustomMinimumSize=new(92,height);name.ClipText=true;
        name.AddThemeColorOverride("font_color",worker.Id==_dossierId?RGold:RInk);
    }
    private void RenderResidentSchedule(DemoWorker selected)
    {
        _scheduleCells.Clear();_scheduleBrushes.Clear();
        RT(_residentBody,"每格 1 小时 · 选择颜色后按住左键涂抹",13,RMuted);
        var tools=RH(_residentBody,8);
        foreach(string mode in ResidentWork.Modes)
        {
            var brush=RB(tools,ResidentWork.ModeName(mode),()=>{_scheduleBrush=mode;RenderResidentPanel();},"选中后涂到下方时间格",86);
            brush.AddThemeStyleboxOverride("normal",RStyle(ScheduleColor(mode),mode==_scheduleBrush?"dfd8b7":"626b6a",8,4));
            _scheduleBrushes[mode]=brush;
        }
        RT(tools,"",12,RMuted).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        RB(tools,"日间作息",()=>ApplyDay(ResidentWork.DayShift()),"仅应用于当前选中居民：22—06 休息、08—18 工作、18—20 娱乐，其余无固定");
        RB(tools,"全天自由",()=>ApplyDay(ResidentWork.FreeDay()),"仅应用于当前选中居民");
        RB(tools,"复制",()=>{_copiedSchedule=(string[])selected.Person.Schedule.Clone();_residentNotice.Text="已复制"+selected.Name+"的作息，切换居民后可粘贴。";RenderResidentPanel();});
        var paste=RB(tools,"粘贴",()=>{if(_copiedSchedule is not null)ApplyDay(_copiedSchedule);});paste.Disabled=_copiedSchedule is null;
        var hours=RH(_residentBody,1);RT(hours,"居民 / 时刻",12,RMuted).CustomMinimumSize=new(92,22);
        for(int hour=0;hour<24;hour++){var label=RT(hours,hour.ToString("00"),11,hour==_world.Calendar.Hour?RGold:RMuted);label.CustomMinimumSize=new(30,22);label.HorizontalAlignment=HorizontalAlignment.Center;}
        var roster=RScroll(_residentBody);roster.AddThemeConstantOverride("separation",5);
        foreach(var worker in PlanningRoster())
        {
            var row=RH(roster,1);PlanningName(row,worker);
            for(int h=0;h<24;h++)
            {
                int hour=h;var button=RB(row,"",()=>{},"",30);button.CustomMinimumSize=new(30,30);button.AddThemeFontSizeOverride("font_size",13);
                _scheduleCells[(worker.Id,hour)]=button;StyleScheduleCell(button,worker,hour);
                button.GuiInput+=input=>
                {
                    bool paint=input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:true}||input is InputEventMouseMotion move&&(move.ButtonMask&MouseButtonMask.Left)!=0;
                    if(!paint)return;
                    button.AcceptEvent();
                    // Godot captures a held drag on its first control. Hit-test the
                    // actual pointer so painting can cross cells and resident rows.
                    var pointer=button.GetGlobalTransformWithCanvas()*((InputEventMouse)input).Position;
                    PaintScheduleAt(pointer);
                };
            }
        }
        PlanningPager(_residentBody);
        RT(_residentBody,"休息、娱乐时段不领取普通工作；工作时段保留进食和极度疲劳保护。无固定时按需求与工作优先级行动。",12,RMuted,true);
        RT(_residentBody,"手动指定的个人活动优先完成。新居民和旧存档默认全天无固定，可直接套用日间作息。",12,RMuted,true);
    }
    private void PaintScheduleAt(Vector2 pointer)
    {
        foreach(var (key,cell) in _scheduleCells)
        {
            if(!cell.GetGlobalRect().HasPoint(pointer)||cell.Disabled)continue;
            var worker=_world.Workers.FirstOrDefault(w=>w.Id==key.Resident);
            if(worker is null||worker.Person.Schedule[key.Hour]==_scheduleBrush)return;
            var result=_world.ActResident(new(worker.Id,"schedule",_scheduleBrush,Hour:key.Hour));
            _residentNotice.Text=result.Message;if(result.Ok)StyleScheduleCell(cell,worker,key.Hour);
            return;
        }
    }
    private void StyleScheduleCell(Button b,DemoWorker worker,int hour)
    {
        string mode=worker.Person.Schedule[hour];b.Text=ScheduleGlyph(mode);b.Disabled=worker.Person.Dead;
        b.AddThemeStyleboxOverride("normal",RStyle(ScheduleColor(mode),hour==_world.Calendar.Hour?"cfb77c":"536162",0,0));
        b.TooltipText=$"{worker.Name} · {hour:00}:00—{hour+1:00}:00 · {ResidentWork.ModeName(mode)}";
    }
    private void ApplyDay(string[] hours)
    {
        var result=_world.ActResident(new(_dossierId,"schedule",Hours:hours));_residentNotice.Text=result.Message;RenderResidentPanel();
    }
    private void RenderResidentWork(DemoWorker selected)
    {
        _priorityCells.Clear();
        RT(_residentBody,"1 最高 · 4 最低 · — 不做。左键提高，右键反向；同级从左到右。",13,RMuted,true);
        var headings=RH(_residentBody,3);RT(headings,"居民",13,RMuted).CustomMinimumSize=new(92,34);
        foreach(var type in ResidentWork.Types)
        {
            var label=RT(headings,type.Name,14,type.Available?RInk:RMuted);label.CustomMinimumSize=new(61,34);label.HorizontalAlignment=HorizontalAlignment.Center;
        }
        var roster=RScroll(_residentBody);roster.AddThemeConstantOverride("separation",6);
        foreach(var worker in PlanningRoster())
        {
            var row=RH(roster,3);PlanningName(row,worker,34);
            foreach(var type in ResidentWork.Types)
            {
                var button=RB(row,"",()=>{},"",61);button.CustomMinimumSize=new(61,34);button.AddThemeFontSizeOverride("font_size",18);
                _priorityCells[(worker.Id,type.Id)]=button;StylePriorityCell(button,worker,type);
                button.GuiInput+=input=>
                {
                    if(input is not InputEventMouseButton {Pressed:true} click||click.ButtonIndex is not (MouseButton.Left or MouseButton.Right))return;
                    button.AcceptEvent();if(!type.Available||worker.Person.Dead)return;
                    int old=worker.Person.WorkPriorities[type.Id];int priority=click.ButtonIndex==MouseButton.Left?(old+4)%5:(old+1)%5;
                    var result=_world.ActResident(new(worker.Id,"work_priority",type.Id,Priority:priority));_residentNotice.Text=result.Message;
                    if(result.Ok)StylePriorityCell(button,worker,type);
                };
            }
        }
        PlanningPager(_residentBody);
        RT(_residentBody,"研究：选择聚落项目后，按优先级自动研究。医疗：有随身绷带时照料可达、未处理的伤口。",12,RMuted,true);
        RT(_residentBody,"屠宰尚无对应生产流程，暂不可用。医疗和研究默认关闭；其他已有工作默认开启，搬运优先级较低。",12,RMuted,true);
    }
    private static void StylePriorityCell(Button b,DemoWorker worker,ResidentWorkType type)
    {
        int priority=worker.Person.WorkPriorities[type.Id];b.Text=!type.Available?"未开放":priority==0?"—":priority.ToString();b.Disabled=!type.Available||worker.Person.Dead;
        if(!type.Available)b.AddThemeFontSizeOverride("font_size",11);
        var color=priority switch{1=>RGreen,2=>RGold,3=>RInk,_=>RMuted};b.AddThemeColorOverride("font_color",color);
        b.AddThemeStyleboxOverride("normal",RStyle(priority is 1 or 2?"303d35":"272f31",priority==1?"73896c":"4b5657",4,2));
        b.TooltipText=type.Available?$"{worker.Name} · {type.Name}\n{(priority==0?"不做":$"优先级 {priority}")}"+(type.Skill==""?"":$" · {ResidentCatalog.Skill(type.Skill).Name} {worker.Person.Level(type.Skill)} 级")+"\n左键：— → 4 → 3 → 2 → 1 → —；右键反向":"尚未实现动物、尸体与屠宰配方流程";
    }
}

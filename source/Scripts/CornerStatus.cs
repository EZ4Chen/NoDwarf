using Godot;
namespace NoDwarf;
public partial class Main
{
    private VBoxContainer _cornerStatus=null!;
    private Label _temperatureStatus=null!,_weatherStatus=null!,_seasonStatus=null!;
    private Button _doubleSpeed=null!;
    private static void StyleFloatingText(Label label)
    {
        label.AddThemeColorOverride("font_shadow_color",new Color(0,0,0,.95f));
        label.AddThemeConstantOverride("shadow_offset_x",1);label.AddThemeConstantOverride("shadow_offset_y",1);
        label.AddThemeColorOverride("font_outline_color",new Color(0,0,0,.65f));
        label.AddThemeConstantOverride("outline_size",2);
    }
    private void BuildCornerStatus(Control root,VBoxContainer viewColumn)
    {
        _cornerStatus=new VBoxContainer{Name="TimeClimateHud",AnchorLeft=1,AnchorRight=1,AnchorTop=1,AnchorBottom=1,
            OffsetLeft=-242,OffsetRight=-12,OffsetTop=-214,OffsetBottom=-48,Alignment=BoxContainer.AlignmentMode.End,MouseFilter=Control.MouseFilterEnum.Ignore};
        _cornerStatus.AddThemeConstantOverride("separation",3);root.AddChild(_cornerStatus);
        Label Line(string accessibleName)
        {
            var label=Text("",14,Ink);label.HorizontalAlignment=HorizontalAlignment.Right;
            label.MouseFilter=Control.MouseFilterEnum.Pass;StyleFloatingText(label);UiAccess.Name(label,accessibleName);
            _cornerStatus.AddChild(label);return label;
        }
        _temperatureStatus=Line("室外温度");_weatherStatus=Line("当前天气");_timeStatus=Line("当前时间");
        _calendarLabel=Line("当前日期");_seasonStatus=Line("当前季节");
        var speeds=new HBoxContainer{Alignment=BoxContainer.AlignmentMode.End};speeds.AddThemeConstantOverride("separation",2);_cornerStatus.AddChild(speeds);
        Button Speed(string icon,string name,Action action)
        {
            var button=IconButton(speeds,icon,"",action);button.CustomMinimumSize=new(30,28);
            button.AddThemeConstantOverride("icon_max_width",24);
            button.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());
            button.AddThemeStyleboxOverride("hover",HudStyle("ffffff18",2));
            button.AddThemeStyleboxOverride("pressed",HudStyle("ffffff28",2));
            button.TooltipText=name;UiAccess.Name(button,name);return button;
        }
        _pause=Speed("pause","暂停 / 继续 [空格]",TogglePause);
        _normalSpeed=Speed("play","正常速度（1×）",()=>SetTimeSpeed(1));
        _doubleSpeed=Speed("fast","两倍速度（2×）",()=>SetTimeSpeed(2));
        _fastSpeed=Speed("fastest","三倍速度（3×）",()=>SetTimeSpeed(3));
        _layerStatus=Text("",12,Muted);viewColumn.AddChild(_layerStatus);
        var display=new HBoxContainer();viewColumn.AddChild(display);
        var walls=new MenuButton{Text="精确施工",CustomMinimumSize=new(118,30)};display.AddChild(walls);_cutButton=walls;
        UiAccess.Name(walls,"墙壁显示方式");walls.TooltipText="选择墙壁显示方式 [H 循环切换]";
        foreach(var (name,id) in new[]{("全貌",0),("本层完整墙",3),("墙壁剖切",1),("精确施工",2)})walls.GetPopup().AddRadioCheckItem(name,id);
        walls.AboutToPopup+=()=>{int mode=!_cutaway?0:_interiorView?1:_floorWalls?3:2;var popup=walls.GetPopup();for(int i=0;i<popup.ItemCount;i++)popup.SetItemChecked(i,popup.GetItemId(i)==mode);};
        walls.GetPopup().IdPressed+=id=>SetViewMode((int)id);
        Button(display,"地图概览",()=>_mapPanel.Visible=!_mapPanel.Visible);
    }
    private void SetTimeSpeed(int speed){_clock.SetSpeed(speed);_clock.Paused=false;RefreshUi();}
    private void RefreshTimeControls()
    {
        foreach(var (button,active) in new[]{(_pause,_clock.Paused),(_normalSpeed,!_clock.Paused&&_clock.Speed==1),(_doubleSpeed,!_clock.Paused&&_clock.Speed==2),(_fastSpeed,!_clock.Paused&&_clock.Speed==3)})
        {
            button.AddThemeColorOverride("icon_normal_color",active?new Color("ffe2a2"):new Color("aab4bf"));
            button.AccessibilityDescription=active?"当前已选中":"";
        }
    }
}

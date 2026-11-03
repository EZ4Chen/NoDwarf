using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private Control? _safetyPanel;
    private GameWindowFrame? _safetyFrame;
    private SafetyRules? _safetyDraft;
    private DemoWorld? _safetyEditWorld;
    private Node3D? _safetyVisuals;
    private SafetyRules? _drawnSafety;
    private int _drawnSafetyLevel=int.MinValue;
    private static bool SafetyTool(string tool)=>tool is "workarea" or "forbiddenarea";
    private void CloseSafetyPanel(bool discard=true)
    {
        _safetyPanel?.QueueFree();_safetyPanel=null;_safetyFrame=null;
        if(discard){_safetyDraft=null;_safetyEditWorld=null;}
        SyncSafetyAreas();
    }
    private void ShowSafetyAreas(bool fresh=true)
    {
        if(fresh||_safetyEditWorld!=_world){_safetyEditWorld=_world;_safetyDraft=_world.Safety.CheckedCopy();}
        CloseSafetyPanel(false);SelectTool("select");_buildMenu.Hide();
        var host=(Control)_selectionPanel.GetParent();
        _safetyPanel=new Control{MouseFilter=Control.MouseFilterEnum.Stop};host.AddChild(_safetyPanel);_safetyPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _safetyFrame=new GameWindowFrame("安全作业范围",()=>CloseSafetyPanel());_safetyPanel.AddChild(_safetyFrame);_safetyFrame.FitCentered(_safetyPanel,new(720,560));
        var column=GameUi.Column(GameUi.Scroll(_safetyFrame.Body));
        var enabled=GameUi.Field(column,"启用安全限制",new CheckBox{ButtonPressed=_safetyDraft!.Enabled});
        enabled.Toggled+=v=>_safetyDraft=_safetyDraft! with{Enabled=v};
        var supply=GameUi.Field(column,"停止到作业区外取料",new CheckBox{ButtonPressed=_safetyDraft.StopOutsideSupplies,Disabled=_safetyDraft.WorkAreas.Count==0});
        supply.Toggled+=v=>_safetyDraft=_safetyDraft! with{StopOutsideSupplies=v};
        GameUi.Label(column,"作业区限制工作和照料目的地；禁行区限制完整路径。征召和手动移动可离开作业区，仍不能进入禁行区。",wrap:true,muted:true);
        GameUi.Label(column,"停用限制或移除禁行区会重新允许居民进入；限制补给可能造成断粮断水，请查看生活预警。",wrap:true,muted:true);
        void List(bool forbidden)
        {
            var areas=forbidden?_safetyDraft!.ForbiddenAreas:_safetyDraft!.WorkAreas;
            GameUi.Label(column,forbidden?"禁行区":"作业区",size:18);
            if(areas.Count==0)GameUi.Label(column,forbidden?"尚无禁行区。":"尚无作业区：工作地点不限。先添加范围，再启用停止外出取料。",wrap:true,muted:true);
            for(int i=0;i<areas.Count;i++)
            {
                int index=i;var area=areas[i];var row=new HBoxContainer();column.AddChild(row);
                var label=GameUi.Label(row,$"#{i+1}  X {area.Min.X}–{area.Max.X} / Z {area.Min.Z}–{area.Max.Z} / 高度 {area.Min.Level}–{area.Max.Level} m",wrap:true);
                label.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
                GameUi.Action(row,"定位",()=>{_focus=WorldPosition(area.Min);_heightSelected=true;_layerOffset=area.Min.Level-(_foundation?.WorldLevel??_world.Camp.Level);_cutaway=true;PositionCamera();SyncWorld();});
                GameUi.Action(row,"移除",()=>
                {
                    var next=areas.Where((_,n)=>n!=index).ToArray();
                    _safetyDraft=forbidden?_safetyDraft! with{ForbiddenAreas=next}:_safetyDraft! with{WorkAreas=next,StopOutsideSupplies=next.Length>0&&_safetyDraft!.StopOutsideSupplies};ShowSafetyAreas(false);
                });
            }
            GameUi.Action(column,forbidden?"添加禁行区":"添加作业区",()=>
            {CloseSafetyPanel(false);SelectTool(forbidden?"forbiddenarea":"workarea");});
        }
        List(false);List(true);
        _safetyFrame.SetStatus("修改尚未应用。地图框选：左键两次选角，+/− 切换高度，右键或 Esc 取消。");
        _safetyFrame.AddAction("取消",()=>CloseSafetyPanel());
        _safetyFrame.AddAction("应用范围",()=>
        {
            if(_safetyEditWorld!=_world){CloseSafetyPanel();return;}
            try{var result=_world.ConfigureSafety(_safetyDraft!);CloseSafetyPanel();SyncWorld();RefreshUi();ShowActionFeedback(result.Message,!result.Ok);}
            catch(InvalidDataException e){_safetyFrame!.SetStatus(e.Message);}
        },true);
        SyncSafetyAreas();
    }
    private void CommitSafetyArea(Cell a,Cell b)
    {
        if(_safetyEditWorld!=_world||_safetyDraft is null){SelectTool("select");return;}
        bool forbidden=_tool=="forbiddenarea";var area=SafetyVolume.Between(a,b);
        var edited=forbidden?_safetyDraft with{ForbiddenAreas=_safetyDraft.ForbiddenAreas.Append(area).ToArray()}:_safetyDraft with{WorkAreas=_safetyDraft.WorkAreas.Append(area).ToArray()};
        try{_safetyDraft=edited.CheckedCopy();ShowSafetyAreas(false);}
        catch(InvalidDataException e){ShowSafetyAreas(false);_safetyFrame!.SetStatus(e.Message);}
    }
    private void SyncSafetyAreas()
    {
        if(_safetyEditWorld is not null&&_safetyEditWorld!=_world){CloseSafetyPanel();return;}
        bool show=_safetyPanel is not null||SafetyTool(_tool);
        if(_safetyVisuals is null){if(!show)return;_safetyVisuals=new();AddChild(_safetyVisuals);}
        _safetyVisuals.Visible=show;if(!show)return;
        var rules=_safetyDraft??_world.Safety;
        if(ReferenceEquals(rules,_drawnSafety)&&_drawnSafetyLevel==ActiveLevel)return;
        _drawnSafety=rules;_drawnSafetyLevel=ActiveLevel;Clear(_safetyVisuals);
        foreach(var (areas,color) in new[]{(rules.WorkAreas,"7ac1c9"),(rules.ForbiddenAreas,"ed9b86")})
        foreach(var area in areas.Where(a=>ActiveLevel>=a.Min.Level&&ActiveLevel<=a.Max.Level))
        {
            float width=area.Max.X-area.Min.X+1,depth=area.Max.Z-area.Min.Z+1;
            var center=WorldPosition(area.Min with{Level=ActiveLevel})+new Vector3((width-1)/2,.06f,(depth-1)/2);
            var mat=Mat(color);mat.ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded;mat.NoDepthTest=true;
            foreach(float side in new[]{-1f,1f})
            {Box(_safetyVisuals,center+new Vector3(side*width/2,0,0),new Vector3(.05f,.05f,depth),mat);Box(_safetyVisuals,center+new Vector3(0,0,side*depth/2),new Vector3(width,.05f,.05f),mat);}
        }
    }
}

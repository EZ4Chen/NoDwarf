using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private void HealthMeter(Node parent,string label,double value,Color color,string hint)
    {
        var row=RH(parent,6);row.TooltipText=hint;
        var text=RT(row,$"{label}  {value:F0}%",12,color);text.CustomMinimumSize=new(126,20);
        var area=RV(row,0);area.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        area.SizeFlagsVertical=Control.SizeFlags.ShrinkCenter;
        var bar=RMeter(area,value,100,color,8);
        UiAccess.Name(bar,$"{label}，{value:F0}%，{hint}");
    }
    private void RenderTreatment(Node parent,HealthTreatment treatment,bool needsCare)
    {
        string text=treatment.Remaining>0?$"治疗质量 {treatment.Quality:P0} · 剩余 {treatment.Remaining/GameCalendar.SecondsPerDay*24:F1} 游戏小时":needsCare?"护理未生效或已到期 · 可安排护理":"无需再次护理";
        RT(parent,text,12,treatment.Remaining>0?RGreen:RMuted,true);
    }
    private string _healthResident="",_healthExpandedKey="";
    private bool _healthAllParts;
    private readonly Dictionary<string,Button> _healthRows=new();
    private Button _healthPartsToggle=null!;
    private int _healthShownParts;
    private static string HealthKey(HealthConditionState state)=>state.Part+":"+state.Definition.Id;
    private static bool HealthBleeding(HealthConditionState s,ResidentProfile p)=>s.Definition.Id=="blood_loss"?ResidentHealth.Bleeding(p)>0:
        p.Injuries.Any(i=>i.Part==s.Part&&i.Kind==s.Definition.Id&&!i.Treated&&(i.Kind=="cut"||i.Kind=="missing"&&i.FreshAmputation));
    private static string HealthStatus(HealthConditionState s,ResidentProfile p)=>HealthBleeding(s,p)?"正在流血":s.Definition.Recovery switch
    {
        HealthRecovery.Environment=>s.Definition.Id=="dehydration"?"需补水":s.Definition.Id=="cold"?"需保暖":"需降温",
        HealthRecovery.Permanent=>"永久",
        HealthRecovery.Blood=>HealthBleeding(s,p)?"正在流血":"恢复中",
        HealthRecovery.Nutrition=>p.Hunger>=40?"恢复中":p.Hunger<10?"恶化中":"恢复暂停",
        HealthRecovery.Immunity when s.Immunity>=100=>"恢复中",
        _=>HealthBleeding(s,p)?"正在流血":s.Treatment?.Remaining>0?(s.Treatment.Remaining<60?"护理将到期":"护理有效"):"待护理"
    };
    private void RenderHealthList(Node parent,DemoWorker w)
    {
        if(_healthResident!=w.Id){_healthResident=w.Id;_healthExpandedKey="";_healthAllParts=false;}
        _healthRows.Clear();_healthShownParts=0;
        var p=w.Person;var states=HealthConditions.States(p).OrderBy(s=>HealthBleeding(s,p)?0:s.Severity>=s.Definition.Maximum*.8?1:HealthStatus(s,p)=="待护理"?2:3).ThenBy(s=>s.Part).ToArray();
        if(!states.Any(s=>HealthKey(s)==_healthExpandedKey))_healthExpandedKey="";
        RSection(parent,"异常状态",states.Length==0?"无异常":$"{states.Length} 项");
        var scrollContent=RScroll(parent);
        var inset=new MarginContainer();inset.AddThemeConstantOverride("margin_right",12);scrollContent.AddChild(inset);
        var list=RV(inset,4);
        if(states.Length==0)RT(list,"未发现伤病或其他异常。",14,RGreen,true);
        foreach(var state in states)
        {
            string key=HealthKey(state);bool expanded=_healthExpandedKey==key;
            string part=state.Part==""?"全身":ResidentCatalog.PartName(state.Part);
            string severity=state.Definition.Recovery==HealthRecovery.Injury?$"{state.Severity:F0}%":state.Stage.Name;
            string status=HealthStatus(state,p);
            var button=RB(list,"",()=>{_healthExpandedKey=expanded?"":key;RenderResidentPanel();if(_healthRows.TryGetValue(key,out var focus))focus.GrabFocus();},"点击展开或收起详情",0);
            button.CustomMinimumSize=new(0,36);button.ToggleMode=true;button.ButtonPressed=expanded;
            button.AddThemeStyleboxOverride("normal",RStyle("252b2c","424744",8,5));
            button.AddThemeStyleboxOverride("hover",RStyle("343a37","a18a57",8,5));
            button.AddThemeStyleboxOverride("pressed",RStyle("393b30","b79a60",8,5));
            UiAccess.Name(button,$"{part}，{state.Definition.Name}，{severity}，{status}，{(expanded?"已展开，点击收起":"已收起，点击展开")}");
            var margin=new MarginContainer{MouseFilter=Control.MouseFilterEnum.Ignore};button.AddChild(margin);margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            margin.AddThemeConstantOverride("margin_left",8);margin.AddThemeConstantOverride("margin_right",8);
            var row=RH(margin,6);row.MouseFilter=Control.MouseFilterEnum.Ignore;
            void Text(string text,int width,Color color,bool grow=false)
            {var label=RT(row,text,13,color);label.CustomMinimumSize=new(width,0);label.VerticalAlignment=VerticalAlignment.Center;label.MouseFilter=Control.MouseFilterEnum.Ignore;if(grow)label.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;}
            Text(expanded?"▾":"▸",12,RGold);Text(part,42,RMuted);Text(state.Definition.Name,70,RInk,true);Text(severity,40,RGold);Text(status,76,status is "正在流血" or "待护理"?RRed:RGreen);
            _healthRows[key]=button;
            if(expanded)
            {
                var panel=new PanelContainer();panel.AddThemeStyleboxOverride("panel",RStyle("202628","766744",10,8));list.AddChild(panel);
                var details=RV(panel,6);RenderHealthDetail(details,state,p);
            }
        }
        // Outside the scrolling list: the complete-body switch remains reachable with many injuries.
        _healthPartsToggle=RB(parent,_healthAllParts?"▾ 收起全部身体部位":"▸ 查看全部身体部位（10）",()=>{_healthAllParts=!_healthAllParts;RenderResidentPanel();_healthPartsToggle.GrabFocus();if(_healthAllParts)RevealHealthParts(_residentScrolls.Last());},"展开或收起十个部位的完整度；不改变选中的异常状态");
        _healthPartsToggle.ToggleMode=true;_healthPartsToggle.ButtonPressed=_healthAllParts;
        UiAccess.Name(_healthPartsToggle,_healthAllParts?"全部身体部位已展开，点击收起":"全部身体部位已收起，点击展开");
        if(_healthAllParts)
        {
            RSection(list,"全部身体部位");
            foreach(var part in ResidentCatalog.Parts)
            {RStat(list,part.Name,$"{p.Integrity(part.Id):P0}",p.Integrity(part.Id)<1?RGold:RMuted);_healthShownParts++;}
        }
    }
    private async void RevealHealthParts(ScrollContainer scroll)
    {
        // Wait for the expanded contents and scrollbar to finish their layout passes.
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(GodotObject.IsInstanceValid(scroll)&&scroll.IsInsideTree()&&_healthAllParts)scroll.ScrollVertical=int.MaxValue;
    }
    private void RenderHealthDetail(Node parent,HealthConditionState state,ResidentProfile p)
    {
        var def=state.Definition;
        if(def.Id=="fracture")RT(parent,p.Injuries.Any(i=>i.Kind=="fracture"&&i.Part==state.Part&&i.Fixed)?"已用夹板固定":"尚未固定；夹板可改善功能和恢复速度",13,RMuted,true);
        if(def.Recovery==HealthRecovery.Environment){HealthMeter(parent,"严重程度",state.Severity/3,RRed,"离开恶劣温度或补水后逐步恢复。");RT(parent,def.Id=="dehydration"?"保持饮水至少 40% 后恢复。":"移至舒适温度的室内；寒冷时使用供暖和衣物。",13,RMuted,true);return;}
        if(def.Recovery==HealthRecovery.Permanent)
        {RT(parent,$"永久损伤 {state.Severity:F0}% · 普通护理不能消除",13,RMuted,true);
            if(p.Prostheses.TryGetValue(state.Part,out var function))RT(parent,$"木制义肢 · 部位功能 {function:P0}",13,RGreen,true);
            if(def.Id=="missing")RT(parent,HealthBleeding(state,p)?"新断肢仍在出血，需要原地包扎止血。":"断端已稳定；护理不会让肢体重新长出。",13,HealthBleeding(state,p)?RRed:RMuted,true);return;}
        HealthMeter(parent,def.Recovery==HealthRecovery.Injury?"伤势":def.Recovery==HealthRecovery.Blood?"失血程度":"阶段进度",
            def.Recovery is HealthRecovery.Injury or HealthRecovery.Blood?state.Severity:def.StageProgress(state.Severity),RRed,"进度越低越轻；疾病或营养不良满阶段会加重。");
        if(state.Immunity is double immunity)HealthMeter(parent,"免疫",immunity,RGreen,"满值后逐步退病；护理减缓恶化，不直接增加免疫。");
        string effects=HealthConditions.EffectText(state.Stage.Effects);if(effects!="")RT(parent,effects,12,RMuted,true);
        if(def.Recovery==HealthRecovery.Nutrition)RT(parent,"饱食低于 10% 时恶化；保持至少 40% 才逐步恢复。",13,RMuted,true);
        else if(def.Recovery==HealthRecovery.Blood)
            RT(parent,ResidentHealth.Bleeding(p)>0?$"每模拟秒失血 {ResidentHealth.Bleeding(p):F2}% · 需要先止血。血量耗尽会死亡。":"已止血，血量缓慢恢复；吃饱恢复更快。",13,RMuted,true);
        else if(state.Treatment is not null)
        {
            bool needed=def.Recovery==HealthRecovery.Immunity?state.Immunity<100:p.Injuries.Any(i=>i.Part==state.Part&&i.Kind==def.Id&&HealthCare.WoundNeedsCare(i));
            RenderTreatment(parent,state.Treatment,needed);
            if(def.Recovery==HealthRecovery.Injury)RT(parent,"护理促进愈合；永久损伤单独列出，不会随普通治疗消失。",12,RMuted,true);
        }
    }
}

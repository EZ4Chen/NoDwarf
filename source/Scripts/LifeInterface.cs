using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private VBoxContainer _lifeNotices=null!;
    private string _lifeNoticeKey="";
    private Button _waterPointAction=null!;
    private void RefreshLifeNotices()
    {
        if(_lifeNotices is null)return;
        var alerts=_world.LifeAlerts;string key=string.Join("|",alerts.Select(a=>a.Id+":"+a.Text+":"+a.Location));if(key==_lifeNoticeKey)return;_lifeNoticeKey=key;
        foreach(Node child in _lifeNotices.GetChildren()){_lifeNotices.RemoveChild(child);child.QueueFree();}
        foreach(var alert in alerts.Take(3))
        {
            var b=Button(_lifeNotices,alert.Text,()=>FocusLifeAlert(alert));b.AutowrapMode=TextServer.AutowrapMode.WordSmart;b.CustomMinimumSize=new(226,30);
            b.TooltipText=alert.Resident!=""||alert.Location is not null?"定位并查看状态":"查看处理方式";UiAccess.Name(b,alert.Text,b.TooltipText);b.AddThemeColorOverride("font_color",alert.Priority==0?new Color("ed9b86"):alert.Priority==1?Accent:Ink);b.AddThemeFontSizeOverride("font_size",12);
        }
        if(alerts.Count>3)
        {
            var more=new MenuButton{Text=$"全部生活预警（{alerts.Count}）"};_lifeNotices.AddChild(more);UiAccess.Name(more,"全部生活预警");
            for(int i=0;i<alerts.Count;i++)more.GetPopup().AddItem(alerts[i].Text,i);
            var snapshot=alerts.ToArray();more.GetPopup().IdPressed+=id=>{if(id>=0&&id<snapshot.Length)FocusLifeAlert(snapshot[id]);};
        }
    }
    private void FocusLifeAlert(LifeAlert alert)
    {
        int i=_world.Workers.FindIndex(w=>w.Id==alert.Resident);
        if(i>=0){SelectResident(i);_focus=WorldPosition(_world.Workers[i].Tile);}
        else if(alert.Location is Cell c)_focus=WorldPosition(c);
        else if(alert.Id is "food" or "water")
        {if(!_inspector.Visible)TogglePanel(_inspector);ShowActionFeedback(alert.Id=="water"?"在生产页制作水桶；选中安全水源地格可设为取水点。":"在生产页安排烹饪，或在建筑规划 → 区域划定种植区。");}
        else ShowActionFeedback(alert.Text);
        if(alert.Location is Cell location && (alert.Id.StartsWith("dig-water:") || alert.Id.StartsWith("flood:")))
        {
            _heightSelected=true;_cutaway=true;_interiorView=false;_floorWalls=false;
            _layerOffset=location.Level-(_foundation?.WorldLevel??_world.Camp.Level);_revision=-1;
        }
        PositionCamera();RefreshUi();
        if(alert.Id.StartsWith("dig-water:") && int.TryParse(alert.Id[10..],out int jobId))ShowWaterDigWarning(jobId);
    }
    private void ShowWaterDigWarning(int jobId)
    {
        var world=_world;
        var job=world.Jobs.FirstOrDefault(j=>j.Id==jobId&&j.WaterPaused);
        if(job is null)return;
        string risk=world.DigWaterRisk(job);
        var dialog=new ConfirmationDialog{Title=$"采掘 #{jobId} · 水患暂停",
            DialogText=risk==""?"当前渗水风险已消退。恢复前仍会重新检查水情。":
                "开挖处有渗水或承压涌水风险，继续可能淹没通道并困住居民。可先排水、设置闸门或取消采掘。确认只适用于这项任务当前水情；变化或读档后会重新检查。",
            OkButtonText=risk==""?"恢复采掘":"承担涌水风险并继续",CancelButtonText="保持暂停"};
        AddChild(dialog);
        var cancel=dialog.AddButton("取消这项采掘",true,"cancel-dig");UiAccess.Name(cancel,"取消这项采掘","保留已挖地形，移除未完成任务");
        UiAccess.Name(dialog.GetOkButton(),dialog.OkButtonText);UiAccess.Name(dialog.GetCancelButton(),"保持暂停");
        dialog.Confirmed+=()=>{if(_world==world){world.Log(world.ResumeWaterDig(jobId,risk).Message);RefreshUi();}dialog.QueueFree();};
        dialog.CustomAction+=action=>{if(action=="cancel-dig"){if(_world==world&&world.Jobs.Contains(job)){world.CancelJob(job);world.RefreshLifeAlerts();SyncWorld();RefreshUi();}dialog.QueueFree();}};
        dialog.Canceled+=()=>dialog.QueueFree();dialog.PopupCentered();
    }
    private void LifeCareMenu(Node parent,DemoWorker patient,Func<string> helper)
    {
        var menu=new MenuButton{Text="饮水、固定与义肢 ▾",CustomMinimumSize=new(0,30)};parent.AddChild(menu);UiAccess.Name(menu,"更多照料操作");
        var popup=menu.GetPopup();popup.AddItem("送饮水（消耗一桶水）",0);popup.AddItem("固定骨折（消耗夹板）",1);popup.AddItem("装木制义肢（恢复部分能力）",2);popup.AddItem(patient.Person.Isolated?"解除接触隔离":"接触隔离（暂停社交和公共娱乐）",3);
        void CheckCare()
        {
            var medic=_world.Workers.FirstOrDefault(w=>w.Id==helper());
            string common=patient.Person.Dead?"该居民已死亡":medic is null||medic.Person.Incapacitated||medic.Person.Manipulation<.05||medic.Combat.Drafted?"请选择能够照料、未征召的居民":"";
            bool Has(string item)=>_world.Available(item)>0||medic is not null&&_world.Inventory(medic).Any(s=>s.Item==item);
            string[] reasons=[
                patient.Person.Thirst>=95?"暂不口渴":!Has("water_bucket")&&!(Has("bucket")&&_world.WaterPoints.Count>0)?"需要一桶饮水，或水桶与取水点":"",
                !patient.Person.Injuries.Any(i=>i.Kind=="fracture"&&!i.Fixed&&i.Severity>i.PermanentSeverity)?"没有待固定的骨折":!Has("splint")?"缺少夹板，可在工作台制作":"",
                !patient.Person.Injuries.Any(i=>i.Kind=="missing"&&i.Treated&&i.Part is not ("head" or "torso")&&!patient.Person.Prostheses.ContainsKey(i.Part))?"没有已止血且尚未安装义肢的缺失肢体":!Has("prosthesis")?"缺少木制义肢，可在工作台制作":""
            ];
            for(int i=0;i<3;i++){string reason=common!=""?common:reasons[i];popup.SetItemDisabled(i,reason!="");popup.SetItemTooltip(i,reason!=""?reason:i==2?"消耗一件木制义肢，恢复该部位 50% 功能，安装后不能取回。":"安排照料；执行时检查用品和通路。");}
            popup.SetItemDisabled(3,patient.Person.Dead);
        }
        CheckCare();popup.AboutToPopup+=CheckCare;
        popup.IdPressed+=id=>{if(id==3)ResidentDo("isolate",actor:patient.Id);else ResidentDo(id==0?"drink":id==1?"splint":"prosthesis",patient.Id,actor:helper());};
        menu.TooltipText="木制义肢逐个安装到已止血的缺失肢体，恢复 50% 部位功能；消耗成品，不能再取回。隔离阻止热病接触传播。";
    }
}

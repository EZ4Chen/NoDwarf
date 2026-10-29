using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private PanelContainer _combatPanel=null!;
    private Button _draftButton=null!,_combatStop=null!,_alertButton=null!;
    private Label _combatSummary=null!;
    private HBoxContainer _raidTools=null!;
    private Button _locateEnemy=null!;
    private OptionButton _defenseMode=null!;
    private readonly Dictionary<string,Node3D> _enemyVisuals=new();
    private DemoWorld? _combatVisualWorld;
    private void CombatNotice(ResidentResult r){ShowActionFeedback(r.Message,!r.Ok);RefreshUi();}
    private void BuildCombatInterface(Control root,HBoxContainer tabs,HBoxContainer selected)
    {
        _draftButton=IconButton(selected,"sword","征召",DraftSelectedResidents);
        UiAccess.Name(_draftButton,"征召或解除征召","快捷键 R，作用于所有选中居民。征召会暂停工作并放下搬运中的材料；解除后重新安排工作。");
        _combatStop=Button(selected,"停止",StopSelectedResidents);
        _combatPanel=Panel(root,Vector2.Zero,new Vector2(350,280));BottomLeft(_combatPanel,480,350);
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",8);_combatPanel.AddChild(column);
        column.AddChild(Text("防卫",18,Ink));
        _combatSummary=Text("",14,Ink,true);column.AddChild(_combatSummary);
        Button(column,"⚑ 设置集合点",()=>{SelectTool("rally");_combatPanel.Hide();_hint.Text="左键地面设置平民集合点；右键取消。";});
        _alertButton=Button(column,"开启集合警报",()=>CombatNotice(_world.SetCivilianAlert(!_world.CivilianAlert)));
        UiAccess.Name(_alertButton,"切换平民集合警报","未征召居民停止工作并集合。解除后重新分配工作。");
        var defenseRow=new HBoxContainer();column.AddChild(defenseRow);defenseRow.AddChild(Text("主选防御",14,Ink));
        _defenseMode=new OptionButton();defenseRow.AddChild(_defenseMode);
        foreach(var label in new[]{"自动选择","优先盾挡","只用招架","不主动防御"})_defenseMode.AddItem(label);
        UiAccess.Name(_defenseMode,"主选居民的近战防御方式","格挡与招架需要准备时间、朝向和体力；没有盾牌不能盾挡。");
        _defenseMode.ItemSelected+=index=>{if(_selectedWorker>=0)CombatNotice(_world.SetCombatDefense(_world.Workers[_selectedWorker].Id,new[]{"auto","block","parry","none"}[(int)index]));};
        var raid=new HBoxContainer();_raidTools=raid;column.AddChild(raid);
        foreach(int count in new[]{1,3}){int n=count;var b=Button(raid,$"测试袭击 · {n} 人",()=>ConfirmTestRaid(n));UiAccess.Name(b,$"生成 {n} 名敌人的测试袭击","确认后敌人从地图边缘入场，可能导致居民受伤或死亡。");}
        _locateEnemy=Button(column,"定位敌人",()=>{var e=_world.Enemies.FirstOrDefault(e=>!e.Person.Incapacitated);if(e is not null){_focus=WorldPosition(e.Tile);PositionCamera();}});
        HudTab(tabs,"防卫",()=>{TogglePanel(_combatPanel);RefreshUi();},"征召与平民集合");
        _combatPanel.Hide();
    }
    private void ConfirmTestRaid(int count)
    {
        var dialog=new ConfirmationDialog{Title="触发测试袭击",DialogText=$"将从地图边缘生成 {count} 名敌人。敌人会攻击居民，可能造成伤势或死亡。建议先保存。",OkButtonText="生成敌人",CancelButtonText="取消"};
        AddChild(dialog);dialog.Confirmed+=()=>{CombatNotice(_world.StartTestRaid(count));dialog.QueueFree();};dialog.Canceled+=()=>dialog.QueueFree();dialog.PopupCentered();
    }
    private void RefreshCombatUi()
    {
        if(_draftButton is null)return;
        var w=_selectedWorker>=0&&_selectedWorker<_world.Workers.Count?_world.Workers[_selectedWorker]:null;
        var selected=SelectedResidents();
        _defenseMode.Disabled=w is null;
        if(w is not null)_defenseMode.Select(Array.IndexOf(new[]{"auto","block","parry","none"},w.Combat.DefensePreference));
        _draftButton.Visible=w is not null;_combatStop.Visible=selected.Any(i=>_world.Workers[i].Combat.Drafted);
        _draftButton.Text=selected.Length>0&&selected.All(i=>_world.Workers[i].Combat.Drafted)?"解除征召 [R]":"征召 [R]";
        _draftButton.TooltipText="R 切换选中居民的征召状态；右键地面移动，右键敌人攻击。";
        _alertButton.Text=_world.CivilianAlert?"解除集合警报":"开启集合警报";
        int active=_world.Enemies.Count(e=>!e.Person.Incapacitated);
        _raidTools.Visible=_developerMode;_locateEnemy.Disabled=active==0;_locateEnemy.TooltipText=active==0?"当前没有活动敌人":"定位最近列出的活动敌人";
        _combatSummary.Text=$"征召 {_world.Workers.Count(w=>w.Combat.Drafted)} 人 · 活动敌人 {active}\n集合点：{(_world.RallyPoint is Cell c?$"{c.X}, {c.Z} / {c.Level}m":"未设置")}\n选中居民后按 R 征召；右键地面移动，右键敌人攻击。";
        if(w is not null)_combatSummary.Text+=$"\n{w.Name} · 体力 {w.Combat.Stamina:0}/100 · {w.Status}";
        if(_world.Enemies.Count>0&&_world.Workers.Count>0&&_world.Workers.All(w=>w.Person.Incapacitated))_combatSummary.Text+="\n全员失能：可暂停并读取此前存档。";
    }
    private void AnimateCombat()
    {
        if(_combatVisualWorld!=_world){foreach(var node in _enemyVisuals.Values)node.QueueFree();_enemyVisuals.Clear();_combatVisualWorld=_world;}
        foreach(var id in _enemyVisuals.Keys.Where(id=>!_world.Enemies.Any(e=>e.Id==id)).ToArray()){_enemyVisuals[id].QueueFree();_enemyVisuals.Remove(id);}
        foreach(var e in _world.Enemies)
        {
            if(!_enemyVisuals.TryGetValue(e.Id,out var node))
            {
                node=new Node3D();AddChild(node);_enemyVisuals[e.Id]=node;
                node.AddChild(new Sprite3D{Texture=EquipmentArt.Texture(e.Appearance,_world.Inventory(e)),PixelSize=WorldScale.DwarfHeightMeters/300f,Position=new(0,WorldScale.DwarfHeightMeters*.49f,0),Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Shaded=false,AlphaCut=SpriteBase3D.AlphaCutMode.Discard});
                node.AddChild(new Label3D{Position=new(0,1.65f,0),FontSize=32,PixelSize=.006f,Font=GameFont.Regular,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Modulate=new Color("ff9c88"),OutlineSize=8});
                var body=new StaticBody3D{CollisionLayer=16,CollisionMask=0,Position=new(0,.7f,0)};body.SetMeta("enemy",e.Id);node.AddChild(body);body.AddChild(new CollisionShape3D{Shape=new CapsuleShape3D{Radius=.3f,Height=1.4f}});
            }
            if(node.GetNodeOrNull<ActorModelView>("ModelView") is null)
            {AddActorModel(node);node.GetNode<ActorModelView>("ModelView").UpdateEquipment(_world.Inventory(e),e.Appearance.Beard);}
            node.Position=new((float)e.X-(DemoWorld.Width-1)/2f,_world.WorkerHeight(e),(float)e.Z-(DemoWorld.Depth-1)/2f);node.Visible=VisibleAt(e.Tile);
            node.GetChild<Label3D>(1).Text=e.Name+"\n"+(e.Person.Incapacitated?(e.Person.Dead?"死亡":"失能"):e.Status);
            var sprite=node.GetChild<Sprite3D>(0);sprite.Modulate=e.Combat.FlashUntil>_world.SimulationTime?Colors.White:new Color("ed8276");
            sprite.Rotation=new(0,0,e.Person.Incapacitated?Mathf.Pi/2:e.Combat.FlashUntil>_world.SimulationTime?.2f:0);
            node.GetChild<StaticBody3D>(2).CollisionLayer=node.Visible?16u:0u;
            UpdateActorModel(node,e);
        }
    }
}

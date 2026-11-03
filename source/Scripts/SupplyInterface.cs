using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private Control? _supplyBackdrop;
    private GameWindowFrame? _supplyFrame;
    private Button _forbidItemButton=null!,_supplyRuleButton=null!;
    private void CloseSupplyRules(){_supplyBackdrop?.QueueFree();_supplyBackdrop=null;_supplyFrame=null;}
    private void ShowSupplyRules(string item="cloth")
    {
        CloseSupplyRules();var world=_world;var host=_selectionPanel.GetParent<Control>();
        _supplyBackdrop=new Control{MouseFilter=Control.MouseFilterEnum.Stop,ZIndex=40};host.AddChild(_supplyBackdrop);_supplyBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _supplyFrame=new GameWindowFrame("物资使用",CloseSupplyRules);_supplyBackdrop.AddChild(_supplyFrame);_supplyFrame.FitCentered(_supplyBackdrop,new(720,580));
        var body=GameUi.Column(GameUi.Scroll(_supplyFrame.Body));
        var picker=GameUi.Field(body,"物品",new OptionButton());var items=world.Content.Items.ToArray();int selected=Array.FindIndex(items,i=>i.Id==item);if(selected<0)selected=0;item=items[selected].Id;
        foreach(var definition in items)picker.AddItem(definition.Name);picker.Select(selected);picker.TooltipText="切换物品会放弃尚未应用的设置。";picker.ItemSelected+=i=>ShowSupplyRules(items[(int)i].Id);
        var summary=GameUi.Label(body,world.ItemAvailability(item),wrap:true);
        var rule=world.SupplyReserves.FirstOrDefault(r=>r.Item==item);
        var minimum=GameUi.Field(body,"最低保留数量",new SpinBox{MinValue=0,MaxValue=1000000,Step=1,Value=rule?.Minimum??0});UiAccess.Name(minimum.GetLineEdit(),"最低保留数量");
        GameUi.Label(body,"保留部分允许这些用途使用",size:18);
        var uses=new Dictionary<string,CheckBox>();
        foreach(var use in world.SupplyUses.Where(u=>!u.Id.StartsWith("recipe:")||world.Content.Recipe(u.Id[7..]).Inputs.ContainsKey(item)))
        {var check=new CheckBox{Text=use.Name,ButtonPressed=rule?.AllowedUses.Contains(use.Id)==true};body.AddChild(check);UiAccess.Name(check,"允许"+use.Name+"使用保留物资");uses[use.Id]=check;}
        GameUi.Label(body,"保留量仅限制新预约，已预约和搬运的材料仍按原计划使用。搬运保留物资不会解除保留；禁用物品不允许任何用途领取。",wrap:true,muted:true);
        _supplyFrame.SetStatus("设为 0 会取消保留，未预约物资将对所有用途开放。关闭窗口放弃未应用修改。");
        _supplyFrame.AddAction("取消",CloseSupplyRules);
        _supplyFrame.AddAction("应用规则",()=>
        {
            if(world!=_world){CloseSupplyRules();return;}
            var result=world.ConfigureSupplyReserve(item,(int)minimum.Value,uses.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key));
            _supplyFrame!.SetStatus(result.Message);summary.Text=world.ItemAvailability(item);RefreshUi();
        },true);
    }
    private void BuildSupplyCommands()
    {
        _forbidItemButton=IconButton(_objectCommands,"cancel","禁用物品",()=>
        {if(_world.Items.FirstOrDefault(s=>s.Id==_selectedItemId) is {} stack){var result=_world.SetItemForbidden(stack.Id,!stack.Forbidden);ShowActionFeedback(result.Message,!result.Ok);RefreshUi();}},true);
        StyleObjectCommand(_forbidItemButton,"cancel");
        _supplyRuleButton=IconButton(_objectCommands,"stockpile","物资规则",()=>{if(_world.Items.FirstOrDefault(s=>s.Id==_selectedItemId) is {} stack)ShowSupplyRules(stack.Item);},true);StyleObjectCommand(_supplyRuleButton,"stockpile");UiAccess.Name(_supplyRuleButton,"物资规则");
    }
    private void RefreshSupplyCommands(ItemStack? stack)
    {
        _forbidItemButton.Visible=_supplyRuleButton.Visible=stack is not null;if(stack is null)return;
        _forbidItemButton.Text=stack.Forbidden?"恢复使用":"禁用物品";
        _forbidItemButton.TooltipText=stack.Forbidden?"恢复这堆物品的取料、取食、照料和搬运。":"停止使用这堆物品并释放相关预约；物品保留，可能造成断粮或医疗缺料。";
        UiAccess.Name(_forbidItemButton,_forbidItemButton.Text,_forbidItemButton.TooltipText);
        _selection.Text+="\n"+(stack.Forbidden?"此堆已禁用\n":"")+_world.ItemAvailability(stack.Item);
    }
}

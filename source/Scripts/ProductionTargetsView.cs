using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private OptionButton _targetRecipe=null!;
    private Button _applyProductionTarget=null!;
    private SpinBox _targetCount=null!;
    private VBoxContainer _targetRows=null!;
    private readonly Dictionary<string,(Label Text,Button Pause,Control Row)> _productionTargetRows=new();
    private DemoWorld? _targetWorld;
    private void BuildProductionTargets(VBoxContainer parent)
    {
        parent.AddChild(new HSeparator());parent.AddChild(Text("自动补产 · 按库存目标",15,Accent));
        parent.AddChild(Text("生产物品",13,Ink));
        _targetRecipe=new OptionButton{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};parent.AddChild(_targetRecipe);UiAccess.Name(_targetRecipe,"生产物品");
        foreach(var r in ProductionCatalog.All)_targetRecipe.AddItem(r.Name.Split('：')[0]);
        parent.AddChild(Text("目标库存",13,Ink));
        var settings=new VBoxContainer();parent.AddChild(settings);
        _targetCount=new SpinBox{MinValue=1,MaxValue=10000,Step=1,Value=20,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};settings.AddChild(_targetCount);
        UiAccess.Name(_targetCount,"目标库存","包含在制品和搬运途中库存，不含个人装备。");
        _targetCount.TooltipText=_targetCount.AccessibilityDescription;
        _applyProductionTarget=Button(settings,"应用库存目标",()=>{_world.SetProductionTarget(ProductionCatalog.All[_targetRecipe.Selected].Id,(int)_targetCount.Value);RefreshUi();});
        _targetRecipe.ItemSelected+=i=>{var r=ProductionCatalog.All[(int)i];_targetCount.Value=_world.ProductionOrders.FirstOrDefault(o=>o.Recipe==r.Id)?.Target??20;_targetRecipe.TooltipText=r.Name;};
        parent.AddChild(Text("暂停或移除目标后，已下单批次仍会继续。",12,Ink,true));
        _targetRows=new VBoxContainer();parent.AddChild(_targetRows);parent.AddChild(new HSeparator());parent.AddChild(Text("手动加工 · 每次一批",15,Accent));
    }
    private void RefreshProductionTargets()
    {
        if(_targetRows is null||!_inspector.Visible)return;
        if(_targetWorld!=_world){Clear(_targetRows);_productionTargetRows.Clear();_targetWorld=_world;}
        foreach(var id in _productionTargetRows.Keys.Where(id=>!_world.ProductionOrders.Any(o=>o.Recipe==id)).ToArray())
        {var row=_productionTargetRows[id].Row;_targetRows.RemoveChild(row);row.QueueFree();_productionTargetRows.Remove(id);}
        foreach(var order in _world.ProductionOrders)
        {
            if(!_productionTargetRows.TryGetValue(order.Recipe,out var row))
            {
                string id=order.Recipe;var box=new VBoxContainer();_targetRows.AddChild(box);
                var label=Text("",13,Ink,true);box.AddChild(label);var buttons=new HBoxContainer();box.AddChild(buttons);
                var pause=Button(buttons,"暂停",()=>{var current=_world.ProductionOrders.FirstOrDefault(o=>o.Recipe==id);if(current is not null)_world.SetProductionTarget(id,current.Target,!current.Enabled);RefreshUi();});
                var remove=Button(buttons,"移除目标",()=>{_world.SetProductionTarget(id,0);RefreshUi();});
                UiAccess.Name(remove,"移除库存目标："+ProductionCatalog.Get(id).Name.Split('：')[0],"停止后续补产，保留已下单批次。");
                remove.TooltipText=remove.AccessibilityDescription;row=(label,pause,box);_productionTargetRows[id]=row;
            }
            row.Text.Text=ProductionCatalog.Get(order.Recipe).Name.Split('：')[0]+" · "+_world.ProductionTargetStatus(order);
            row.Pause.Text=order.Enabled?"暂停补产":"恢复补产";
            row.Pause.AccessibilityName=row.Pause.Text+"："+ProductionCatalog.Get(order.Recipe).Name.Split('：')[0];
        }
    }
}

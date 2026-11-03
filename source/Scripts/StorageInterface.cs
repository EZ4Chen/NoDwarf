using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private PanelContainer? _storagePanel;
    private Control? _storageBackdrop;
    private void CloseStorage(){_storageBackdrop?.QueueFree();_storageBackdrop=null;_storagePanel=null;}
    private void ShowStorage(StorageZone zone)
    {
        CloseStorage();var editedWorld=_world;var host=_selectionPanel.GetParent<Control>();
        _storageBackdrop=new Control{MouseFilter=Control.MouseFilterEnum.Stop,ZIndex=40};host.AddChild(_storageBackdrop);_storageBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var frame=new GameWindowFrame($"堆场 #{zone.Id} · {zone.Cells.Count} 格",CloseStorage);_storagePanel=frame;_storageBackdrop.AddChild(frame);frame.FitCentered(_storageBackdrop,new(660,560));
        var column=GameUi.Column(GameUi.Scroll(frame.Body));
        GameUi.Label(column,$"已存 {zone.Cells.Sum(c=>editedWorld.StoredUnits(c))} / {zone.Cells.Count*DemoWorld.CellStorageCapacity} 容量 · 预约 {zone.Cells.Sum(c=>editedWorld.ReservedStorageUnits(c))}",wrap:true);
        GameUi.Label(column,"每格 100 容量；物品按定义占容，落地与在途预约合计。满格停止派送，扩仓或清运后恢复。",muted:true,wrap:true);
        var priority=GameUi.Field(column,"搬运优先级",new OptionButton());foreach(string name in new[]{"1 · 低","2 · 较低","3 · 普通","4 · 较高","5 · 最高"})priority.AddItem(name);priority.Selected=zone.Priority-1;
        GameUi.Label(column,"允许储存的物品（不接收不代表禁止使用）");
        var presets=new HBoxContainer();column.AddChild(presets);var toggles=new Dictionary<string,CheckBox>();
        void Preset(Func<string,bool> include){foreach(var p in toggles)p.Value.ButtonPressed=include(p.Key);}
        GameUi.Action(presets,"全部",()=>Preset(_=>true));GameUi.Action(presets,"全不选",()=>Preset(_=>false));
        GameUi.Action(presets,"食物",()=>Preset(i=>editedWorld.Content.Item(i) is {Edible:not null} or {Category:"food" or "food_ingredient"}));GameUi.Action(presets,"种子",()=>Preset(i=>CropCatalog.All.Any(c=>c.Seed==i)));
        foreach(var item in editedWorld.Content.Items)
        {var check=new CheckBox{Text=$"{item.Name} · 每个占 {item.StorageUnits}",ButtonPressed=zone.Allowed.Contains(item.Id)};column.AddChild(check);UiAccess.Name(check,item.Name+"允许入库");toggles[item.Id]=check;}
        frame.SetStatus("移除接收种类会取消相关派送，已在库物资保留。");
        frame.AddAction("取消",CloseStorage);
        frame.AddAction("应用设置",()=>{if(_world==editedWorld)_world.ConfigureStorage(zone.Id,priority.Selected+1,toggles.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key));CloseStorage();RefreshUi();},true);
    }
}

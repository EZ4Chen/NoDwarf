using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private PanelContainer? _storagePanel;
    private void ShowStorage(StorageZone zone)
    {
        if(_storagePanel is not null){_storagePanel.QueueFree();_storagePanel=null;}
        var editedWorld=_world;
        var panel=Panel((Control)_selectionPanel.GetParent(),new Vector2(120,100),new Vector2(360,460));_storagePanel=panel;
        var column=new VBoxContainer();panel.AddChild(column);
        column.AddChild(Text($"堆场 #{zone.Id} · {zone.Cells.Count} 格",18,Accent));
        column.AddChild(Text("修改后点击应用设置",12,Muted));
        column.AddChild(Text("搬运优先级",13,Ink));
        var priority=new OptionButton();foreach(string name in new[]{"1 · 低","2 · 较低","3 · 普通","4 · 较高","5 · 最高"})priority.AddItem(name);
        priority.Selected=zone.Priority-1;column.AddChild(priority);UiAccess.Name(priority,"搬运优先级","优先搬入数值更高的储存区。");
        column.AddChild(Text("允许储存的物品",13,Ink));
        var scroll=new ScrollContainer{CustomMinimumSize=new Vector2(340,300)};column.AddChild(scroll);
        var checks=new VBoxContainer();scroll.AddChild(checks);var toggles=new Dictionary<string,CheckBox>();
        foreach(var item in ItemCatalog.All)
        {var check=new CheckBox{Text=item.Name,ButtonPressed=zone.Allowed.Contains(item.Id),Icon=UiIcons.Get(item.Id)};check.ExpandIcon=true;check.AddThemeConstantOverride("icon_max_width",24);check.CustomMinimumSize=new Vector2(280,30);checks.AddChild(check);toggles[item.Id]=check;}
        var presets=new HBoxContainer();column.AddChild(presets);
        void Preset(Func<string,bool> include){foreach(var p in toggles)p.Value.ButtonPressed=include(p.Key);}
        Button(presets,"全部",()=>Preset(_=>true));Button(presets,"全不选",()=>Preset(_=>false));
        Button(presets,"食物",()=>Preset(i=>i is "meal" or "raw_food" or "wheat" or "flour" or "potato" or "mushroom"));Button(presets,"种子",()=>Preset(i=>CropCatalog.All.Any(c=>c.Seed==i)));
        var actions=new HBoxContainer();column.AddChild(actions);
        Button(actions,"应用设置",()=>{if(_world==editedWorld)_world.ConfigureStorage(zone.Id,priority.Selected+1,toggles.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key));panel.QueueFree();_storagePanel=null;RefreshUi();});
        Button(actions,"取消",()=>{panel.QueueFree();_storagePanel=null;});
    }
}

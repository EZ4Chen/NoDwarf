using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private Cell? _farmStart;
    private ConfirmationDialog? _removeGrowingDialog;
    private Node3D? _farmsView;
    private DemoWorld? _farmWorld;
    private readonly Dictionary<Cell,(Node3D Root,string Crop,int Stage)> _farmNodes=new();
    private VBoxContainer _growingSettings=null!;
    private OptionButton _growingCrop=null!;
    private CheckButton _growingSowing=null!;
    private Label _growingHelp=null!;
    private Node3D? _growingOutline;
    private int _outlinedZone=-1;
    private long _outlinedRevision=-1;
    private DemoWorld? _outlinedWorld;
    private WorldView _outlinedView;
    private HashSet<Cell> _outlineCells=new();
    private static readonly string[] GrowingCrops=CropCatalog.All.Select(c=>c.Id).ToArray();
    private GrowingZone? SelectedGrowingZone=>_selectedWorker<0&&_selectedCell is Cell c?_world.GrowingZoneAt(c):null;
    private void BuildGrowingZoneSettings(VBoxContainer parent)
    {
        _growingSettings=new VBoxContainer();parent.AddChild(_growingSettings);
        _growingSettings.AddChild(new HSeparator());
        var row=new HBoxContainer();_growingSettings.AddChild(row);row.AddChild(Text("种植作物",14,Ink));
        _growingSettings.AddChild(Text("修改即生效",12,Muted));
        _growingCrop=new OptionButton{CustomMinimumSize=new(210,32),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};row.AddChild(_growingCrop);
        UiAccess.Name(_growingCrop,"种植作物","选择即生效；现有作物收获后换种。");
        foreach(string crop in GrowingCrops)_growingCrop.AddItem(DemoWorld.CropName(crop));
        _growingCrop.ItemSelected+=index=>{if(SelectedGrowingZone is {} zone){_world.ConfigureGrowingZone(zone.Id,GrowingCrops[(int)index],zone.AllowSowing);SyncWorld();RefreshUi();}};
        _growingSowing=new CheckButton{Text="允许播种 / 补种",TooltipText="关闭后停止新播种，已种作物仍正常生长和收获。"};_growingSettings.AddChild(_growingSowing);
        _growingSowing.Toggled+=value=>{if(SelectedGrowingZone is {} zone){_world.ConfigureGrowingZone(zone.Id,zone.Crop,value);SyncWorld();RefreshUi();}};
        _growingHelp=Text("",12,Muted,true);_growingSettings.AddChild(_growingHelp);
        _growingSettings.Hide();
    }
    private void MarkFarm(Cell c)
    {
        if(_farmStart is not Cell start){_farmStart=c;_hint.Text="选择同高的另一角；右键取消。";return;}
        _farmStart=null;
        if(_tool=="unfarm"){RequestRemoveGrowingArea(start,c);return;}
        bool ok=_world.DesignateGrowingZone(start,c);
        if(!ok)_world.Log("未改变种植区：请选择同高、平整、空闲的天然土壤或地下岩石地面，范围最多 16×16。");
        SyncWorld();RefreshUi();
    }
    private void RequestRemoveGrowingArea(Cell start,Cell end)
    {
        if(_removeGrowingDialog?.Visible==true)return;
        if(start.Level!=end.Level||Math.Abs(start.X-end.X)>15||Math.Abs(start.Z-end.Z)>15)
        {_world.Log("请选择同高、最多 16×16 格的范围。");return;}
        var plots=_world.Farms.Values.Where(p=>p.Cell.Level==start.Level&&p.Cell.X>=Math.Min(start.X,end.X)&&p.Cell.X<=Math.Max(start.X,end.X)&&p.Cell.Z>=Math.Min(start.Z,end.Z)&&p.Cell.Z<=Math.Max(start.Z,end.Z)).ToArray();
        if(plots.Length==0){_world.Log("范围内没有种植区。");return;}
        var world=_world;bool paused=_clock.Paused;_clock.Paused=true;
        _removeGrowingDialog?.QueueFree();
        var dialog=new ConfirmationDialog{Title="移除种植区",OkButtonText="确认移除",CancelButtonText="保留种植区",
            DialogText=$"将移除 {plots.Length} 格种植区，现有植物保留并继续生长。\n区域内自动播种与收获任务会取消。\n此操作无法撤销。"};
        _removeGrowingDialog=dialog;AddChild(dialog);
        dialog.Confirmed+=()=>{if(_world==world){_world.SetFarm(start,end,"grain",true);_clock.Paused=paused;}SyncWorld();RefreshUi();};
        dialog.Canceled+=()=>{if(_world==world)_clock.Paused=paused;SelectTool("select");};
        _hoverCell=null;ClearHover();dialog.PopupCentered(new Vector2I(460,200));
        dialog.GetCancelButton().GrabFocus();
    }
    private void DrawFarmPreview(Cell end)
    {
        var start=_farmStart??end;
        float width=Math.Abs(end.X-start.X)+1,depth=Math.Abs(end.Z-start.Z)+1;
        var center=WorldPosition(start)+new Vector3((end.X-start.X)*.5f,.045f,(end.Z-start.Z)*.5f);
        var material=PreviewMaterial(end.Level!=start.Level||width>16||depth>16);
        foreach(float side in new[]{-1f,1f})
        {Box(_hover,center+new Vector3(side*width/2,0,0),new Vector3(.04f,.04f,depth),material);Box(_hover,center+new Vector3(0,0,side*depth/2),new Vector3(width,.04f,.04f),material);}
    }
    private void DrawFarms()
    {
        if(_farmsView is null){_farmsView=new Node3D();AddChild(_farmsView);}
        if(_farmWorld!=_world){Clear(_farmsView);_farmNodes.Clear();_farmWorld=_world;}
        foreach(var c in _farmNodes.Keys.Where(c=>!_world.Plants.ContainsKey(c)).ToArray())
        {var old=_farmNodes[c].Root;_farmsView.RemoveChild(old);old.QueueFree();_farmNodes.Remove(c);}
        foreach(var p in _world.Plants.Values)
        {
            int stage=CropVisuals.Stage(p);
            if(_farmNodes.TryGetValue(p.Cell,out var existing)&&(existing.Stage!=stage||existing.Crop!=p.Crop))
            {_farmsView.RemoveChild(existing.Root);existing.Root.QueueFree();_farmNodes.Remove(p.Cell);}
            if(!_farmNodes.TryGetValue(p.Cell,out var entry))
            {
                var root=new Node3D{Position=WorldPosition(p.Cell)};_farmsView.AddChild(root);
                CropVisuals.AddTo(root,p.Crop,stage,p.Prepared);
                entry=(root,p.Crop,stage);_farmNodes[p.Cell]=entry;
            }
            entry.Root.Visible=VisibleAt(p.Cell);
        }
    }
    private void RefreshFarms()
    {
        var zone=SelectedGrowingZone;
        _growingSettings.Visible=zone is not null;
        BottomLeft(_selectionPanel,zone is not null?400:282,400);_selectionPanel.OffsetBottom=-76;
        if(zone is not null&&_selectedCell is Cell c&&_world.Farms.TryGetValue(c,out var plot))
        {
            var plots=_world.Farms.Values.Where(p=>p.ZoneId==zone.Id).ToArray();
            int planted=plots.Count(p=>_world.Plants.ContainsKey(p.Cell)),ready=plots.Count(p=>_world.Plants.TryGetValue(p.Cell,out var plant)&&plant.Growth>=DemoWorld.GrowTime(plant.Crop));
            var climate=_world.FarmClimate(_world.Plants.GetValueOrDefault(c) as PlantSite??plot);
            _selection.Text=$"种植区 #{zone.Id} · {plots.Length} 格\n未成熟 {planted-ready} · 待收获 {ready}\n选中地格：{_world.FarmStatus(plot)}\n温度 {climate.Temperature:F1}°C · 日光 {climate.Light:P0} · 肥力 {climate.Fertility:P0}";
            _growingCrop.Select(Array.IndexOf(GrowingCrops,zone.Crop));_growingSowing.SetPressedNoSignal(zone.AllowSowing);
            _growingHelp.Text=CropCatalog.Get(zone.Crop).Description+"\n设置作用于整个区域。现有作物收获后按新设置续种。";
        }
        RefreshGrowingOutline(zone);
    }
    private void RefreshGrowingOutline(GrowingZone? zone)
    {
        if(_growingOutline is null){_growingOutline=new Node3D();AddChild(_growingOutline);}
        _growingOutline.Visible=zone is not null&&_selectionPanel.Visible;
        if(_outlinedWorld==_world&&_outlinedZone==(zone?.Id??-1)&&_outlinedRevision==_world.Revision&&_outlinedView==CurrentView)return;
        var cells=zone is null?new HashSet<Cell>():_world.Farms.Values.Where(p=>p.ZoneId==zone.Id&&VisibleAt(p.Cell)).Select(p=>p.Cell).ToHashSet();
        bool same=_outlinedWorld==_world&&_outlinedZone==(zone?.Id??-1)&&_outlinedView==CurrentView&&_outlineCells.SetEquals(cells);
        _outlinedWorld=_world;_outlinedZone=zone?.Id??-1;_outlinedRevision=_world.Revision;_outlinedView=CurrentView;_outlineCells=cells;
        if(same)return; // Work progress changes the world revision, but not the zone boundary.
        Clear(_growingOutline);
        if(zone is null)return;
        var material=Mat("e4d7a4");
        foreach(var p in _world.Farms.Values.Where(p=>p.ZoneId==zone.Id&&VisibleAt(p.Cell)))
        foreach(var d in new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)})
            if(_world.GrowingZoneAt(p.Cell+d)?.Id!=zone.Id)
                Box(_growingOutline,WorldPosition(p.Cell)+new Vector3(d.X*.49f,.065f,d.Z*.49f),new Vector3(d.X==0?1:.05f,.04f,d.Z==0?1:.05f),material);
    }
}

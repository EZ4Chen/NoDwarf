using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private Cell? _farmStart;
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
        _growingCrop=new OptionButton{CustomMinimumSize=new(210,32),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};row.AddChild(_growingCrop);
        foreach(string crop in GrowingCrops)_growingCrop.AddItem(DemoWorld.CropName(crop));
        _growingCrop.ItemSelected+=index=>{if(SelectedGrowingZone is {} zone){_world.ConfigureGrowingZone(zone.Id,GrowingCrops[(int)index],zone.AllowSowing);SyncWorld();RefreshUi();}};
        _growingSowing=new CheckButton{Text="允许播种 / 补种",TooltipText="关闭后停止新播种，已种作物仍正常生长和收获。"};_growingSettings.AddChild(_growingSowing);
        _growingSowing.Toggled+=value=>{if(SelectedGrowingZone is {} zone){_world.ConfigureGrowingZone(zone.Id,zone.Crop,value);SyncWorld();RefreshUi();}};
        _growingHelp=Text("",12,Muted,true);_growingSettings.AddChild(_growingHelp);
        _growingSettings.Hide();
    }
    private void MarkFarm(Cell c)
    {
        if(_farmStart is not Cell start){_farmStart=c;_world.Log("农田：选择同高的另一角，最多 16×16 格；右键取消。");return;}
        bool ok=_tool=="unfarm"?_world.SetFarm(start,c,"grain",true):_world.DesignateGrowingZone(start,c);_farmStart=null;
        if(!ok)_world.Log("未改变种植区：请选择同高、平整、空闲的天然土壤或地下岩石地面，范围最多 16×16。");
        SyncWorld();RefreshUi();
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
        foreach(var c in _farmNodes.Keys.Where(c=>!_world.Farms.ContainsKey(c)).ToArray())
        {var old=_farmNodes[c].Root;_farmsView.RemoveChild(old);old.QueueFree();_farmNodes.Remove(c);}
        foreach(var p in _world.Farms.Values)
        {
            int stage=p.Planted?1+(int)(p.Growth/DemoWorld.GrowTime(p.Crop)*3):0;
            if(_farmNodes.TryGetValue(p.Cell,out var existing)&&(existing.Stage!=stage||existing.Crop!=p.Crop))
            {_farmsView.RemoveChild(existing.Root);existing.Root.QueueFree();_farmNodes.Remove(p.Cell);}
            if(!_farmNodes.TryGetValue(p.Cell,out var entry))
            {
                var root=new Node3D{Position=WorldPosition(p.Cell)};_farmsView.AddChild(root);
                Box(root,new Vector3(0,.018f,0),new Vector3(.92f,.025f,.92f),Mat(p.Crop=="mushroom"?"594f65":"70533d"));
                var outline=Mat(p.Crop=="mushroom"?"b49bc7":"a2b86a");
                foreach(float side in new[]{-.46f,.46f})
                {Box(root,new Vector3(side,.035f,0),new Vector3(.025f,.025f,.94f),outline);Box(root,new Vector3(0,.035f,side),new Vector3(.94f,.025f,.025f),outline);}
                if(stage>0)
                foreach(float x in new[]{-.23f,.23f})foreach(float z in new[]{-.23f,.23f})
                {
                    float h=.08f+stage*.09f;
                    Box(root,new Vector3(x,h/2+.04f,z),new Vector3(.045f,h,.045f),Mat(p.Crop=="mushroom"?"d8cebc":"7e9c4d"));
                    Box(root,new Vector3(x,h+.04f,z),p.Crop=="grain"?new Vector3(.09f,.12f,.06f):new Vector3(.12f+stage*.035f,.07f,.12f+stage*.035f),Mat(p.Crop switch {"cotton"=>stage==4?"f2ede4":"a3ba75","potato"=>stage==4?"af8c62":"6f994c","grain"=>stage==4?"e0c673":"9fbb60",_=>"b78da8"}));
                }
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
            int planted=plots.Count(p=>p.Planted),ready=plots.Count(p=>p.Planted&&p.Growth>=DemoWorld.GrowTime(p.Crop));
            var climate=_world.FarmClimate(plot);
            _selection.Text=$"种植区 #{zone.Id} · {plots.Length} 格\n未成熟 {planted-ready} · 待收获 {ready}\n选中地格：{_world.FarmStatus(plot)}\n温度 {climate.Temperature:F1}°C · 日光 {climate.Light:P0}";
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

public partial class Main
{
    private async void CaptureFarmPreview()
    {
        try
        {
            _clock.Paused=true;_world=new DemoWorld();_world.Stockpiles.Clear();_overview.World=_world;_terrainRevision=-1;
            var origin=FarmingChecks.SurfacePlot(_world);SelectTool("growingzone");Act(origin);Act(origin+new Cell(2,1));
            if(_world.Farms.Count!=6)throw new InvalidOperationException("Two-corner farm UI failed");
            for(int i=0;i<120*30&&!_world.Farms.Values.All(p=>p.Planted);i++)_world.Update(1d/30);
            if(!_world.Farms.Values.All(p=>p.Planted))throw new InvalidOperationException("Residents did not plant preview plots");
            _world.DeveloperResidents(false);for(int i=0;i<65*30;i++)_world.Update(1d/30);
            _world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;
            _foundation=null;_heightSelected=false;_layerOffset=0;_cameraYaw=0;_topDown=false;_focus=WorldPosition(origin+new Cell(1,1));_camera.Size=12;
            _inspector.Hide();_selectionPanel.Hide();_worldMenu.Hide();CreatePeople();PositionCamera();SetViewMode(0);
            ShowBuildPage("区域");_buildMenu.Show();SelectTool("growingzone");SyncWorld();RefreshUi();
            async Task Snap(string name)
            {
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/{name}.png"))!=Error.Ok)throw new IOException("Farm screenshot failed");
            }
            await Snap("grain-farm-preview");
            if(Pick(_camera.UnprojectPosition(WorldPosition(origin)))!=origin)throw new InvalidOperationException("Farm selection must hit its ground cell");
            if(PickTarget(_camera.UnprojectPosition(WorldPosition(origin)))!=origin)throw new InvalidOperationException("Clicking terrain must select the zone above its soil block");
            if(_farmNodes.Count!=6||_categoryButtons["区域"].GetGlobalRect().Intersects(_buildTab.GetGlobalRect()))throw new InvalidOperationException("Farm visuals or category layout failed");
            SelectTool("select");Act(origin);await Snap("growing-zone-settings");
            if(!_growingSettings.Visible||_architectTools.Visible||SelectedGrowingZone is null)throw new InvalidOperationException("Selecting a plot must open its zone settings");
            _growingSowing.ButtonPressed=false;
            if(SelectedGrowingZone.AllowSowing)throw new InvalidOperationException("Zone checkbox did not disable sowing");
            _growingCrop.EmitSignal(OptionButton.SignalName.ItemSelected,(long)Array.IndexOf(GrowingCrops,"mushroom"));
            if(SelectedGrowingZone.Crop!="mushroom"||_world.Farms.Values.Any(f=>f.Crop!="grain"))throw new InvalidOperationException("Changing zone crop must preserve existing grain");
            _world.ConfigureGrowingZone(SelectedGrowingZone.Id,"grain",true);
            var p=new Cell(20,20,-5);
            for(int x=-1;x<=3;x++)for(int z=-1;z<=2;z++)for(int y=0;y<2;y++)_world.Terrain.Excavate(p+new Cell(x,z,y));
            _world.SetFarm(p,p+new Cell(2,1),"mushroom");_world.DeveloperResidents(true);
            foreach(var worker in _world.Workers){worker.X=p.X-1;worker.Z=p.Z;worker.Level=p.Level;}
            _world.SpawnItem("mushroom_spawn",6,p+new Cell(-1,0));_world.SpawnItem("soil",6,p+new Cell(-1,0));
            for(int i=0;i<120*30&&!_world.Farms.Values.Where(f=>f.Crop=="mushroom").All(f=>f.Planted);i++)_world.Update(1d/30);
            if(!_world.Farms.Values.Where(f=>f.Crop=="mushroom").All(f=>f.Planted))throw new InvalidOperationException("Cave plots never planted");
            _world.DeveloperResidents(false);for(int i=0;i<90*30;i++)_world.Update(1d/30);
            _foundation=new FoundationPlan(p,p+new Cell(2,1));_heightSelected=true;_layerOffset=0;_focus=WorldPosition(p+new Cell(1,1));_camera.Size=11;
            _buildMenu.Hide();CreatePeople();PositionCamera();SetViewMode(1);SyncWorld();RefreshUi();await Snap("mushroom-farm-preview");
            GD.Print("FARM_UI_OK: two-corner tools, seed delivery, staged crop visuals, save/load and cave farms");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

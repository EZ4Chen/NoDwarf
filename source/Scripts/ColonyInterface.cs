using Godot;
using NoDwarf.Simulation;
using System.Text.Json;
namespace NoDwarf;

public partial class Main
{
    private Node3D? _itemsView;
    private Cell? _stockpileStart;
    private Button _angleButton=null!;
    private ConfirmationDialog? _loadDialog;
    private void ToggleTopDown()
    {
        _topDown=!_topDown;PositionCamera();_revision=-1;_hoverCell=null;Clear(_hover);
        _angleButton.Text=_topDown?"斜视":"俯视";
    }
    private void MarkStockpile(Cell c)
    {
        if(_stockpileStart is not Cell start){_stockpileStart=c;_world.Log("堆场：点击同一高度的另一角（最大 31×31）。");return;}
        bool ok=_world.SetStockpile(start,c,_tool=="unstockpile");_stockpileStart=null;
        _world.Log(ok?"堆场已更新，居民空闲时会搬入散落物资。":"堆场两角须同高且在范围内。");SyncWorld();RefreshUi();
        if(ok&&_tool=="stockpile"&&_world.StorageZones.LastOrDefault() is {} zone)ShowStorage(zone);
    }
    private DemoWorld? _visualWorld,_itemWorld;
    private WorldView _visualView;
    private readonly Dictionary<int,(Node3D Root,Label3D Label,StaticBody3D Body,string Item)> _itemNodes=new();
    private readonly Dictionary<Cell,Node3D> _stockNodes=new();
    private VBoxContainer _orderList=null!;
    private Label _emptyOrders=null!;
    private void DrawItems()
    {
        using var timing = _world.Probe?.Measure("view.sync-items") ?? default;
        if(_itemsView is null){_itemsView=new Node3D();AddChild(_itemsView);}
        if(_itemWorld!=_world){Clear(_itemsView);_itemNodes.Clear();_stockNodes.Clear();_itemWorld=_world;}
        void Remove(Node3D node){_itemsView.RemoveChild(node);node.QueueFree();}
        foreach(var c in _stockNodes.Keys.Where(c=>!_world.Stockpiles.Contains(c)).ToArray())
        {Remove(_stockNodes[c]);_stockNodes.Remove(c);}
        foreach(var c in _world.Stockpiles)
        {
            if(!_stockNodes.TryGetValue(c,out var root))
            {
                root=new Node3D{Position=WorldPosition(c)+Vector3.Up*.015f};_itemsView.AddChild(root);_stockNodes[c]=root;
                var color=Mat("6e897b");
                foreach(float side in new[]{-.46f,.46f})
                {Box(root,new Vector3(side,0,0),new Vector3(.018f,.018f,.94f),color);Box(root,new Vector3(0,0,side),new Vector3(.94f,.018f,.018f),color);}
            }
            root.Visible=VisibleAt(c);
        }
        var stacks=_world.Items.Where(s=>s.Owner=="").ToArray();var ids=stacks.Select(s=>s.Id).ToHashSet();
        foreach(int id in _itemNodes.Keys.Where(id=>!ids.Contains(id)).ToArray()){Remove(_itemNodes[id].Root);_itemNodes.Remove(id);}
        foreach(var group in stacks.GroupBy(s=>s.Cell))
        {
            int index=0;
            foreach(var stack in group)
            {
                if(_itemNodes.TryGetValue(stack.Id,out var old)&&old.Item!=stack.Item){Remove(old.Root);_itemNodes.Remove(stack.Id);}
                if(!_itemNodes.TryGetValue(stack.Id,out var entry))
                {
                    var root=new Node3D();_itemsView.AddChild(root);
                    var def=ItemCatalog.Get(stack.Item);
                    string? spriteId=stack.Item switch{"raw_iron"=>"raw_iron","iron"=>"iron_ingot","sword"=>"iron_sword","coal"=>"coal",_=>null};
                    if(EquipmentArt.Icon(stack.Item) is {} equipment)root.AddChild(new Sprite3D{Texture=equipment,PixelSize=.45f/Math.Max(equipment.GetWidth(),equipment.GetHeight()),Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Shaded=false});
                    else if(stack.Item is "raw_food" or "meal" or "grain_seed" or "mushroom_spawn")root.AddChild(new Sprite3D{Texture=UiIcons.Get(stack.Item),PixelSize=.007f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Shaded=false});
                    else if(spriteId is null)BlockTextures.Box(root,Vector3.Zero,new Vector3(.27f,.23f,.27f),def.Texture);
                    else root.AddChild(new Sprite3D{Texture=GD.Load<Texture2D>($"res://Assets/Textures/Items/{spriteId}.png"),PixelSize=.007f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Shaded=false});
                    var label=new Label3D{Position=Vector3.Up*.22f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,FontSize=28,PixelSize=.004f,OutlineSize=8,NoDepthTest=false};root.AddChild(label);
                    var body=new StaticBody3D{CollisionLayer=2,CollisionMask=0};root.AddChild(body);
                    body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new Vector3(.3f,.3f,.3f)}});
                    entry=(root,label,body,stack.Item);_itemNodes[stack.Id]=entry;
                }
                bool visible=VisibleAt(stack.Cell);entry.Root.Visible=visible;entry.Body.CollisionLayer=visible?2u:0u;
                entry.Root.Position=WorldPosition(stack.Cell)+new Vector3((index%2)*.32f-.16f,.14f,(index/2%2)*.32f-.16f);index++;
                entry.Label.Text=stack.Count.ToString();entry.Body.SetMeta("cell",new Vector3I(stack.Cell.X,stack.Cell.Level,stack.Cell.Z));
            }
        }
    }
    private bool SessionInput(InputEvent input)
    {
        if(DossierOpen)return false;
        if(input is not InputEventKey{Pressed:true,Echo:false} key||GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit||_consolePanel.Visible||_characterEditor.Visible)return false;
        if(key.Keycode==Key.F5)SaveSession();
        else if(key.Keycode==Key.F9)RequestLoadSession();
        else if(key.Keycode==Key.V)ToggleTopDown();
        else return false;
        GetViewport().SetInputAsHandled();return true;
    }
    public sealed class SessionState
    {
        public int Version {get;set;}=1;
        public string World {get;set;}="";
        public long Tick {get;set;} public int Speed {get;set;} public bool Paused {get;set;} public double Pending {get;set;}
        public bool Developer {get;set;} public bool Creative {get;set;}
        public Cell? FoundationMin {get;set;} public Cell? FoundationMax {get;set;} public int Floor {get;set;} public int FloorHeight {get;set;}=3;
        public int LayerOffset {get;set;} public int Placement {get;set;} public bool HeightSelected {get;set;}
        public int ViewMode {get;set;} public bool TopDown {get;set;} public float Yaw {get;set;} public float Zoom {get;set;}
        public float Pitch {get;set;}=DefaultCameraPitch;
        public float FocusX {get;set;} public float FocusY {get;set;} public float FocusZ {get;set;}
    }
    private string? _activeSavePath;
    private string SavePath=>_activeSavePath??ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Contains("--colony-preview-test")?"res://.local/test-colony-save.json":"user://saves/colony-save.json");
    private string LegacySavePath=>ProjectSettings.GlobalizePath("res://.local/colony-save.json");
    private string ReadSavePath=>ResolveReadSavePath(SavePath,LegacySavePath,_activeSavePath is not null||OS.GetCmdlineUserArgs().Contains("--colony-preview-test"));
    private static string ResolveReadSavePath(string current,string legacy,bool hasActiveColony)=>hasActiveColony?current:SaveFiles.ReadPath(current,legacy);
    private void SaveSession()=>TrySaveSession();
    private bool TrySaveSession()
    {
        try
        {
            var state=new SessionState{World=_world.SaveJson(),Tick=_clock.Tick,Speed=_clock.Speed,Paused=_clock.Paused,Pending=_clock.PendingSeconds,
                Developer=_developerMode,Creative=_creative,FoundationMin=_foundation?.Min,FoundationMax=_foundation?.Max,Floor=_foundation?.Floor??0,FloorHeight=_foundation?.FloorHeight??3,
                LayerOffset=_layerOffset,Placement=_placement,HeightSelected=_heightSelected,ViewMode=!_cutaway?0:_interiorView?1:_floorWalls?3:2,TopDown=_topDown,Yaw=_cameraYaw,Pitch=_orbitPitch,Zoom=_camera.Size,FocusX=_focus.X,FocusY=_focus.Y,FocusZ=_focus.Z};
            SaveFiles.Write(SavePath,JsonSerializer.Serialize(state,DemoWorld.SaveOptions),ReadSavePath);
            _world.Log("营地已保存（F9 读取；上一份保留为 .bak）。");
            RefreshUi();return true;
        }catch(Exception e){_world.Log("保存失败："+e.Message);}
        RefreshUi();return false;
    }
    private void RequestLoadSession()
    {
        if(!File.Exists(ReadSavePath)){_world.Log("还没有营地存档，请先按 F5。");RefreshUi();return;}
        if(_loadDialog is null)
        {
            _loadDialog=new ConfirmationDialog{Title="读取营地",DialogText="读取上次保存的营地？当前未保存的修改将被替换。",OkButtonText="读取",CancelButtonText="取消"};AddChild(_loadDialog);
            _loadDialog.Confirmed+=LoadSession;
        }
        _loadDialog.PopupCentered();
    }
    public sealed record PreparedSession(SessionState State,DemoWorld World,SimulationClock Clock,FoundationPlan? Foundation);
    public static PreparedSession PrepareSession(string path)
    {
        if(new FileInfo(path).Length>64*1024*1024)throw new InvalidDataException("存档文件过大。");
        var s=JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path),DemoWorld.SaveOptions)??throw new InvalidDataException("无效存档。");
            if(!float.IsFinite(s.Pitch)||s.Pitch<MinCameraPitch||s.Pitch>MaxCameraPitch)throw new InvalidDataException("视角俯仰无效。");
            if(s.Version!=1||s.ViewMode is <0 or >3||s.Placement is <-1 or >1||!float.IsFinite(s.Yaw)||!float.IsFinite(s.Zoom)||s.Zoom is <12 or >240||!float.IsFinite(s.FocusX)||!float.IsFinite(s.FocusY)||!float.IsFinite(s.FocusZ))throw new InvalidDataException("视图存档无效。");
            var loaded=DemoWorld.LoadJson(s.World);var clock=new SimulationClock();clock.Restore(s.Tick,s.Speed,s.Paused,s.Pending);
            FoundationPlan? foundation=null;
            if(s.FoundationMin is Cell a&&s.FoundationMax is Cell b){if(!DemoWorld.Inside(a)||!DemoWorld.Inside(b))throw new InvalidDataException("地基越界。");foundation=new FoundationPlan(a,b);foundation.SetFloorHeight(s.FloorHeight);foundation.Step(s.Floor);}
            int level=(foundation?.WorldLevel??loaded.Camp.Level)+s.LayerOffset;
            if(level<=TerrainMap.MinLevel||level>TerrainMap.MaxLevel)throw new InvalidDataException("视图高度越界。");
        return new(s,loaded,clock,foundation);
    }
    private void ApplyPreparedSession(PreparedSession prepared)
    {
        var (s,loaded,clock,foundation)=prepared;
            _world=loaded;_clock=clock;_foundation=foundation;_layerOffset=s.LayerOffset;_placement=s.Placement;_heightSelected=s.HeightSelected;
            _developerMode=s.Developer;_creative=s.Creative;_revealUnknown=false;_topDown=s.TopDown;_cameraYaw=s.Yaw;_orbitPitch=s.Pitch;_camera.Size=s.Zoom;_focus=new(s.FocusX,s.FocusY,s.FocusZ);
            _selectedCell=null;_selectedWorker=-1;_stockpileStart=null;_farmStart=null;_foundationStart=null;_selectionPanel.Hide();_worldMenu.Hide();_terrainRevision=-1;
            _overview.World=_world;_overview.RefreshMap();CreatePeople();PositionCamera();DrawFoundation();SetViewMode(s.ViewMode);_angleButton.Text=_topDown?"斜视":"俯视";
    }
    private void LoadSession()
    {
        try
        {
            string readPath=ReadSavePath;
            var prepared=PrepareSession(readPath);
            // Only migrate a fully validated legacy save. Leave the original untouched.
            string? migrationError=null;
            if(readPath!=SavePath)
            {try{SaveFiles.Write(SavePath,File.ReadAllText(readPath));}catch(Exception e){migrationError=e.Message;}}
            ApplyPreparedSession(prepared);
            _world.Log("营地已恢复，可继续搬运、施工与生产。");
            if(migrationError is not null)_world.Log("旧存档已读取，但迁移失败："+migrationError);RefreshUi();
        }catch(Exception e){_world.Log("读取失败，保留当前营地："+e.Message);RefreshUi();}
    }
}

public partial class Main
{
    private async void CaptureColonyPreview()
    {
        try
        {
            _world=new DemoWorld();_world.DeveloperResidents(false);_overview.World=_world;_terrainRevision=-1;
            _world.QueueBlueprint(_world.Camp,DemoWorld.ExampleBlueprint());_world.DeveloperFinishPlans();
            _world.SpawnItem("raw_iron",6,_world.Camp+new Cell(3,8));
            _world.SetStockpile(_world.Camp+new Cell(0,8),_world.Camp+new Cell(5,9));
            _foundation=new FoundationPlan(_world.Camp,_world.Camp+new Cell(5,4));_heightSelected=true;_layerOffset=0;
            _focus=WorldPosition(_world.Camp+new Cell(2,4));_camera.Size=17;_cameraYaw=0;PositionCamera();CreatePeople();DrawFoundation();
            async Task Snap(string name)
            {
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/{name}.png"))!=Error.Ok)throw new IOException("Screenshot failed");
            }
            SetViewMode(3);await Snap("colony-walls");
            if(!_floorWalls||_interiorView||CurrentView.Top!=ActiveLevel+2)throw new InvalidOperationException("Full floor walls must omit ceiling and upper floors");
            SetViewMode(1);await Snap("colony-cutaway");
            ToggleTopDown();await Snap("colony-topdown");
            var target=_world.Camp+new Cell(2,2);
            if(Pick(_camera.UnprojectPosition(WorldPosition(target)),1)!=target)throw new InvalidOperationException("Top-down picking must hit same floor cell");
            SyncWorld();
            var raw=_world.Items.Single(i=>i.Item=="raw_iron");
            ulong itemNode=_itemNodes[raw.Id].Root.GetInstanceId(),buildingNode=_structures.GetChild(0).GetInstanceId();
            _world.SpawnItem("raw_iron",1,raw.Cell);SyncWorld();
            if(_itemNodes[raw.Id].Root.GetInstanceId()!=itemNode||_structures.GetChild(0).GetInstanceId()!=buildingNode||_itemNodes[raw.Id].Label.Text!="7")
                throw new InvalidOperationException("Stock count change rebuilt unchanged geometry or failed label update");
            if(!_world.QueueSmelt())throw new InvalidOperationException("Preview smelting order unavailable");
            _world.DeveloperPlace(_world.Camp+new Cell(3,1),BuildKind.Stove);
            if(!_world.QueueCook())throw new InvalidOperationException("Preview cooking order unavailable");
            _orbitPitch=Mathf.DegToRad(55);
            SaveSession();if(!File.Exists(SavePath))throw new InvalidOperationException("Session save missing");
            _world.Items.Clear();_topDown=false;_orbitPitch=MinCameraPitch;LoadSession();
            if(!_topDown||_world.Total("raw_iron")!=7||!_world.Jobs.Any(j=>j.Smelt)||_world.Workers.Count!=0||_foundation?.WorldLevel!=_world.Camp.Level)throw new InvalidOperationException("Session restore lost world, editor or camera state");
            ToggleTopDown();_inspector.Show();RefreshUi();await Snap("colony-orders");
            if(!Mathf.IsEqualApprox(_cameraPitch,Mathf.DegToRad(55)))throw new InvalidOperationException("Session restore lost saved pitch");
            var legacy=JsonSerializer.Deserialize<SessionState>("{}",DemoWorld.SaveOptions)!;
            if(legacy.Pitch!=DefaultCameraPitch)throw new InvalidOperationException("Old saves must retain the original camera angle");
            var order=_world.Jobs.Single(j=>j.Smelt);
            if(!_billRows.TryGetValue(order.ProductionOrderId,out var row)||!row.Count.TooltipText.Contains("尚未启用居民"))throw new InvalidOperationException("Order list must show individual waiting reason");
            _world.RemoveProductionOrder(order.ProductionOrderId);RefreshOrders();
            if(_world.Jobs.Any(j=>j.Smelt)||_world.Available("raw_iron")!=7||_billRows.ContainsKey(order.ProductionOrderId))throw new InvalidOperationException("Per-order cancel must release reservations and remove its row");
            if(_inspector.GetGlobalRect().Intersects(_buildTab.GetGlobalRect()))throw new InvalidOperationException("Order panel blocks bottom tabs");
            _inspector.Hide();_worldMenu.Show();await Snap("colony-save-menu");
            if(_worldMenu.GetGlobalRect().Intersects(_buildTab.GetGlobalRect()))throw new InvalidOperationException("Save panel blocks bottom tabs");
            GD.Print("COLONY_UI_OK: full walls, cutaway, top-down picking, stock rendering, session roundtrip and panel bounds");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

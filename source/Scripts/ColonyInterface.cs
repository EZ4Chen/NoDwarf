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
        AnchorCameraOrbit();
        _topDown=!_topDown;PositionCamera();_revision=-1;_hoverCell=null;ClearHover();
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
        var stockZones=_world.StorageZones.SelectMany(z=>z.Cells.Select(c=>(Cell:c,Id:z.Id))).ToDictionary(p=>p.Cell,p=>p.Id);
        var visibleStocks=_world.Stockpiles.Where(VisibleAt).ToHashSet();
        foreach(var c in _world.Stockpiles)
        {
            if(!_stockNodes.TryGetValue(c,out var root))
            {root=new Node3D{Position=WorldPosition(c)+Vector3.Up*.015f};_itemsView.AddChild(root);_stockNodes[c]=root;}
            int zone=stockZones.GetValueOrDefault(c);
            int mask=CellBoundary.Mask(c,n=>visibleStocks.Contains(n)&&stockZones.GetValueOrDefault(n)==zone);
            if(!root.HasMeta("outline_mask")||root.GetMeta("outline_mask").AsInt32()!=mask)
            {
                Clear(root);root.SetMeta("outline_mask",mask);
                if(mask!=0)root.AddChild(new MeshInstance3D{Mesh=ConstructionOutlineMesh(mask),MaterialOverride=Mat("6e897b"),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
            }
            root.Visible=visibleStocks.Contains(c);
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
                    var def=_world.Content.Item(stack.Item);
                    string? spriteId=stack.Item switch{"raw_iron"=>"raw_iron","iron"=>"iron_ingot","sword"=>"iron_sword","coal"=>"coal",_=>null};
                    if(EquipmentArt.Icon(stack.Item) is {} equipment)root.AddChild(new Sprite3D{Texture=equipment,PixelSize=.45f/Math.Max(equipment.GetWidth(),equipment.GetHeight()),Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Shaded=false});
                    else if(stack.Item is "raw_food" or "meal" or "grain_seed" or "mushroom_spawn")root.AddChild(new Sprite3D{Texture=UiIcons.Get(stack.Item),PixelSize=.007f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Shaded=false});
                    else if(spriteId is null)BlockTextures.Box(root,Vector3.Zero,new Vector3(.27f,.23f,.27f),def.Texture);
                    else root.AddChild(new Sprite3D{Texture=GD.Load<Texture2D>($"res://Assets/Textures/Items/{spriteId}.png"),PixelSize=.007f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Shaded=false});
                    var label=new Label3D{Position=Vector3.Up*.22f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,FontSize=28,PixelSize=.004f,OutlineSize=8,NoDepthTest=false};root.AddChild(label);
                    var body=new StaticBody3D{CollisionLayer=2,CollisionMask=0};root.AddChild(body);
                    using(var collisionShape=new BoxShape3D{Size=new Vector3(.3f,.3f,.3f)})body.AddChild(new CollisionShape3D{Shape=collisionShape});
                    entry=(root,label,body,stack.Item);_itemNodes[stack.Id]=entry;
                }
                bool visible=VisibleAt(stack.Cell);entry.Root.Visible=visible;entry.Body.CollisionLayer=visible?2u:0u;
                entry.Root.Position=WorldPosition(stack.Cell)+new Vector3((index%2)*.32f-.16f,.14f,(index/2%2)*.32f-.16f);index++;
                entry.Body.SetMeta("item_id",stack.Id);entry.Label.Text=stack.Count.ToString();entry.Body.SetMeta("cell",new Vector3I(stack.Cell.X,stack.Cell.Level,stack.Cell.Z));
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
    private string? _activeSavePath;
    private string SavePath=>_activeSavePath??ProjectSettings.GlobalizePath(
#if NODWARF_CHECKS
        OS.GetCmdlineUserArgs().Contains("--colony-preview-test")?"res://.local/test-colony-save.json":
#endif
        "user://saves/colony-save.json");
    private string ReadSavePath=>SavePath;
    private readonly SaveService _saveService=new();
    private Task<bool>? _saveTask;
    private DemoWorld? _saveOwner;
    private string? _saveTarget;
    private Task? _loadTask;
    private double _sessionPrepareMs,_sessionPublishMs;
    private bool _saveAgain,_sessionLeaving;
    private bool SessionAlive=>GodotObject.IsInstanceValid(this)&&IsInsideTree();
    private async void SaveSession()
    {
        if(_sessionLeaving)return;
        if(_loadTask is {IsCompleted:false}){ShowActionFeedback("正在读取营地，请稍候再保存。",true);return;}
        ShowActionFeedback("正在保存营地……");await SaveSessionAsync();
    }
    private Task<bool> SaveSessionAsync()
    {
        if(_loadTask is {IsCompleted:false})return Task.FromResult(false);
        if(_saveTask is {IsCompleted:false})
        {
            if(_saveOwner!=_world||_saveTarget!=SavePath)return SaveAfterCurrent(_saveTask,_world,SavePath);
            _saveAgain=true;return _saveTask;
        }
        _saveOwner=_world;_saveTarget=SavePath;
        return _saveTask=SavePendingSession();
    }
    private async Task<bool> SaveAfterCurrent(Task<bool> preceding,DemoWorld owner,string path)
    {
        await preceding;
        return SessionAlive&&_world==owner&&SavePath==path&&await SaveSessionAsync();
    }
    private async Task<bool> SavePendingSession()
    {
        var owner=_world;string path=SavePath;
        try
        {
            await SaveService.Ready;
            do
            {
                _saveAgain=false;
                if(!SessionAlive||_world!=owner)return false;
                // This continuation runs on Godot's main context, between simulation ticks.
                var state=new SessionState{Version=2,TickRate=SimulationClock.BaseTicksPerSecond,WorldState=owner.CaptureSnapshot(),Tick=_clock.Tick,Speed=_clock.Speed,Paused=_clock.Paused,Pending=_clock.PendingSeconds,
                    Developer=_developerMode,Creative=_creative,FoundationMin=_foundation?.Min,FoundationMax=_foundation?.Max,Floor=_foundation?.Floor??0,FloorHeight=_foundation?.FloorHeight??3,
                    LayerOffset=_layerOffset,Placement=_placement,HeightSelected=_heightSelected,ViewMode=!_cutaway?0:_interiorView?1:_floorWalls?3:2,TopDown=_topDown,Yaw=_cameraYaw,Pitch=_orbitPitch,Zoom=_camera.Size,FocusX=_focus.X,FocusY=_focus.Y,FocusZ=_focus.Z};
                await _saveService.Write(path,state);
            }while(_saveAgain);
            if(SessionAlive&&_world==owner){owner.Log("营地已保存（F9 读取；上一份保留为 .bak）。");RefreshUi();}
            return SessionAlive&&_world==owner;
        }
        catch(Exception e)
        {
            if(SessionAlive&&_world==owner){owner.Log("保存失败："+e.Message);RefreshUi();}
            return false;
        }
        finally{_saveOwner=null;_saveTarget=null;_saveTask=null;owner=null!;}
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
    private void ApplyPreparedSession(PreparedSession prepared)
    {
        var (s,loaded,clock,foundation,forest,map)=prepared;
        _terrainView.UsePreparedForest(loaded,forest);
        _storyDirector?.Dispose();_storyDirector=null;
        _residentMindDirector?.Dispose();_residentMindDirector=null;
            _world=loaded;_clock=clock;_foundation=foundation;_layerOffset=s.LayerOffset;_placement=s.Placement;_heightSelected=s.HeightSelected;
            _developerMode=s.Developer;_creative=s.Creative;_revealUnknown=false;_topDown=s.TopDown;_cameraYaw=s.Yaw;_orbitPitch=s.Pitch;_camera.Size=s.Zoom;_focus=new(s.FocusX,s.FocusY,s.FocusZ);
            _selectedCell=null;_selectedWorker=-1;_stockpileStart=null;_farmStart=null;_foundationStart=null;_selectionPanel.Hide();_worldMenu.Hide();_terrainRevision=-1;
            _overview.World=_world;_overview.ApplyPreparedMap(map);CreatePeople();PositionCamera();DrawFoundation();SetViewMode(s.ViewMode);_angleButton.Text=_topDown?"斜视":"俯视";
    }
    private async void LoadSession()=>await LoadSessionAsync();
    private Task LoadSessionAsync()
    {
        if(_sessionLeaving)return Task.CompletedTask;
        if(_loadTask is {IsCompleted:false})return _loadTask;
        return _loadTask=LoadPendingSession();
    }
    private async Task LoadPendingSession()
    {
        var owner=_world;string path=SavePath;
        try
        {
            if(_saveTask is {IsCompleted:false} saving&&!await saving)return;
            if(!SessionAlive||_world!=owner||SavePath!=path)return;
            string readPath=ReadSavePath;ShowActionFeedback("正在读取营地……");
            var result=await Task.Run(()=>
            {
                long started=System.Diagnostics.Stopwatch.GetTimestamp();
                var prepared=SessionPreparation.Prepare(readPath);
                return (prepared,ms:System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            });
            if(!SessionAlive||_world!=owner||SavePath!=path||_sessionLeaving)return;
            _sessionPrepareMs=result.ms;
#if NODWARF_CHECKS
            bool measurePublish=OS.GetCmdlineUserArgs().Contains("--session-async-test");
            if(measurePublish)result.prepared.World.Probe=new PerformanceProbe();
#endif
            long published=System.Diagnostics.Stopwatch.GetTimestamp();
            ApplyPreparedSession(result.prepared);
            _world.Log("营地已恢复，可继续搬运、施工与生产。");
            RefreshUi();
            _sessionPublishMs=System.Diagnostics.Stopwatch.GetElapsedTime(published).TotalMilliseconds;
#if NODWARF_CHECKS
            if(measurePublish){GD.Print("SESSION_PUBLISH_PROFILE: "+JsonSerializer.Serialize(_world.Probe!.Report()));_world.Probe=null;}
#endif
        }
        catch(Exception e)
        {if(SessionAlive&&_world==owner){owner.Log("读取失败，保留当前营地："+e.Message);RefreshUi();}}
        finally{_loadTask=null;owner=null!;}
    }

}

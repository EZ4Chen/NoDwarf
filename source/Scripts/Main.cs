using Godot;
using NoDwarf.Simulation;

namespace NoDwarf;

public partial class Main : Node3D
{
    private DemoWorld _world = null!;
    private SimulationClock _clock = new();
    private Camera3D _camera = null!;
    private Node3D _structures = null!, _ghosts = null!, _hover = null!;
    private readonly List<Node3D> _people = new();
    private Label _events = null!, _stats = null!, _hint = null!;
    private Button _pause = null!;
    private readonly Dictionary<string, Button> _toolButtons = new();
    private string _tool = "select";
    private int _facing;
    private float _cameraYaw;
    private static readonly float DefaultCameraPitch=Mathf.Atan2(38,50);
    private static readonly float MinCameraPitch=Mathf.DegToRad(20),MaxCameraPitch=Mathf.DegToRad(80);
    private float _orbitPitch=DefaultCameraPitch;
    private bool _topDown, _floorWalls;
    private float _cameraPitch=>_topDown?Mathf.Pi/2:_orbitPitch;
    private Vector3 CameraRight => new Vector3(.8f,0,-.6f).Rotated(Vector3.Up,_cameraYaw);
    private Vector3 CameraBack => new Vector3(.6f,0,.8f).Rotated(Vector3.Up,_cameraYaw);
    private void OrbitCamera(float degrees)
    {
        _cameraYaw=Mathf.PosMod(_cameraYaw+Mathf.DegToRad(degrees),Mathf.Tau);
        _dragging=false;PositionCamera();_hoverCell=null;Clear(_hover);
    }
    private int _layerOffset;
    private bool _heightSelected;
    private readonly Dictionary<int,Button> _placementButtons=new();
    private int _placement; // 0: block at feet, -1: floor beneath feet, 1: ceiling
    private int ActiveLevel => (_foundation?.WorldLevel ?? _world.Camp.Level) + _layerOffset;
    private int BuildLevel => ActiveLevel + (_placement < 0 ? -1 : _placement > 0 ? (_foundation?.FloorHeight ?? 3)-1 : 0);
    private WorldView CurrentView => new(_cutaway && (_foundation is not null || _heightSelected || _interiorView || _floorWalls) ? ((_interiorView || _floorWalls) ? ActiveLevel+(_foundation?.FloorHeight??3)-2 : Math.Max(ActiveLevel, BuildLevel)) : null, _developerMode && _revealUnknown,_wallCaps,_viewStamp);
    private bool VisibleAt(Cell c) => CurrentView.Visible(_world,c);
    private int _terrainRevision=-1;
    private Label _layerStatus = null!;
    private Button _cutButton = null!;

    private FoundationPlan? _foundation;
    private Cell? _foundationStart;
    private Node3D _foundationOutline = null!;
    private Label _floorLabel = null!;
    private SpinBox _floorHeight = null!;
    private int _revision = -1;
    private double _sampleSeconds, _actualTps;
    private long _sampleTicks;
    private Cell? _hoverCell;
    private bool _cutaway = true, _dragging;
    private Vector2 _dragOrigin;
    private Vector3 _cameraOrigin;
    private int _uiCounter;
    private TerrainView _terrainView = null!;
    private WorldOverview _overview = null!;
    private PanelContainer _inspector = null!;
    private Label _location = null!;
    private SpinBox _seed = null!;
    private Vector3 _focus;
    private double _tickMs;
    private ConfirmationDialog _resetDialog = null!;
    private int _pendingSeed;
    private string? _nextRegion="temperate",_pendingRegion;
    private bool _pauseBeforeDialog;
    private PanelContainer _buildMenu = null!, _selectionPanel = null!, _worldMenu = null!;
    private Label _selection = null!;
    private Cell? _selectedCell;
    private Button _buildTab = null!, _structureTab = null!, _stairsTab = null!;
    private readonly List<Button> _residentButtons = new();
    private int _selectedWorker = -1;
    private static readonly Color Ink = new("e5e5df"), Muted = new("a0a7aa"), Accent = new("d1bb87");

    public override void _Ready()
    {
        ConfigureInteriorThreads();
        var launch=GameLaunch.Take();_world=launch?.World??new DemoWorld();_activeSavePath=launch?.SavePath;
        GetWindow().Title="NoDwarf · 营地";
        GetTree().AutoAcceptQuit=false;GetWindow().CloseRequested+=RequestWindowQuit;
        GetWindow().FocusExited+=()=>{_orbitHeld=false;_digStart=null;_digEnd=null;_dragging=false;_hoverCell=null;if(_hover is not null)Clear(_hover);};
        if (OS.GetCmdlineUserArgs().Any(a=>a is "--appearance-preview-test" or "--resident-preview-test")) GetViewport().GuiEmbedSubwindows = true;
        StartLogging(); if(launch?.Session is null)LoadCharacters(); BuildScene(); BuildUi(); BuildDeveloperUi(); SelectTool("select"); SyncWorld(); RefreshUi();
        if(launch is null)ApplyDefaultEditor();
        else if(launch.Session is not null)ApplyPreparedSession(launch.Session);
        else {_developerMode=false;_creative=false;_clock.Paused=true;_world.Log("三位居民已抵达。安排工作与建设，按空格开始时间。");RefreshUi();}
        if(OS.GetCmdlineUserArgs().Any(a=>a is "--smoke-test" or "--interaction-test" or "--appearance-preview-test" or "--resident-preview-test"))
        {
            if(!_developerMode||!_clock.Paused||_world.Workers.Count!=0)throw new InvalidOperationException("Default editor must be paused and resident-free");
            _world.DeveloperResidents(true);_developerMode=false;_clock.Paused=false;CreatePeople();
        }
        if(OS.GetCmdlineUserArgs().Contains("--hud-review"))
        {_world.DeveloperResidents(true);_developerMode=false;_creative=false;_clock.Paused=true;CreatePeople();FocusCamp();RefreshUi();}
        if (OS.GetCmdlineUserArgs().Contains("--appearance-preview-test")) CaptureCharacterPreview();
        if (OS.GetCmdlineUserArgs().Contains("--startup-preview-test")) CheckStartupArrival();
        if (OS.GetCmdlineUserArgs().Contains("--geography-preview-test")) CheckGeographyPreview();
        if (OS.GetCmdlineUserArgs().Contains("--map-display-test")) CheckMapDisplay();
        if (OS.GetCmdlineUserArgs().Contains("--water-safety-ui-test")) CheckWaterSafetyUi();
        if (OS.GetCmdlineUserArgs().Contains("--mint-menu-test")) CheckMintMenus();
        if (OS.GetCmdlineUserArgs().Contains("--ui-cleanup-test")) CheckUiCleanup();
        if (OS.GetCmdlineUserArgs().Contains("--life-preview-test")) CaptureLifePreview();
        if (OS.GetCmdlineUserArgs().Contains("--resident-preview-test")) CaptureResidentPreview();
        if (OS.GetCmdlineUserArgs().Contains("--resident-test")) { try { ResidentChecks.Run(); GetTree().Quit(); } catch(Exception e) { GD.PushError(e.ToString());GetTree().Quit(1); } }
        if (OS.GetCmdlineUserArgs().Contains("--region-preview-test")) CaptureRegionPreview();
        if (OS.GetCmdlineUserArgs().Contains("--voxel-tree-preview-test")) CaptureVoxelTreePreview();
        if (OS.GetCmdlineUserArgs().Contains("--block-face-test")) ReviewBlockFaces();
        if (OS.GetCmdlineUserArgs().Contains("--gravity-test")) ReviewGravity();
        if (OS.GetCmdlineUserArgs().Contains("--climbing-test")) ReviewClimbing();
        if (OS.GetCmdlineUserArgs().Contains("--equipment-test")) ReviewEquipment();
        if (OS.GetCmdlineUserArgs().Contains("--dig-volume-test")) ReviewDigVolume();
        if (OS.GetCmdlineUserArgs().Contains("--mining-cutaway-test")) ReviewMiningCutaway();
        if (OS.GetCmdlineUserArgs().Contains("--designation-test")) ReviewDesignations();
        if (OS.GetCmdlineUserArgs().Contains("--level-guide-test")) ReviewLevelGuide();
        if (OS.GetCmdlineUserArgs().Contains("--corner-hud-test")) ReviewCornerHud();
        if (OS.GetCmdlineUserArgs().Contains("--object-commands-test")) ReviewObjectCommands();
        if (OS.GetCmdlineUserArgs().Contains("--melee-test")) ReviewMelee();
        if (OS.GetCmdlineUserArgs().Contains("--sprite-integration-test")) ReviewSpriteIntegration();
        if (OS.GetCmdlineUserArgs().Contains("--storyteller-test")) ReviewStoryteller();
        if (OS.GetCmdlineUserArgs().Contains("--personality-test")) ReviewResidentMind();
        if (OS.GetCmdlineUserArgs().Contains("--dwarf-integration-test")) ReviewSpriteIntegration();
        if (OS.GetCmdlineUserArgs().Contains("--complex-combat-test")) ReviewComplexCombat();
        if (OS.GetCmdlineUserArgs().Contains("--combat-test")) ReviewCombat();
        if (OS.GetCmdlineUserArgs().Contains("--ladder-test")) ReviewLadders();
        if (OS.GetCmdlineUserArgs().Contains("--settlement-preview-test")) CaptureSettlementPreview();
        if (OS.GetCmdlineUserArgs().Contains("--crop-climate-preview-test")) CaptureCropClimatePreview();
        if (OS.GetCmdlineUserArgs().Contains("--fluid-preview-test")) CaptureFluidPreview();
        if (OS.GetCmdlineUserArgs().Contains("--production-target-preview-test")) CaptureProductionTargets();
        if (OS.GetCmdlineUserArgs().Contains("--production-bill-test")) { try { ProductionBillChecks.Run(); CropProductionChecks.Run(); LogisticsChecks.Run(); GetTree().Quit(); } catch(Exception e) { GD.PushError(e.ToString());GetTree().Quit(1); } }
        if (OS.GetCmdlineUserArgs().Contains("--production-target-test")) { try { ProductionOrderChecks.Run(); GetTree().Quit(); } catch(Exception e) { GD.PushError(e.ToString());GetTree().Quit(1); } }
        if (OS.GetCmdlineUserArgs().Contains("--fluid-test")) { try { FluidChecks.Run(); GetTree().Quit(); } catch(Exception e) { GD.PushError(e.ToString());GetTree().Quit(1); } }
        if (OS.GetCmdlineUserArgs().Contains("--weather-test")) { try { WeatherChecks.Run(); CropClimateChecks.Run(); GetTree().Quit(); } catch(Exception e) { GD.PushError(e.ToString());GetTree().Quit(1); } }
        if (OS.GetCmdlineUserArgs().Contains("--weather-preview-test")) CaptureWeatherPreview();
        if (OS.GetCmdlineUserArgs().Contains("--calendar-preview-test")) CaptureCalendarPreview();
        if (OS.GetCmdlineUserArgs().Contains("--crop-production-preview-test")) CaptureCropProductionPreview();
        if (OS.GetCmdlineUserArgs().Contains("--farm-preview-test")) CaptureFarmPreview();
        if (OS.GetCmdlineUserArgs().Contains("--colony-preview-test")) CaptureColonyPreview();
        if (OS.GetCmdlineUserArgs().Contains("--construction-preview-test")) CaptureConstructionPreview();
        if (OS.GetCmdlineUserArgs().Contains("--room-preview-test")) CaptureRoomPreview();
        if (OS.GetCmdlineUserArgs().Contains("--distant-view-test")) CheckDistantView();
        if (OS.GetCmdlineUserArgs().Any(a=>a is "--console-test" or "--console-preview-test")) CheckUnifiedConsole();
        if (OS.GetCmdlineUserArgs().Contains("--room-test"))
        {try{RoomEnvironmentChecks.Run();GetTree().Quit();}catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}}
        if (OS.GetCmdlineUserArgs().Contains("--interior-preview-test")) CaptureInteriorPreview();
        if (OS.GetCmdlineUserArgs().Contains("--interaction-test")) RunInteractionCheck();
        if (OS.GetCmdlineUserArgs().Contains("--box-selection-test")) CheckBoxSelection();
        if (OS.GetCmdlineUserArgs().Contains("--tps-test")) StartTpsReview();
        if (OS.GetCmdlineUserArgs().Contains("--depth-review")) RunDepthReview();
        if (OS.GetCmdlineUserArgs().Contains("--terrain-pipeline-test")) CheckTerrainPipeline();
        if (OS.GetCmdlineUserArgs().Contains("--terrain-cache-test")) CheckTerrainCache();
        if (OS.GetCmdlineUserArgs().Contains("--interior-performance-test")) ReviewInteriorPerformance();
        if (OS.GetCmdlineUserArgs().Contains("--interior-thread-test")) ReviewInteriorThreads();
        if(OS.GetCmdlineUserArgs().Contains("--logistics-test"))
        {try{LogisticsChecks.Run();GetTree().Quit();}catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}}
        if (OS.GetCmdlineUserArgs().Contains("--smoke-test"))
        {
            try { CheckInputFocus(); CheckCharacterEditor(); DemoChecks.Run(); CheckDeveloperConsole(); CheckInteriorReview(); CheckIncrementalVisuals(); GD.Print("SMOKE_OK: construction, room, path, crafting, blueprint and clock verified."); GetTree().Quit(); }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        }
    }

    private async void RunInteractionCheck()
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            void Click(Vector2 p)
            {
                GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true);
                GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
                GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }, true);
            }
            if(_mapPanel.Visible||_diagnosticsPanel.Visible)throw new InvalidOperationException("Map and diagnostics should start collapsed");
            if(_resourceCounts.Count!=13||_toolButtons.Values.Any(b=>b.Icon is null))throw new InvalidOperationException("HUD resource or tool icons missing");
            _architectAutoClose.ButtonPressed=false;
            var button = _toolButtons["Wall"];
            _worldMenu.Show(); _seed.GetLineEdit().GrabFocus();
            Click(_buildTab.GetGlobalRect().GetCenter());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Click(_categoryButtons["规划"].GetGlobalRect().GetCenter());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if(!_floorHeight.IsVisibleInTree()||_buildPages["命令"].Visible)throw new InvalidOperationException("Planning category did not expose foundation controls");
            Click(_stairsTab.GetGlobalRect().GetCenter());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Click(_toolButtons[nameof(BuildKind.PlankStairs)].GetGlobalRect().GetCenter());
            if (_tool != nameof(BuildKind.PlankStairs)) throw new InvalidOperationException("Stair material button not selectable");
            GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.R, Keycode = Key.R, Pressed = true }, true);
            if (_facing != 1) throw new InvalidOperationException("R must rotate stairs");
            Click(_structureTab.GetGlobalRect().GetCenter());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Click(_toolButtons[nameof(BuildKind.StoneBricks)].GetGlobalRect().GetCenter());
            if (_tool != nameof(BuildKind.StoneBricks)) throw new InvalidOperationException("Material block button not selectable");
            if (_hint.IsVisibleInTree()&&button.GetGlobalRect().Intersects(_hint.GetGlobalRect())) throw new InvalidOperationException("Construction button overlaps hint bar");
            Click(button.GetGlobalRect().GetCenter());
            if (GetViewport().GuiGetFocusOwner() is LineEdit) throw new InvalidOperationException("Toolbar click must exit seed editing");
            if (_tool != "Wall") throw new InvalidOperationException("Toolbar mouse click did not select stone wall");
            _buildMenu.Hide();
            var target = _world.Camp;
            var screen = _camera.UnprojectPosition(WorldPosition(target));
            var picked = Pick(screen);
            if (picked != target) throw new InvalidOperationException($"Ground picking failed: expected {target}, got {picked}, screen {screen}");
            Click(screen);
            if (!_world.Jobs.Any(j => j.Cell == target && j.Build == BuildKind.Wall))
                throw new InvalidOperationException("Map mouse click did not queue wall");
            for (int i = 0; i < 30 * 60; i++) _world.Update(1.0 / 30);
            if (!_world.Buildings.ContainsKey(target)) throw new InvalidOperationException("Clicked wall did not complete");
            // Full-height walls must be picked by their visible geometry, not the ground behind them.
            _cutaway = false; SyncWorld();
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var wallScreen = _camera.UnprojectPosition(WorldPosition(target) + Vector3.Up * .85f);
            if (PickTarget(wallScreen) != target) throw new InvalidOperationException("Upper wall click selects incorrect cell");
            SelectTool("select"); _buildMenu.Hide();
            Click(wallScreen);
            if (_selectedCell != target) throw new InvalidOperationException("Wall click did not update selection panel");
            var personScreen = _camera.UnprojectPosition(_people[0].Position + Vector3.Up * .85f);
            Click(personScreen);
            if (_selectedWorker != 0) throw new InvalidOperationException("Visible resident body did not select resident");
            SelectTool("erase"); Click(wallScreen);
            if(!_world.Jobs.Any(j=>j.Demolish&&j.Cell==target))throw new InvalidOperationException("Wall click did not queue demolition");
            for(int i=0;i<30*60;i++)_world.Update(1d/30);
            if (_world.Buildings.ContainsKey(target)) throw new InvalidOperationException("Queued wall demolition did not finish");
            SelectTool("foundation"); _buildMenu.Hide(); _selectionPanel.Hide();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Click(_camera.UnprojectPosition(WorldPosition(_world.Camp)));
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var secondCorner = _camera.UnprojectPosition(WorldPosition(_world.Camp + new Cell(4, 4)));
            Click(secondCorner);
            if (_foundation is null || _foundation.WorldLevel != _world.Camp.Level) throw new InvalidOperationException($"Foundation clicks must establish floor zero: tool={_tool}, start={_foundationStart}, events={string.Join(" | ", _world.Events)}");
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Plus,PhysicalKeycode=Key.Equal,MetaPressed=true});
            if (_foundation.WorldLevel != _world.Camp.Level + 3 || Math.Abs(_focus.Y - _foundation.WorldLevel) > .01) throw new InvalidOperationException("Floor + must jump camera by configured height");
            SelectTool("Wall"); Click(_camera.UnprojectPosition(WorldPosition(target)));
            if (_world.Jobs.Any(j => j.Cell == target)) throw new InvalidOperationException("Selected upper floor must not silently build on ground");
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Minus,PhysicalKeycode=Key.Minus,CtrlPressed=true});
            if (_foundation.WorldLevel != _world.Camp.Level) throw new InvalidOperationException("Control minus must return to floor zero");
            var beforeReset = _world;
            _clock.Paused = false; Reset();
            if (!_resetDialog.Visible || !_clock.Paused) throw new InvalidOperationException("Reset must display confirmation and pause");
            _resetDialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); _resetDialog.Hide();
            if (_world != beforeReset || _clock.Paused) throw new InvalidOperationException("Cancel reset must retain world and restore pause state");
            Reset();
            if (!_resetDialog.Visible || _world != beforeReset) throw new InvalidOperationException("Later reset must ask again");
            _resetDialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); _resetDialog.Hide();
            // Build the same multi-storey world through production jobs, then test real camera rays.
            _world=new DemoWorld(); _overview.World=_world; StartTwoStoreyDemo();
            for(int i=0;i<30*900;i++) _world.Update(1.0/30);
            if(_world.Jobs.Count!=0) throw new InvalidOperationException("Generated-map two-storey construction did not finish");
            CreatePeople(); ChangeFloor(1); SyncWorld();
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var anvil=_world.Camp+new Cell(4,1,3);
            var anvilScreen=_camera.UnprojectPosition(WorldPosition(anvil)+Vector3.Up*.75f);
            if(PickTarget(anvilScreen)!=anvil) throw new InvalidOperationException("Cutaway roof must not intercept upper anvil selection: "+PickTarget(anvilScreen));
            var upperAir=_world.Camp+new Cell(3,3,3);
            SelectTool("StoneBricks");
            if(PickForTool(_camera.UnprojectPosition(WorldPosition(upperAir)))!=upperAir) throw new InvalidOperationException("Upper construction plane does not match preview");
            SetPlacement(-1);
            if(PickForTool(_camera.UnprojectPosition(WorldPosition(upperAir)))!=upperAir+new Cell(0,0,-1)) throw new InvalidOperationException("Floor slab must occupy the voxel below feet");
            SetPlacement(1);
            if(BuildLevel!=_foundation!.WorldLevel+2) throw new InvalidOperationException("Ceiling uses floor-to-floor spacing");
            SelectTool("select");_cutaway=false;SyncWorld();
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var roof=_world.Camp+new Cell(3,3,5);
            if(PickTarget(_camera.UnprojectPosition(WorldPosition(roof)+Vector3.Up*.99f))!=roof) throw new InvalidOperationException("Overview must restore selectable roof");
            _cutaway=true;ChangeFloor(-1);SyncWorld();AnimatePeople();
            if(_people.Where((p,i)=>_world.Workers[i].Level>ActiveLevel).Any(p=>p.Visible||p.GetChild<StaticBody3D>(2).CollisionLayer!=0)) throw new InvalidOperationException("Hidden upstairs residents must not intercept clicks");
            _cutaway=true;ChangeFloor(1);SyncWorld();
            var orbitTarget=_world.Camp+new Cell(2,2,3);
            for(int turn=0;turn<8;turn++)
            {
                OrbitCamera(45);
                var screenPoint=_camera.UnprojectPosition(WorldPosition(orbitTarget));
                if(PlanePick(screenPoint,orbitTarget.Level)!=orbitTarget)throw new InvalidOperationException("Rotated construction plane lost its cell");
                if(Math.Abs(CameraRight.Dot(CameraBack))>.001f)throw new InvalidOperationException("Camera movement axes are not orthogonal");
            }
            _cameraYaw=0;PositionCamera();ChangeFloor(-1);
            foreach(var kind in new[]{BuildKind.LogStairs,BuildKind.PlankStairs,BuildKind.CobbleStairs,BuildKind.BrickStairs})
                if(BlockTextures.Material(BlockCatalog.Id(kind)).AlbedoTexture is null)throw new InvalidOperationException("Stair texture missing: "+kind);
            GD.Print("ORBIT_STAIRS_OK: eight camera angles, matching plane picks, four stair textures");
            GD.Print("MULTILEVEL_INTERACTION_OK: upper anvil, floor/ceiling placement planes, roof overview, hidden resident picking");
            _world=new DemoWorld();_overview.World=_world;_foundation=null;StartUndergroundDemo();
            for(int i=0;i<30*240;i++)_world.Update(1.0/30);
            SyncWorld();CreatePeople();
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!_world.WorkshopReady||_world.Jobs.Count!=0)throw new InvalidOperationException("Basement example did not finish");
            var retainedRock=_world.Camp+new Cell(5,2,-3);
            SetViewMode(1);
            if(_wallCaps is null||_wallCaps.Count==0||CurrentView.CellTop(retainedRock)>=retainedRock.Level+1)
                throw new InvalidOperationException("Interior view must lower front natural walls");
            if(!_world.NaturalSolid(retainedRock))throw new InvalidOperationException("Interior view changed real terrain");
            IEnumerable<Node> Descendants(Node n)
            {foreach(Node child in n.GetChildren()){yield return child;foreach(var sub in Descendants(child))yield return sub;}}
            var overlays=Descendants(_terrainView).Where(n=>n.HasMeta("view_overlay")).ToArray();
            if(overlays.Length!=0)throw new InvalidOperationException("Hidden foreground walls must not leave transparent overlays");
            var frontCaps=_wallCaps.Keys.ToHashSet();
            OrbitCamera(180);SyncWorld();
            if(frontCaps.SetEquals(_wallCaps!.Keys))throw new InvalidOperationException("Interior walls must follow camera side");
            OrbitCamera(180);SetViewMode(2);
            if(CurrentView.CellTop(retainedRock)!=ActiveLevel+1)throw new InvalidOperationException("Precise mode must restore full voxel walls");
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var undergroundAnvil=_world.Camp+new Cell(4,1,-3);
            var undergroundScreen=_camera.UnprojectPosition(WorldPosition(undergroundAnvil)+Vector3.Up*.85f);
            if(PickTarget(undergroundScreen)!=undergroundAnvil)throw new InvalidOperationException("Terrain cutaway blocks basement anvil: "+PickTarget(undergroundScreen));
            var inside=_world.Camp+new Cell(3,2,-3);
            if(Pick(_camera.UnprojectPosition(WorldPosition(inside)))!=inside)throw new InvalidOperationException("Excavated ground click wrong: expected="+inside+" actual="+Pick(_camera.UnprojectPosition(WorldPosition(inside))));
            var rock=_world.Camp+new Cell(5,2,-3);
            SelectTool("dig");var diggingPoint=_camera.UnprojectPosition(WorldPosition(rock)+Vector3.Up);
            if(PickForTool(diggingPoint)!=rock)throw new InvalidOperationException("Mining plane wrong level");
            _buildMenu.Hide();_inspector.Hide();Click(diggingPoint);Click(diggingPoint);
            if(!_world.Jobs.Any(j=>j.Dig&&j.Cell==rock))throw new InvalidOperationException("Mining click did not queue excavation");
            _world.Erase(rock);SelectTool("select");_cutaway=false;SyncWorld();
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(PickTarget(undergroundScreen)==undergroundAnvil)throw new InvalidOperationException("Full terrain must occlude underground workshop");
            if(!_world.NaturalSolid(inside+new Cell(0,0,2)))throw new InvalidOperationException("Cutaway must not delete roof voxels");
            if(BlockTextures.Material("nodwarf:furnace",front:true).AlbedoTexture is null || BlockTextures.Material("nodwarf:anvil",top:true).AlbedoTexture is null)throw new InvalidOperationException("Equipment textures missing");
            GD.Print("UNDERGROUND_INTERACTION_OK: exposed basement floor and anvil, mining mouse input, restored terrain occlusion, equipment textures");
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;
            _foundation=null;_cameraYaw=0;_camera.Size=20;FocusCamp();SyncWorld();CreatePeople();
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            int beforeHeight=ActiveLevel;
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Plus,PhysicalKeycode=Key.Equal});
            if(ActiveLevel!=beforeHeight+1)throw new InvalidOperationException("Plus must adjust one metre without a foundation");
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Minus,PhysicalKeycode=Key.Minus});
            if(ActiveLevel!=beforeHeight)throw new InvalidOperationException("Minus must adjust one metre");
            _heightSelected=false;_layerOffset=0;SyncWorld();
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            SelectTool("dig");
            var miningStart=_world.Camp+new Cell(2,2,-1);
            for(int angle=0;angle<8;angle++)
            {
                OrbitCamera(45);
                if(DigPick(_camera.UnprojectPosition(WorldPosition(miningStart)+Vector3.Up))!=miningStart)
                    throw new InvalidOperationException("Visible surface mining pick shifted at angle "+angle);
            }
            _cameraYaw=0;PositionCamera();
            var a=_camera.UnprojectPosition(WorldPosition(miningStart)+Vector3.Up);
            var b=_camera.UnprojectPosition(WorldPosition(miningStart+new Cell(2,1))+Vector3.Up);
            void Mouse(MouseButton button,bool pressed,Vector2 p)=>GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=button,Pressed=pressed,Position=p,GlobalPosition=p},true);
            GetViewport().PushInput(new InputEventMouseMotion{Position=a,GlobalPosition=a},true);
            Mouse(MouseButton.Left,true,a);
            GetViewport().PushInput(new InputEventMouseMotion{Position=b,GlobalPosition=b,ButtonMask=MouseButtonMask.Left},true);
            if(_world.Jobs.Count!=0)throw new InvalidOperationException("Mining selection must wait for second click");
            Mouse(MouseButton.Left,false,b);
            if(_world.Jobs.Count!=0)throw new InvalidOperationException("Release must not commit mining");
            Mouse(MouseButton.Left,true,b);Mouse(MouseButton.Left,false,b);
            if(_world.Jobs.Count!=6||_world.Jobs.Any(j=>j.DigHeight!=1||j.Cell.Level!=miningStart.Level))
                throw new InvalidOperationException("Mining drag must mark six individual voxels at one height");
            foreach(var job in _world.Jobs.ToArray())_world.Erase(job.Cell);
            _hoverCell=null;
            Mouse(MouseButton.Left,true,a);
            GetViewport().PushInput(new InputEventMouseMotion{Position=b,GlobalPosition=b,ButtonMask=MouseButtonMask.Left},true);
            Mouse(MouseButton.Right,true,b);Mouse(MouseButton.Right,false,b);Mouse(MouseButton.Left,false,b);
            if(_world.Jobs.Count!=0)throw new InvalidOperationException("Cancel drag must not queue mining");
            SelectTool("dig");float oldYaw=_cameraYaw,oldPitch=_cameraPitch;
            Mouse(MouseButton.Right,true,a);
            GetViewport().PushInput(new InputEventMouseMotion{Position=a+new Vector2(90,30),GlobalPosition=a+new Vector2(90,30),ButtonMask=MouseButtonMask.Right},true);
            Mouse(MouseButton.Right,false,a+new Vector2(90,30));
            if(_cameraYaw==oldYaw||_cameraPitch<=oldPitch||_tool!="dig"||_orbitHeld)
                throw new InvalidOperationException("Right drag must change yaw and pitch and keep the tool");
            void VerticalDrag(float distance)
            {
                Mouse(MouseButton.Right,true,a);
                var end=a+new Vector2(0,distance);
                GetViewport().PushInput(new InputEventMouseMotion{Position=end,GlobalPosition=end,ButtonMask=MouseButtonMask.Right},true);
                Mouse(MouseButton.Right,false,end);
            }
            oldYaw=_cameraYaw;VerticalDrag(-1000);
            if(_cameraPitch!=MinCameraPitch||_cameraYaw!=oldYaw||_tool!="dig")throw new InvalidOperationException("Vertical drag must clamp at 20 degrees without yaw or tool changes");
            VerticalDrag(1000);
            if(_cameraPitch!=MaxCameraPitch)throw new InvalidOperationException("Vertical drag must clamp at 80 degrees");
            VerticalDrag(-50);oldPitch=_cameraPitch;
            ToggleTopDown();ToggleTopDown();
            if(_cameraPitch!=oldPitch)throw new InvalidOperationException("Top-down toggle must remember the oblique angle");
            ToggleTopDown();VerticalDrag(-50);
            if(_topDown||_cameraPitch>=Mathf.Pi/2||_angleButton.Text!="俯视")throw new InvalidOperationException("Vertical drag must leave top-down view");
            Mouse(MouseButton.Right,true,a);Mouse(MouseButton.Right,false,a);
            if(_tool!="select"||_orbitHeld)throw new InvalidOperationException("Right click must still cancel the active tool");
            GD.Print("EDITOR_REVISION_OK: default resident-free editor, modifier height shortcuts, camera-facing interior walls, preserved terrain");
            GD.Print("GESTURES_OK: visible voxel picks at eight angles, one-voxel depth, six-cell drag, cancel, right-drag yaw and pitch, 20–80 degree limits, top-down angle memory");
            GD.Print("HUD_OK: icon tools, collapsed map/debug, planning categories, construction and selection");
            GD.Print("INTERACTION_OK: toolbar, wall construction, wall-body selection and erase, resident selection, reset cancellation");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void CheckInputFocus()
    {
        _worldMenu.Show();
        var editor = _seed.GetLineEdit();
        foreach (var key in new[] { Key.Enter, Key.Escape })
        {
            editor.GrabFocus(); editor.Text = "74219";
            _Input(new InputEventKey { Keycode = key, Pressed = true });
            if (GetViewport().GuiGetFocusOwner() == editor) throw new InvalidOperationException("Seed field retained focus after confirmation");
        }
        editor.GrabFocus();
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(640, 400) });
        if (GetViewport().GuiGetFocusOwner() == editor) throw new InvalidOperationException("Map click must release seed focus");
        _worldMenu.Hide();
        GD.Print("INPUT_OK: Enter, Escape and map click release seed focus.");
    }

    public override void _Process(double delta)
    {
        RefreshActionFeedback();
        DiagnosticTrace.Frame((long)Engine.GetProcessFrames());
        using var frameTrace=DiagnosticTrace.Span("frame.main");
        long processStarted = _tpsReview is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        BeginFrameDiagnostic(delta);
        var profileFrame = _world.Probe?.Measure("frame.main-callback") ?? default;
        PumpInteriorWork();
        int quadrant=Mathf.FloorToInt(Mathf.RadToDeg(Mathf.PosMod(_cameraYaw,Mathf.Tau))/5);
        if(LocalWallCutaway&&quadrant!=_viewQuadrant){_viewQuadrant=quadrant;_revision=-1;}
        if(!_depthReviewActive)MoveCamera(delta);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        int ticks = _clock.Advance(delta);
        PumpStoryteller(ticks > 0);
        PumpResidentMind(ticks > 0);
        if(_creative&&_clock.Paused)_world.AdvanceSupport();
        try { for (int i = 0; i < ticks; i++) { if (_tpsReview is null) _world.Update(1.0 / SimulationClock.BaseTicksPerSecond); else ReviewTick(); } }
        catch(Exception e) { _clock.Paused=true;_sessionLog.Write(_clock.Tick,"ERROR","模拟",e.ToString());GD.PushError(e.ToString()); }
        _tickMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _sampleTicks += ticks; _sampleSeconds += delta;
        if (_sampleSeconds >= 0.5)
        { _actualTps = _sampleTicks / _sampleSeconds; _sampleSeconds = 0; _sampleTicks = 0; }
        long syncStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_world.Revision != _revision) SyncWorld();
        double syncMs = System.Diagnostics.Stopwatch.GetElapsedTime(syncStarted).TotalMilliseconds;
        long uiStarted = _tpsReview is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        AnimatePeople();
        AnimateBuildingFalls(delta);
        HideInactiveDigHighlight();
        UpdateConstructionVisuals();
        RefreshWeatherVisuals(delta);RefreshFluidVisuals();
        if (++_uiCounter % 6 == 0) RefreshUi();
        UpdateHover();
        UpdateLevelGuide();
        profileFrame.Dispose();
        long reviewStarted = _frameDiagnostic is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        if (_tpsReview is not null) FinishTpsFrame(syncMs,
            System.Diagnostics.Stopwatch.GetElapsedTime(uiStarted).TotalMilliseconds,
            System.Diagnostics.Stopwatch.GetElapsedTime(processStarted).TotalMilliseconds, delta);
        if (_frameDiagnostic?.Current is { } diagnosticFrame)
            diagnosticFrame.reviewBookkeepingMs = System.Diagnostics.Stopwatch.GetElapsedTime(reviewStarted).TotalMilliseconds;
        _frameDiagnostic?.End();
    }

    public override void _Input(InputEvent input)
    {
        if(input is InputEventKey{Pressed:true,Keycode:Key.Escape}&&_billDetails is not null)
        {CloseBillDetails();GetViewport().SetInputAsHandled();return;}
        if(input is InputEventKey{Pressed:true,Keycode:Key.F} findKey&&(findKey.CtrlPressed||findKey.MetaPressed)&&(_buildMenu?.Visible==true||_inspector?.Visible==true))
        {if(_buildMenu?.Visible==true)_architectSearch.GrabFocus();else _recipeSearch.GrabFocus();GetViewport().SetInputAsHandled();return;}

        if(_depthReviewActive){GetViewport().SetInputAsHandled();return;}
        // Benchmarks own their camera/clock; incidental keyboard or GUI input must
        // not resume a frozen scene or change the workload being compared.
        if(_tpsReview is not null){GetViewport().SetInputAsHandled();return;}
        // Benchmark camera and workload must not be changed by desktop input.
        if (_tpsReview is not null) { GetViewport().SetInputAsHandled(); return; }
        if(_titleDialog?.Visible==true)return;
        if(_removeGrowingDialog?.Visible==true)return;
        if (DeveloperInput(input)) return;
        if(DossierOpen)
        {
            if(input is InputEventKey { Pressed:true, Echo:false, Keycode:Key.Escape })
            { CloseResidentPanel();GetViewport().SetInputAsHandled(); }
            return;
        }
        if(input is InputEventMouseButton{Pressed:true,ButtonIndex:MouseButton.Left}&&GetViewport().GuiGetHoveredControl() is null)
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        if(HandleMapGesture(input))return;
        if (SessionInput(input)) return;
        if (DossierOpen || _characterEditor is not null && _characterEditor.Visible) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit editor)
        {
            if (input is InputEventKey { Pressed: true } key && key.Keycode is Key.Enter or Key.KpEnter or Key.Escape)
            {
                CommitNumberField(editor); GetViewport().SetInputAsHandled(); return;
            }
            if (input is InputEventMouseButton { Pressed: true } click &&
                !editor.GetGlobalRect().HasPoint(click.Position))
            { CommitNumberField(editor); }
        }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: false }) _dragging = false;
    }

    private void CommitNumberField(LineEdit editor)
    {
        if (editor == _seed.GetLineEdit()) _seed.Apply();
        if (editor == _floorHeight.GetLineEdit()) _floorHeight.Apply();
        if (editor.GetParent() is SpinBox numeric) numeric.Apply();
        editor.ReleaseFocus();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_tpsReview is not null) return;
        if(_titleDialog?.Visible==true)return;
        if(DossierOpen)return;
        if (_characterEditor.Visible || _consolePanel.Visible || _removeGrowingDialog?.Visible==true) return;
        if (input is InputEventPanGesture pan)
        {
            _focus += (CameraRight * pan.Delta.X + CameraBack * pan.Delta.Y) * (_camera.Size / 120f);
            PositionCamera();
        }
        if (input is InputEventMagnifyGesture pinch) _camera.Size = Math.Clamp(_camera.Size / pinch.Factor, 12, 240);
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed) _camera.Size = Math.Max(12, _camera.Size * .9f);
            if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed) _camera.Size = Math.Min(240, _camera.Size * 1.1f);
            if (mouse.ButtonIndex == MouseButton.Middle)
            { _dragging = mouse.Pressed; _dragOrigin = mouse.Position; _cameraOrigin = _focus; }
            if(mouse.ButtonIndex==MouseButton.Right && mouse.Pressed)
            { _orbitHeld=true;_orbitMoved=false;_orbitOrigin=mouse.Position;_orbitLast=mouse.Position; }
            if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed)
            {
                if(_tool is "dig" or "digstairs" or "canceldig") { if(PickForTool(mouse.Position) is Cell start) { _digStart=start;_digEnd=start;_hoverCell=null; } }
                else if (_tool=="select"||AreaTool(_tool)) BeginLeftSelection(mouse.Position,mouse.ShiftPressed);
                else if (PickForTool(mouse.Position) is Cell cell) Act(cell);
            }
        }
        if (input is InputEventMouseMotion motion && _dragging)
        {
            var offset = (motion.Position - _dragOrigin) * (_camera.Size / 800);
            _focus = _cameraOrigin - CameraRight * offset.X + CameraBack * offset.Y / Mathf.Sin(_cameraPitch);
            PositionCamera();
        }
        if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Space) TogglePause();
            if (key.Keycode == Key.Escape) { _combatPanel?.Hide(); _storagePanel?.QueueFree();_storagePanel=null; SelectTool("select"); _buildMenu.Hide(); _inspector.Hide(); _worldMenu.Hide(); ClearResidentSelection(); }
            if (key.Keycode == Key.H) ToggleWalls();
            if ((key.PhysicalKeycode==Key.R||key.Keycode==Key.R)&&!key.CtrlPressed&&!key.MetaPressed&&!key.AltPressed&&GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit))
            {
                if(_tool=="select")
                {if(SelectedResidents().Length>0)DraftSelectedResidents();}
                else RotateStairs();
                GetViewport().SetInputAsHandled();
            }
            if (key.PhysicalKeycode == Key.Z) OrbitCamera(-45);
            if (key.PhysicalKeycode == Key.C) OrbitCamera(45);
            if (key.PhysicalKeycode == Key.Q) StepLayer(-1);
            if (key.PhysicalKeycode == Key.E) StepLayer(1);
            if (key.Keycode is Key.Plus or Key.KpAdd || key.PhysicalKeycode == Key.Equal) {if(key.MetaPressed||key.CtrlPressed)ChangeFloor(1);else StepLayer(1);}
            if (key.Keycode is Key.Minus or Key.KpSubtract) {if(key.MetaPressed||key.CtrlPressed)ChangeFloor(-1);else StepLayer(-1);}
            if (key.PhysicalKeycode == Key.F || key.Keycode == Key.Home) FocusCamp();
            if (key.PhysicalKeycode == Key.B) TogglePanel(_buildMenu);
            string? tool = key.Keycode switch { Key.Key1 => "Wall", Key.Key2 => "Door", Key.Key3 => "Furnace", Key.Key4 => "Anvil", Key.Key5 => "erase", _ => null };
            if (tool is not null) SelectTool(tool);
        }
    }

    private Cell? PlanePick(Vector2 screen, int level)
    {
        var origin = _camera.ProjectRayOrigin(screen); var ray = _camera.ProjectRayNormal(screen);
        if (Math.Abs(ray.Y)<.001) return null;
        var p=origin+ray*((level-origin.Y)/ray.Y);
        var c=new Cell((int)Math.Floor(p.X+DemoWorld.Width/2f),(int)Math.Floor(p.Z+DemoWorld.Depth/2f),level);
        return DemoWorld.Inside(c)?c:null;
    }
    private Cell? PickForTool(Vector2 screen) => _tool=="canceldig" ? PlanePick(screen,ActiveLevel+1) is Cell cancelCell ? cancelCell with{Level=ActiveLevel} : null : _tool is "dig" or "digstairs" ? DigPick(screen) : _tool=="chop" ? ChopPick(screen) : (_foundation is not null || _heightSelected) && (_tool is "blueprint" || Enum.TryParse<BuildKind>(_tool,out _))
        ? BuildPlanePick(screen) : (_tool is "foundation" or "stockpile" or "unstockpile" or "growingzone" or "unfarm" ? Pick(screen,1) : Enum.TryParse<BuildKind>(_tool,out _) ? Pick(screen) : PickTarget(screen));
    private Cell? BuildPlanePick(Vector2 screen)
    { var c=PlanePick(screen,_placement<0?ActiveLevel:BuildLevel); return c is Cell p ? p with { Level=BuildLevel } : null; }
    private Cell? Pick(Vector2 screen,uint mask=3)
    {
        var hit = PickObject(screen, mask);
        if (hit.Count == 0 || ((Vector3)hit["normal"]).Y < .5f) return null;
        Vector3 p = (Vector3)hit["position"];
        int x = (int)Math.Floor(p.X + DemoWorld.Width / 2f), z = (int)Math.Floor(p.Z + DemoWorld.Depth / 2f);
        if(x<0||z<0||x>=DemoWorld.Width||z>=DemoWorld.Depth) return null;
        var collider=hit["collider"].AsGodotObject();
        if(collider.HasMeta("cell"))
        {
            var b=collider.GetMeta("cell").AsVector3I();
            var cell=new Cell(b.X,b.Z,b.Y);
            if (_world.Buildings.TryGetValue(cell,out var block) && BlockCatalog.IsWall(block.Kind)) return cell+new Cell(0,0,1);
            return cell;
        }
        var ground=_world.Surface(x,z);
        return _world.NaturalRamp(ground)&&p.Y>=ground.Level&&p.Y<=ground.Level+1 ? ground : new Cell(x,z,(int)Math.Round(p.Y));
    }
    private Godot.Collections.Dictionary PickObject(Vector2 screen, uint mask)
    {
        Godot.Collections.Dictionary Cast(Vector2 point)
        {
            var origin = _camera.ProjectRayOrigin(point);
            return GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + _camera.ProjectRayNormal(point) * _camera.Far, mask));
        }
        var hit = Cast(screen);
        if (hit.Count > 0) return hit;
        // Trimesh queries can miss an exact shared triangle edge due to float precision.
        // Retry only a hundredth of a viewport pixel away, never a whole neighboring tile.
        foreach (var offset in new[] { new Vector2(.01f, .01f), new Vector2(-.01f, .01f), new Vector2(.01f, -.01f), new Vector2(-.01f, -.01f) })
        { hit = Cast(screen + offset); if (hit.Count > 0) return hit; }
        return hit;
    }
    private Cell? PickTarget(Vector2 screen)
    {
        if(_tool=="select"&&CurrentView.Level is not null&&PlanePick(screen,ActiveLevel+1) is Cell plane)
        {var regionCell=plane with{Level=ActiveLevel};if(DigRegionAt(regionCell) is not null)return regionCell;}
        var hit = PickObject(screen, 11); // Terrain, completed objects and selectable construction plans.
        if (hit.Count == 0) return null;
        var collider = hit["collider"].AsGodotObject();
        if (collider.HasMeta("cell"))
        { var p = collider.GetMeta("cell").AsVector3I(); return new Cell(p.X, p.Z, p.Y); }
        var point=hit["position"].AsVector3()-hit["normal"].AsVector3()*.01f;
        var cell=new Cell((int)Math.Floor(point.X+DemoWorld.Width/2f),(int)Math.Floor(point.Z+DemoWorld.Depth/2f),(int)Math.Floor(point.Y));
        var above=cell+new Cell(0,0,1);
        if(hit["normal"].AsVector3().Y>.5f&&(_world.Farms.ContainsKey(above)||_world.Stockpiles.Contains(above)||_world.Fluids.Amount(above)>0))return above;
        return DemoWorld.Inside(cell)?cell:null;
    }
    private int? PickResident(Vector2 screen)
    {
        var hit = PickObject(screen, 15);
        if (hit.Count == 0) return null;
        var collider = hit["collider"].AsGodotObject();
        return collider.HasMeta("resident") ? collider.GetMeta("resident").AsInt32() : null;
    }
    private void SelectResident(int index)
    {
        _selectedResidents.Clear();_residentSelectionWorld=_world;
        if(_foundation is not null)
        {
            int floor=(int)Math.Floor((_world.Workers[index].Level-_foundation.Min.Level)/(double)_foundation.FloorHeight);
            _foundation.Step(floor-_foundation.Floor);_layerOffset=0;_placement=0;_revision=-1;DrawFoundation();
        }
        _buildMenu.Hide(); _inspector.Hide(); _worldMenu.Hide();
        _selectedWorker = index; _selectedCell = null; _selectionPanel.Show();
        if(DossierOpen)SelectDossierResident(index);
        RefreshUi();
    }
    private void TogglePanel(PanelContainer panel)
    {
        bool open = !panel.Visible;
        if(panel==_inspector){_billStation=null;FilterRecipes();}
        _combatPanel?.Hide();
        if(DossierOpen)CloseResidentPanel();
        _buildMenu.Hide(); _inspector.Hide(); _worldMenu.Hide(); _selectionPanel.Hide();
        if (open) panel.Show();
        if (panel != _buildMenu) SelectTool("select");
        _storagePanel?.QueueFree();_storagePanel=null;
    }
    private Vector3 WorldPosition(Cell c) => TerrainView.Point(c.X, c.Level, c.Z);

    private void PositionCamera()
    {
        _focus.X = Math.Clamp(_focus.X, -(TerrainMap.Size-1)/2f, (TerrainMap.Size-1)/2f); _focus.Z = Math.Clamp(_focus.Z, -(TerrainMap.Size-1)/2f, (TerrainMap.Size-1)/2f);
        var back=(new Vector3(.6f,0,.8f)*Mathf.Cos(_cameraPitch)+Vector3.Up*Mathf.Sin(_cameraPitch)).Rotated(Vector3.Up,_cameraYaw);
        // Orthographic size changes coverage, not distance. Keep the entire world
        // between the clipping planes, including at shallow pitch and map edges.
        var center=new Vector3(0,(TerrainMap.MinLevel+TerrainMap.MaxLevel+1)/2f,0);
        var half=new Vector3(TerrainMap.Size/2f,TerrainMap.VerticalSize/2f+16,TerrainMap.Size/2f);
        float projectedCenter=(center-_focus).Dot(back),extent=half.Dot(back.Abs());
        float distance=Math.Max(64,projectedCenter+extent+16);
        _camera.Near=.5f;_camera.Far=Math.Max(128,distance-projectedCenter+extent+16);
        _camera.Position=_focus+back*distance;_camera.LookAt(_focus,_topDown?-CameraBack:Vector3.Up);
    }
    private void FocusCamp() { _focus = WorldPosition(_world.Camp); PositionCamera(); }
    private void MoveCamera(double delta)
    {
        if (_tpsReview is not null) return;
        if (_consolePanel.Visible||_removeGrowingDialog?.Visible==true||_billDetails is not null) return;
        if (DossierOpen || _characterEditor is not null && _characterEditor.Visible) return;
        if (!GetWindow().HasFocus()) { _dragging = false; return; }
        if (GetViewport().GuiGetFocusOwner() is LineEdit) return;
        if (Input.IsPhysicalKeyPressed(Key.Meta) || Input.IsPhysicalKeyPressed(Key.Ctrl) || Input.IsPhysicalKeyPressed(Key.Alt)) return;
        bool arrows=GetViewport().GuiGetFocusOwner() is null;
        float x = (Input.IsPhysicalKeyPressed(Key.D) || arrows && Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) || arrows && Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0);
        float z = (Input.IsPhysicalKeyPressed(Key.S) || arrows && Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) || arrows && Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0);
        if (x == 0 && z == 0) return;
        _focus += (CameraRight * x + CameraBack * z) * (float)delta * _camera.Size * .6f;
        PositionCamera();
    }

    private void Act(Cell c)
    {
        if(_tool=="rally"){CombatNotice(_world.SetRallyPoint(c));SelectTool("select");return;}
        if (_tool == "foundation") { MarkFoundation(c); return; }
        if(_tool is "growingzone" or "unfarm"){MarkFarm(c);return;}
        if(_tool is "stockpile" or "unstockpile"){MarkStockpile(c);return;}
        if(_tool=="cancelplan"){foreach(var job in _world.Jobs.Where(j=>j.Cell==c).ToArray())_world.CancelJob(job);SyncWorld();RefreshUi();return;}
        if(_tool is "chop" or "planttree"){_world.QueueTree(c,_tool=="chop");RefreshUi();return;}
        if (_developerMode && _creative && CreativeAct(c)) { SyncWorld(); RefreshUi(); return; }
        if (_tool is "dig" or "digstairs") _world.QueueDig(c,_tool=="digstairs",_facing);
        else if (_tool == "erase") _world.QueueDemolish(c);
        else if (_tool == "blueprint") _world.QueueBlueprint(c, _world.Blueprint);
        else if (Enum.TryParse<BuildKind>(_tool, out var kind)) _world.QueueBuild(c, kind, facing: _facing);
        else
        {
            _selectedResidents.Clear();_selectedWorker = -1; _selectedCell = c;
            _buildMenu.Hide(); _inspector.Hide(); _worldMenu.Hide(); _selectionPanel.Show();
        }
        RefreshUi();
    }

    private void BuildScene()
    {
        _dayEnvironment=new Godot.Environment
        {
            BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("454d43"),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,
            AmbientLightColor=new Color("e1edda"),AmbientLightEnergy=.4f
        };
        AddChild(new WorldEnvironment{Environment=_dayEnvironment});
        _sun=new DirectionalLight3D{RotationDegrees=new Vector3(-55,-25,0),LightColor=new Color("ffe7bb"),LightEnergy=.7f,ShadowEnabled=true,DirectionalShadowMaxDistance=90};AddChild(_sun);
        ConfigureDepthLighting();
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 38, Far = 500, Current = true };
        AddChild(_camera); FocusCamp();BuildWeatherVisuals();
        _terrainView = new TerrainView { SynchronousMeshing = OS.GetCmdlineUserArgs().Contains("--terrain-sync") }; AddChild(_terrainView); _terrainView.Generate(_world,CurrentView);
        _structures = new Node3D(); _ghosts = new Node3D(); _hover = new Node3D();
        AddChild(_structures); AddChild(_ghosts); AddChild(_hover);
        _foundationOutline = new Node3D(); AddChild(_foundationOutline); CreatePeople();
    }

    private static StandardMaterial3D Mat(string color) => new() { AlbedoColor = new Color(color), Roughness = 1 };
    private static void Box(Node parent, Vector3 p, Vector3 size, Material material)
        => parent.AddChild(new MeshInstance3D { Position = p, Mesh = new BoxMesh { Size = size, Material = material } });
    private static void Clear(Node node)
    { foreach (Node child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }

    private void DrawBuilding(Node parent, Cell cell, BuildKind kind, bool ghost = false, int facing = 0, bool fullHeight = false)
    {
        Vector3 p = WorldPosition(cell);
        if (ghost)
        {
            var shape = new Node3D(); parent.AddChild(shape);
            DrawBuilding(shape, cell, kind, false, facing, fullHeight: true);
            foreach (var mesh in shape.GetChildren().OfType<MeshInstance3D>())
                { mesh.MaterialOverride = PreviewMaterial(false); mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; }
            return;
        }
        if(kind==BuildKind.Ladder)
        {
            var d=DemoWorld.StairDirection(facing);
            var mesh=new MeshInstance3D{Position=p+new Vector3(d.X*.46f,.5f,d.Z*.46f),
                Rotation=new Vector3(0,Mathf.Atan2(d.X,d.Z),0),
                Mesh=new QuadMesh{Size=new Vector2(.9f,1),Material=BlockTextures.Material("nodwarf:ladder")}};
            mesh.SetMeta("voxel_bounds",new Aabb(new Vector3(-.45f,-.5f,-.05f),new Vector3(.9f,1,.1f)));
            parent.AddChild(mesh);return;
        }
        if (BlockCatalog.IsStairs(kind))
        {
            var d = DemoWorld.StairDirection(facing);
            for (int step = 0; step < 2; step++)
            {
                float h = (step + 1) * TerrainMap.LayerHeight / 2;
                float along = -.25f + step * .5f;
                BlockTextures.Box(parent, p + new Vector3(d.X * along, h / 2, d.Z * along), new Vector3(d.X == 0 ? 1f : .5f, h, d.Z == 0 ? 1f : .5f), BlockCatalog.Id(kind));
            }
            return;
        }
        if (BlockCatalog.IsWall(kind))
        {
            float h = fullHeight?1f:Math.Clamp(CurrentView.CellTop(cell)-cell.Level,0,1);
            BlockTextures.Box(parent,p+Vector3.Up*h/2,new Vector3(1,h,1),BlockCatalog.Id(kind),
                fullHeight?BlockFaces.All:BlockExposure.BuildingFaces(_world,CurrentView,cell));
        }
        else if(kind==BuildKind.Floodgate)
        {
            bool open=_world.GateOpen(cell);var metal=BlockTextures.Material("nodwarf:floodgate");
            Box(parent,p+new Vector3(-.43f,.5f,0),new Vector3(.12f,1,.25f),metal);
            Box(parent,p+new Vector3(.43f,.5f,0),new Vector3(.12f,1,.25f),metal);
            Box(parent,p+Vector3.Up*(open?.92f:.45f),new Vector3(.8f,open?.16f:.9f,.22f),Mat("497b8c"));
        }
        else if (kind == BuildKind.Door)
        {
            Box(parent, p + new Vector3(-0.4f, 0.85f, 0), new Vector3(0.13f, 1.7f, 0.22f), Mat("a37a50"));
            Box(parent, p + new Vector3(0.4f, 0.85f, 0), new Vector3(0.13f, 1.7f, 0.22f), Mat("a37a50"));
            Box(parent, p + new Vector3(0, 1.75f, 0), new Vector3(0.95f, 0.18f, 0.26f), Mat("bd9565"));
            Box(parent, p + new Vector3(0, 0.03f, 0), new Vector3(0.8f, 0.04f, 0.65f), Mat("c7ab7b"));
        }
        else if (kind is BuildKind.Furnace or BuildKind.Stove)
        {
            BlockTextures.Box(parent,p+Vector3.Up*.5f,Vector3.One,BlockCatalog.Id(kind));
        }
        else if(kind is BuildKind.Table or BuildKind.GameTable or BuildKind.Chair)
        {
            float h=kind==BuildKind.Chair?.45f:.75f;
            BlockTextures.Box(parent,p+Vector3.Up*h,new Vector3(.8f,.12f,.8f),"nodwarf:planks");
            foreach(float x in new[]{-.3f,.3f})foreach(float z in new[]{-.3f,.3f})BlockTextures.Box(parent,p+new Vector3(x,h/2,z),new Vector3(.1f,h,.1f),"nodwarf:log");
            if(kind==BuildKind.Chair)BlockTextures.Box(parent,p+new Vector3(0,.8f,.35f),new Vector3(.8f,.65f,.1f),"nodwarf:planks");
            if(kind==BuildKind.GameTable)for(int x=0;x<4;x++)for(int z=0;z<4;z++)Box(parent,p+new Vector3((x-1.5f)*.16f,h+.07f,(z-1.5f)*.16f),new Vector3(.16f,.02f,.16f),Mat((x+z)%2==0?"e2d6ad":"433c31"));
        }
        else if(kind==BuildKind.Workbench)
        {
            foreach(float x in new[]{-.32f,.32f})foreach(float z in new[]{-.28f,.28f})BlockTextures.Box(parent,p+new Vector3(x,.3f,z),new Vector3(.13f,.6f,.13f),"nodwarf:workbench");
            BlockTextures.Box(parent,p+Vector3.Up*.65f,new Vector3(.95f,.16f,.85f),"nodwarf:workbench");
        }
        else if(kind==BuildKind.Mill)
        {
            BlockTextures.Box(parent,p+Vector3.Up*.2f,new Vector3(.8f,.4f,.8f),"nodwarf:mill");
            parent.AddChild(new MeshInstance3D{Position=p+Vector3.Up*.52f,Mesh=new CylinderMesh{TopRadius=.39f,BottomRadius=.39f,Height=.24f,Material=BlockTextures.Material("nodwarf:mill")}});
            Box(parent,p+new Vector3(.25f,.77f,0),new Vector3(.08f,.3f,.08f),Mat("b29566"));
        }
        else if(kind==BuildKind.Loom)
        {
            foreach(float side in new[]{-.4f,.4f})BlockTextures.Box(parent,p+new Vector3(side,.6f,0),new Vector3(.1f,1.2f,.2f),"nodwarf:loom");
            BlockTextures.Box(parent,p+Vector3.Up*1.1f,new Vector3(.9f,.1f,.2f),"nodwarf:loom");
            Box(parent,p+Vector3.Up*.6f,new Vector3(.65f,.85f,.06f),Mat("dfd3b8"));
            for(int i=-3;i<=3;i++)Box(parent,p+new Vector3(i*.09f,.6f,.045f),new Vector3(.014f,.8f,.014f),Mat("9e8c6d"));
        }
        else if(kind==BuildKind.Bed)
        {
            BlockTextures.Box(parent,p+Vector3.Up*.15f,new Vector3(.8f,.3f,.96f),"nodwarf:bed");
            Box(parent,p+Vector3.Up*.35f,new Vector3(.76f,.16f,.9f),Mat("994c48"));
            Box(parent,p+new Vector3(0,.46f,-.28f),new Vector3(.62f,.1f,.25f),Mat("dfdac5"));
        }
        else if(kind==BuildKind.Campfire)
        {
            BlockTextures.Box(parent,p+Vector3.Up*.1f,new Vector3(.85f,.2f,.25f),"nodwarf:campfire");
            BlockTextures.Box(parent,p+Vector3.Up*.22f,new Vector3(.25f,.2f,.85f),"nodwarf:campfire");
            var flame=new MeshInstance3D{Name="Flame",Position=p+Vector3.Up*.6f,Mesh=new CylinderMesh{TopRadius=0,BottomRadius=.24f,Height=.65f,RadialSegments=6,Material=new StandardMaterial3D{AlbedoColor=new Color("f4a443"),EmissionEnabled=true,Emission=new Color("ed8435")}},Visible=_world.FireFuel.GetValueOrDefault(cell)>0};
            parent.AddChild(flame);
        }
        else if(!BlockTextures.Model(parent,p,BlockCatalog.Id(kind)))
        {
            BlockTextures.Box(parent,p+Vector3.Up*.2f,new Vector3(.65f,.4f,.6f),BlockCatalog.Id(kind));
            BlockTextures.Box(parent,p+Vector3.Up*.52f,new Vector3(.3f,.3f,.3f),BlockCatalog.Id(kind));
            BlockTextures.Box(parent,p+Vector3.Up*.72f,new Vector3(.83f,.16f,.43f),BlockCatalog.Id(kind));
        }
    }

    private static StandardMaterial3D PreviewMaterial(bool invalid) => new()
    {
        AlbedoColor = invalid ? new Color(.9f, .3f, .25f, .28f) : new Color(.4f, .72f, .88f, .20f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Roughness = 1
    };
    private void DrawFoundation()
    {
        Clear(_foundationOutline);
        if (_foundation is null) return;
        float height = BuildLevel + .025f;
        var min = _foundation.Min; var max = _foundation.Max;
        float width = max.X - min.X + 1, depth = max.Z - min.Z + 1;
        var center = TerrainView.Point((min.X + max.X) / 2f, height, (min.Z + max.Z) / 2f);
        var edge = Mat("c7c081");
        Box(_foundationOutline, center + new Vector3(0, 0, -depth / 2), new Vector3(width, .035f, .035f), edge);
        Box(_foundationOutline, center + new Vector3(0, 0, depth / 2), new Vector3(width, .035f, .035f), edge);
        Box(_foundationOutline, center + new Vector3(-width / 2, 0, 0), new Vector3(.035f, .035f, depth), edge);
        Box(_foundationOutline, center + new Vector3(width / 2, 0, 0), new Vector3(.035f, .035f, depth), edge);
    }
    private void MarkFoundation(Cell c)
    {
        if (_foundationStart is not Cell start)
        { _foundationStart = c; _world.Log("已选地基起点，请点击同高空地的另一角。"); RefreshUi(); return; }
        if (start.Level != c.Level) { _world.Log("两角必须同高；右键取消重新选择。"); RefreshUi(); return; }
        var plan = new FoundationPlan(start, c);
        for (int x = plan.Min.X; x <= plan.Max.X; x++)
        for (int z = plan.Min.Z; z <= plan.Max.Z; z++)
            if (_world.Surface(x, z).Level != c.Level || !_world.NaturalSolid(new Cell(x,z,c.Level-1)) || _world.Terrain[x, z].Ramp || _world.Terrain[x, z].Tree)
            { _world.Log("地基范围需要同高、无树木的平地；此操作不会自动填土。"); RefreshUi(); return; }
        plan.SetFloorHeight((int)_floorHeight.Value); _foundation = plan; _foundationStart = null; _layerOffset=0; _placement=0; _cutaway=true; _revision=-1;
        DrawFoundation(); SelectTool("select");
        _world.Log($"地基顶面设为第 0 层，世界高度 {plan.WorldLevel} m；层高 {plan.FloorHeight} m。"); RefreshUi();
    }
    private void ChangeFloor(int direction)
    {
        if (_foundation is null) { _world.Log("先在建筑菜单划定地基，再用 Command/Ctrl + 加减号定位楼层。"); return; }
        int previousLevel=ActiveLevel;
        _foundation.Step(direction);
        if(_foundation.WorldLevel<=TerrainMap.MinLevel || _foundation.WorldLevel>TerrainMap.MaxLevel) { _foundation.Step(-direction);_world.Log("已到当前 Demo 的高度边界。");return; }
        _layerOffset=0; _placement=0; _focus.Y = _foundation.WorldLevel; _revision=-1;
        if(_digEnd is Cell end)_digEnd=end with{Level=Math.Clamp(end.Level+ActiveLevel-previousLevel,TerrainMap.MinLevel+1,TerrainMap.MaxLevel)};
        PositionCamera(); DrawFoundation(); _hoverCell = null; Clear(_hover); RefreshUi();
    }

    private void StepLayer(int direction)
    {
        _heightSelected=true;int datum=_foundation?.WorldLevel??_world.Camp.Level;
        int previousLevel=ActiveLevel;_interiorView=false;_cutaway=true;
        _layerOffset=Math.Clamp(_layerOffset+direction,TerrainMap.MinLevel+1-datum,TerrainMap.MaxLevel-datum);
        if(_digEnd is Cell end)_digEnd=end with{Level=Math.Clamp(end.Level+ActiveLevel-previousLevel,TerrainMap.MinLevel+1,TerrainMap.MaxLevel)};
        _hoverCell=null;_focus.Y=ActiveLevel; _revision=-1; PositionCamera(); DrawFoundation(); RefreshUi();
    }
    private void SetPlacement(int mode)
    {
        if(mode!=0 && _foundation is null) { _world.Log("先划定地基，确定楼板与天花板的高度。"); SelectTool("foundation"); return; }
        if(mode!=0 && (!Enum.TryParse<BuildKind>(_tool,out var kind)||!BlockCatalog.IsWall(kind))) SelectTool("Planks");
        _cutaway=true; _placement=mode; _revision=-1; DrawFoundation(); _hoverCell=null; RefreshUi();
    }
    private void StartTwoStoreyDemo()
    {
        if(!_world.StartTwoStoreyDemo()) return;
        _foundation=new FoundationPlan(_world.Camp,_world.Camp+new Cell(5,7));
        _floorHeight.Value=3; _layerOffset=0; _placement=0; _cutaway=true;
        _inspector.Hide(); _buildMenu.Hide(); SelectTool("select"); FocusCamp(); _camera.Size=20;
        DrawFoundation(); SyncWorld(); RefreshUi();
    }

    private void AddBuildingVisual(Node parent, Cell cell, BuildKind kind, bool ghost = false, int facing = 0)
    {
        if (!VisibleAt(cell)) return;
        var visual = new Node3D(); parent.AddChild(visual); DrawBuilding(visual, cell, kind, ghost, facing);
        if(ghost)
        {
            var body=new StaticBody3D { Position=WorldPosition(cell)+Vector3.Up*.5f,CollisionLayer=2,CollisionMask=0 };
            body.SetMeta("cell",new Vector3I(cell.X,cell.Level,cell.Z)); visual.AddChild(body);
            body.AddChild(new CollisionShape3D { Shape=new BoxShape3D { Size=Vector3.One } });
        }
        foreach (var mesh in visual.GetChildren().OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null) continue;
            var bounds = mesh.HasMeta("voxel_bounds")?mesh.GetMeta("voxel_bounds").AsAabb():mesh.Mesh.GetAabb();
            var body = new StaticBody3D { Position=bounds.GetCenter(),CollisionLayer = 2, CollisionMask = 0 };
            body.SetMeta("cell", new Vector3I(cell.X, cell.Level, cell.Z));
            mesh.AddChild(body); body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = bounds.Size } });
        }
    }

    private void DrawDig(Node parent,Cell cell,bool stairs,int facing)
    {
        var color=Mat("f0b56b");var p=WorldPosition(cell);p.Y=Math.Min(cell.Level+1,CurrentView.CellTop(cell))+.02f;
        foreach(float side in new[]{-.46f,.46f})
        {
            Box(parent,p+new Vector3(side,0,0),new Vector3(.035f,.035f,.94f),color);
            Box(parent,p+new Vector3(0,0,side),new Vector3(.94f,.035f,.035f),color);
        }
        color.ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded;
        color.NoDepthTest=CurrentView.Level is not null;
        Box(parent,p+new Vector3(0,.035f,0),new Vector3(.055f,.035f,.55f),color);
        ((Node3D)parent.GetChild(parent.GetChildCount()-1)).Rotation=new Vector3(0,.65f,0);
        Box(parent,p+new Vector3(-.12f,.04f,-.16f),new Vector3(.42f,.045f,.07f),color);
        ((Node3D)parent.GetChild(parent.GetChildCount()-1)).Rotation=new Vector3(0,.65f,0);
        if(stairs) {var d=DemoWorld.StairDirection(facing);Box(parent,p+new Vector3(d.X*.3f,.04f,d.Z*.3f),new Vector3(.16f,.08f,.16f),color);}
        var body=new StaticBody3D{Position=p,CollisionLayer=2,CollisionMask=0};body.SetMeta("cell",new Vector3I(cell.X,cell.Level,cell.Z));parent.AddChild(body);
        body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new Vector3(.95f,.04f,.95f)}});
    }
    private void StartUndergroundDemo()
    {
        if(!_world.StartUndergroundDemo())return;
        _foundation=new FoundationPlan(_world.Camp,_world.Camp+new Cell(5,7));_floorHeight.Value=3;
        _layerOffset=0;_placement=0;_cutaway=true;_inspector.Hide();_buildMenu.Hide();SelectTool("select");
        _foundation.Step(-1);_focus=WorldPosition(_world.Camp+new Cell(2,3,-3));_camera.Size=17;
        PositionCamera();DrawFoundation();SyncWorld();RefreshUi();
    }

    private void SyncWorld()
    {
        using var timing = _world.Probe?.Measure("view.sync-world") ?? default;
        PrepareInteriorWalls();
        BindWorldLog();
        _terrainView.PriorityCell=new(_focus.X+(TerrainMap.Size-1)/2f,_focus.Z+(TerrainMap.Size-1)/2f);
        _terrainView.Generate(_world,CurrentView);
        if(OS.GetCmdlineUserArgs().Any(a=>a is "--smoke-test" or "--interaction-test"))_terrainView.FlushMeshes();
        _overview.Slice=CurrentView;
        if(_visualWorld!=_world||_terrainRevision!=_world.Terrain.Revision){_terrainRevision=_world.Terrain.Revision;_overview.RefreshMap();}
        SyncStructuresAndPlans();
        DrawItems();DrawFarms();SyncDesignationMarkers();
        _revision = _world.Revision;
        _hoverCell = null;
    }

    private void CreatePeople()
    {
        foreach (var person in _people) { RemoveChild(person); person.QueueFree(); }
        _people.Clear();

        for (int i = 0; i < _world.Workers.Count; i++)
        {
            var node = new Node3D(); AddChild(node); _people.Add(node);
            node.AddChild(new Sprite3D
            {
                Texture = EquipmentArt.Texture(_world.Workers[i].Appearance,_world.Inventory(_world.Workers[i])), PixelSize = WorldScale.DwarfHeightMeters / 300f, Position = new Vector3(0, WorldScale.DwarfHeightMeters * .49f, 0),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Shaded = false, Modulate = Colors.White, AlphaCut = SpriteBase3D.AlphaCutMode.Discard
            });
            node.AddChild(new Label3D
            {
                Text = _world.Workers[i].Name, Position = new Vector3(0, 1.35f, 0),
                FontSize = 36, PixelSize = 0.006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = Ink, OutlineSize = 8,
                Font = GameFont.Regular
            });
            var body = new StaticBody3D { CollisionLayer = 4, CollisionMask = 0, Position = new Vector3(0, WorldScale.DwarfHeightMeters / 2, 0) };
            body.SetMeta("resident", i); node.AddChild(body);
            body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = .27f, Height = WorldScale.DwarfHeightMeters } });
            node.AddChild(new Label3D{Name="Cargo",Position=new Vector3(0,1.7f,0),FontSize=28,PixelSize=.004f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Font=GameFont.Regular,OutlineSize=8});
            AddResidentContact(node);
            AddSelectionRing(node);
            AddActorModel(node);
        }
        for (int i = 0; i < _residentButtons.Count; i++) { _residentButtons[i].Visible=i<_people.Count; if(i<_people.Count) _residentButtons[i].Icon = _people[i].GetChild<Sprite3D>(0).Texture; }
        SyncEquipmentVisuals(true);
        AnimatePeople();
        AnimateBuildingFalls(0);
    }

    private void AnimatePeople()
    {
        SyncEquipmentVisuals();
        AnimateCombat();
        UpdateCommandMarkers();
        for (int i = 0; i < _people.Count; i++)
        {
            var worker = _world.Workers[i]; var node = _people[i];
            float phase = (float)_clock.GameSeconds * 13 + i * 2;
            float bob = worker.Path.Count > 0 ? MathF.Abs(MathF.Sin(phase)) * 0.08f : 0;
            node.Position = new Vector3((float)worker.X - (DemoWorld.Width - 1) / 2f, _world.WorkerHeight(worker) + bob, (float)worker.Z - (DemoWorld.Depth - 1) / 2f);
            node.Visible=VisibleAt(worker.Tile);
            node.GetChild<StaticBody3D>(2).CollisionLayer=node.Visible?4u:0u;
            node.GetChild<Label3D>(1).Visible = _camera.Size < 42 || IsResidentSelected(i);
            node.GetNode<MeshInstance3D>("SelectionRing").Visible=IsResidentSelected(i);
            var cargo=worker.Job?.Materials.FirstOrDefault(r=>r.Stage==1);
            node.GetChild<Label3D>(3).Text=cargo is null?"":$"搬运 {ItemCatalog.Get(cargo.Item).Name} ×{cargo.Count}";
            var sprite = node.GetChild<Sprite3D>(0);
            sprite.Modulate = worker.Combat.FlashUntil>_world.SimulationTime?new Color("ffb0a0"):IsResidentSelected(i) ? new Color("fff0ad") : Colors.White;
            sprite.Rotation = new Vector3(0, 0, worker.Person.Incapacitated?Mathf.Pi/2:worker.Combat.FlashUntil>_world.SimulationTime?.2f:worker.Job is not null && worker.Path.Count == 0 ? MathF.Sin(phase) * 0.1f : 0);
            if (worker.Path.TryPeek(out var next)) sprite.FlipH = next.X < worker.X;
            UpdateActorModel(node,worker,bob);
        }
    }

    private void UpdateHover()
    {
        if(UpdateLeftSelectionPreview())return;
        if(_consolePanel.Visible) { Clear(_hover); _hoverCell=null;return; }
        if(_digStart is Cell start && _digEnd is Cell end) { DrawDigDrag(start,end);return; }
        Cell? picked = PickForTool(GetViewport().GetMousePosition());
        if (GetViewport().GuiGetHoveredControl() is not null) picked = null;
        if (picked == _hoverCell) return;
        _hoverCell = picked; Clear(_hover);
        if (picked is not Cell cell || _tool == "select") return;
        if(_tool=="canceldig")
        {
            _hoverCell=null;DrawDigDrag(cell,cell);return;
        }
        if(_tool is "dig" or "digstairs")
        {
            bool valid=_world.CanDig(cell,_tool=="digstairs");
            _hint.Text=$"{(_tool=="digstairs"?"楼梯坑 · 3 米":"挖掘 · 1 格")} · 目标 {cell.X}, {cell.Z} / {cell.Level} 至 {cell.Level+(_tool=="digstairs"?3:1)} m · 左键选起点，移动鼠标 / +− 选层，再次左键确认";
            var p=WorldPosition(cell)+Vector3.Up*1.02f;
            Box(_hover,p,new Vector3(.95f,.04f,.95f),PreviewMaterial(!valid));
            return;
        }
        if(_tool is "growingzone" or "unfarm"){DrawFarmPreview(cell);return;}
        if (_tool == "blueprint")
        {
            foreach (var part in _world.Blueprint)
                if (DemoWorld.Inside(cell + part.Offset)) DrawBuilding(_hover, cell + part.Offset, part.Kind, true, part.Facing);
        }
        else
        {
            bool hasBuild = Enum.TryParse<BuildKind>(_tool, out var build);
            bool valid = hasBuild && (_developerMode&&_creative ? _world.CanDeveloperPlace(cell,build,_facing) : _world.CanPlace(cell, build, _facing));
            if(hasBuild&&build==BuildKind.Ladder)_hint.Text="木梯 · 每段 1 米 · 贴实心墙安装 · R 旋转靠墙方向";
            if(hasBuild && BlockCatalog.IsStairs(build))
            {
                var blocked=_world.StairObstructions(cell).ToArray();
                foreach(var obstruction in blocked)DrawClearanceOutline(obstruction);
                if(blocked.Length>0)_hint.Text=$"楼梯需要清空：{string.Join("、",blocked.Select(c=>$"{c.Level}m"))} · 切到挖掘逐格清除，再安装；R 调整上行方向";
            }
            if (hasBuild)
            {
                var shape = new Node3D(); _hover.AddChild(shape); DrawBuilding(shape, cell, build, false, _facing, fullHeight: true);
                foreach (var mesh in shape.GetChildren().OfType<MeshInstance3D>()) { mesh.MaterialOverride = PreviewMaterial(!valid); mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; }
            }
            Box(_hover, WorldPosition(cell) + new Vector3(0, 0.025f, 0), new Vector3(1f, 0.025f, 1f), Mat(_tool == "foundation" ? "c7c081" : _tool == "erase" ? "d68c70" : valid ? "edd295" : "c78471"));
            if (Enum.TryParse<BuildKind>(_tool, out build) && BlockCatalog.IsStairs(build))
            {
                var d = DemoWorld.StairDirection(_facing);
                Box(_hover, WorldPosition(cell) + new Vector3(d.X * .35f, .22f, d.Z * .35f), new Vector3(.2f, .2f, .2f), Mat(valid ? "f5d474" : "c78471"));
            }
        }
    }

    private static string GroundName(Ground g) => g.Tree ? "林地（暂不能清除）" : g.Ramp ? BlockCatalog.Get(g.BlockId).Name : g.Kind switch
    { GroundKind.Sand => "沙子", GroundKind.Rock => "岩地", GroundKind.Iron => "铁矿露头（待开采版本）", GroundKind.Coal => "煤层露头（待开采版本）", GroundKind.Soil => "裸土", _ => "草地" };

    private readonly List<Label> _resourceCounts=new();
    private readonly Dictionary<string,Control> _buildPages=new();
    private readonly Dictionary<string,Button> _categoryButtons=new();
    private PanelContainer _mapPanel=null!, _diagnosticsPanel=null!;
    private Label _timeStatus=null!, _devBadge=null!;
    private Button _normalSpeed=null!, _fastSpeed=null!;
    private string _buildCategory="命令";
    private Button IconButton(Node parent,string icon,string title,Action action,bool tile=false)
    {
        var button=Button(parent,title,action);button.Icon=UiIcons.Get(icon);button.ExpandIcon=true;
        button.AddThemeConstantOverride("icon_max_width",tile?36:22);
        button.CustomMinimumSize=tile?new Vector2(90,72):new Vector2(Math.Max(38,46+title.Sum(ch=>ch>127?14:8)),32);
        if(tile){button.IconAlignment=HorizontalAlignment.Center;button.VerticalIconAlignment=VerticalAlignment.Top;button.Alignment=HorizontalAlignment.Center;}
        button.TooltipText=title;return button;
    }
    private void ShowBuildPage(string name)
    {
        _buildCategory=name;
        if(_architectSearch is not null)_architectSearch.Text="";
        _architectDescription.Text=ArchitectHelp(name);_architectDetailTitle.Text=name;_architectDetailIcon.Texture=null;_architectDetailIcon.Hide();
        FilterArchitect();
        foreach(var (key,button) in _categoryButtons)button.AddThemeStyleboxOverride("normal",ButtonStyle(key==name?"66583d":"303942"));
    }

    private void BuildUi()
    {
        var layer=new CanvasLayer();AddChild(layer);
        var root=new Control{MouseFilter=Control.MouseFilterEnum.Ignore};
        layer.AddChild(root);root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.Theme=new Theme{DefaultFont=GameFont.Regular,DefaultFontSize=14};
        var resources=Panel(root,new Vector2(8,8),new Vector2(78,0));
        resources.Name="ResourceHud";resources.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
        var stock=new VBoxContainer();resources.AddChild(stock);
        foreach(var (id,name) in new[]{("stone","石料"),("wood","木料"),("iron","铁锭"),("coal","煤炭"),("sword","铁剑"),("raw_iron","粗铁"),("deep_rubble","深板岩碎石"),("soil","土壤"),("sand","沙子"),("raw_food","食材（含小麦、面粉、土豆、蘑菇）"),("meal","口粮"),("cotton","棉絮"),("cloth","布")})
        {
            var row=new HBoxContainer{TooltipText=name+" · 实体物资总数（含已预留与搬运中）"};stock.AddChild(row);
            row.AddChild(new TextureRect{Texture=UiIcons.Get(id),CustomMinimumSize=new Vector2(22,22),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=Control.MouseFilterEnum.Pass});
            var value=Text("",14,Ink);StyleFloatingText(value);row.AddChild(value);_resourceCounts.Add(value);
        }
        _devBadge=Text("",12,Accent);stock.AddChild(_devBadge);
        var portraits=new HBoxContainer{Position=new Vector2(-108,8),AnchorLeft=.5f,AnchorRight=.5f};
        portraits.AddThemeConstantOverride("separation",6);root.AddChild(portraits);
        for(int i=0;i<_world.Workers.Count;i++)
        {
            int index=i;
            var portrait=Button(portraits,_world.Workers[i].Name,()=>{SelectTool("select");SelectResident(index);_focus=_people[index].Position;PositionCamera();});
            portrait.Icon=_people[i].GetChild<Sprite3D>(0).Texture;portrait.ExpandIcon=true;
            portrait.IconAlignment=HorizontalAlignment.Center;portrait.VerticalIconAlignment=VerticalAlignment.Top;
            portrait.AddThemeConstantOverride("icon_max_width",40);portrait.CustomMinimumSize=new Vector2(66,78);
            _residentButtons.Add(portrait);
        }
        var bottom=Panel(root,Vector2.Zero,new Vector2(0,40));
        bottom.ZIndex=21;bottom.AddThemeStyleboxOverride("panel",HudStyle("151a20",0));
        bottom.AnchorTop=bottom.AnchorBottom=bottom.AnchorRight=1;
        bottom.OffsetTop=-40;bottom.OffsetBottom=0;bottom.OffsetRight=0;
        var tabs=new HBoxContainer();tabs.AddThemeConstantOverride("separation",1);bottom.AddChild(tabs);
        _buildTab=HudTab(tabs,"建筑规划",()=>TogglePanel(_buildMenu),"建筑、命令与种植 [B]");
        _colonyTabs[8]=HudTab(tabs,"工作",()=>OpenColonyPage(8),"各类工作的优先级");
        _colonyTabs[7]=HudTab(tabs,"作息",()=>OpenColonyPage(7),"每日 24 小时安排");
        _colonyTabs[6]=HudTab(tabs,"研究",()=>OpenColonyPage(6),"聚落研究与解锁");
        HudTab(tabs,"生产",()=>OpenProductionBills(),"工坊与生产订单");
        HudTab(tabs,"地图 / 存档",()=>TogglePanel(_worldMenu),"区域地图、保存与读取");
        HudTab(tabs,"回到营地",()=>{if(DossierOpen)CloseResidentPanel();FocusCamp();},"定位营地 [F]");
        HudTab(tabs,"视图",()=>{if(DossierOpen)CloseResidentPanel();_viewControls.Visible=!_viewControls.Visible;},"楼层、旋转、切墙与俯视");

        BuildArchitect(root);
        _selectionPanel = Panel(root, Vector2.Zero, new Vector2(400, 0)); BottomLeft(_selectionPanel, 282, 400);
        _selectionPanel.OffsetBottom=-76;
        var selectionColumn = new VBoxContainer(); selectionColumn.AddThemeConstantOverride("separation", 8); _selectionPanel.AddChild(selectionColumn);
        _selectionTabs=new HBoxContainer();_selectionTabs.AddThemeConstantOverride("separation",1);selectionColumn.AddChild(_selectionTabs);
        foreach(var (label,page) in new[]{("人物",0),("需求",1),("健康",2),("装备",3),("社交",4),("成长",5)})
        {int target=page;var tab=Button(_selectionTabs,label,()=>OpenPersonPage(target));tab.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;tab.AddThemeStyleboxOverride("normal",RStyle("514630","817457",5,4));if(page==0)_residentDetails=tab;}
        _selectionTitle=Text("",22,Ink,true);selectionColumn.AddChild(_selectionTitle);_selectionTitle.Hide();
        _selection = Text("", 14, Ink, true); selectionColumn.AddChild(_selection);
        _constructionProgress=RMeter(selectionColumn,0,1,Accent,7);_constructionProgress.Hide();
        var actions = new HBoxContainer(); selectionColumn.AddChild(actions);
        _locateSelectionAction=Button(actions, "定位", () => { if (_selectedWorker >= 0) _focus = _people[_selectedWorker].Position; else if (_selectedCell is Cell c) _focus = WorldPosition(c); PositionCamera(); });
        _editCharacter = Button(actions, "外观", OpenCharacterEditor);
        _medicalBedAction=Button(actions,"设为医疗床",()=>{if(_selectedCell is Cell bed){var result=_world.SetMedicalBed(bed,!_world.IsMedicalBed(bed));ShowActionFeedback(result.Message,!result.Ok);RefreshUi();}});
        _waterPointAction=Button(actions,"设为取水点",()=>{if(_selectedCell is Cell c){var result=_world.SetWaterPoint(c,!_world.WaterPoints.Contains(c));ShowActionFeedback(result.Message,!result.Ok);RefreshUi();}});
        _waterPointAction.TooltipText="指定可饮用水源；需要水桶和安全岸边通路。取消后不会销毁已取出的水。";
        _medicalBedAction.TooltipText="保留给伤员；改回普通床会释放预约并停止相关搬运。";
        _storageAction=Button(actions,"堆场设置",()=>{if(_selectedCell is Cell cell&&_world.EditableStorageAt(cell) is {} zone)ShowStorage(zone);else ShowActionFeedback("请选择已划定的堆场地格。",true);});
        BuildObjectCommands(root);
        _gateAction=Button(_objectCommands,"打开闸门",ToggleSelectedGate);StyleObjectCommand(_gateAction,"floodgate");_gateAction.Hide();
        BuildGrowingZoneSettings(selectionColumn);

        BuildProductionBrowser(root);
        _worldMenu = Panel(root, Vector2.Zero, new Vector2(330, 170)); BottomLeft(_worldMenu, 430, 370);_worldMenu.OffsetLeft=112;_worldMenu.OffsetRight=482;
        var mapScroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_worldMenu.AddChild(mapScroll);
        var worldColumn = new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill}; worldColumn.AddThemeConstantOverride("separation", 10); mapScroll.AddChild(worldColumn);
        worldColumn.AddChild(Text($"区域地图 · {TerrainMap.Size}×{TerrainMap.Size}", 17, Ink));
        worldColumn.AddChild(Text("新地图种子", 13, Muted));
        _seed = new SpinBox { MinValue = int.MinValue, MaxValue = int.MaxValue, Value = _world.Terrain.Seed, CustomMinimumSize = new Vector2(160, 30) }; worldColumn.AddChild(_seed);UiAccess.Name(_seed,"地图种子");
        Button(worldColumn, "生成新地图…", Reset);
        worldColumn.AddChild(new HSeparator());worldColumn.AddChild(Text("存档",15,Accent));
        var saveRow=new HBoxContainer();worldColumn.AddChild(saveRow);
        IconButton(saveRow,"save","存档 [F5]",SaveSession);
        IconButton(saveRow,"map","读档 [F9]",RequestLoadSession);
        Button(worldColumn,"返回开始菜单",RequestTitleScreen);
        BuildStoryInterface(root,worldColumn);
        BuildResidentMindInterface(root,worldColumn);
        _resetDialog = new ConfirmationDialog { Title = "重新生成地图", OkButtonText = "清空并生成", CancelButtonText = "保留当前营地" };
        AddChild(_resetDialog); _resetDialog.Confirmed += ConfirmReset; _resetDialog.Canceled += CancelReset;

        _viewControls=Panel(root,Vector2.Zero,new Vector2(288,154));
        _viewControls.AnchorLeft=_viewControls.AnchorRight=_viewControls.AnchorTop=_viewControls.AnchorBottom=1;
        _viewControls.OffsetLeft=-300;_viewControls.OffsetRight=-12;_viewControls.OffsetTop=-490;_viewControls.OffsetBottom=-196;
        var viewColumn=new VBoxContainer();_viewControls.AddChild(viewColumn);
        var viewHeading=new HBoxContainer();viewColumn.AddChild(viewHeading);
        var viewTitle=Text("视角与楼层",14,Accent);viewTitle.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;viewHeading.AddChild(viewTitle);
        UiAccess.Name(Button(viewHeading,"×",()=>_viewControls.Hide()),"关闭视图设置");
        var levelRow=new HBoxContainer();viewColumn.AddChild(levelRow);
        IconButton(levelRow,"down","层",()=>ChangeFloor(-1)).TooltipText="下降一层 [Command/Ctrl + -]";
        IconButton(levelRow,"up","层",()=>ChangeFloor(1)).TooltipText="上升一层 [Command/Ctrl + +]";
        IconButton(levelRow,"down","1m",()=>StepLayer(-1)).TooltipText="下降 1 米 [- / Q]";
        IconButton(levelRow,"up","1m",()=>StepLayer(1)).TooltipText="上升 1 米 [+ / E]";
        var orbit=new HBoxContainer();viewColumn.AddChild(orbit);
        IconButton(orbit,"rotate","左转",()=>OrbitCamera(-45)).TooltipText="向左旋转 [Z]";
        IconButton(orbit,"rotate","右转",()=>OrbitCamera(45)).TooltipText="向右旋转 [C]";
        _angleButton=IconButton(orbit,"layers","俯视",ToggleTopDown);_angleButton.TooltipText="切换俯视 [V]";
        var gridToggle=new CheckButton{Text="当前层网格",ButtonPressed=true};viewColumn.AddChild(gridToggle);
        UiAccess.Name(gridToggle,"显示当前层网格");
        gridToggle.Toggled+=enabled=>{_showLevelGuide=enabled;UpdateLevelGuide();};
        _viewControls.Hide();
        BuildCornerStatus(root,viewColumn);
        _mapPanel=Panel(root,Vector2.Zero,new Vector2(204,208));
        _mapPanel.AnchorLeft=_mapPanel.AnchorRight=_mapPanel.AnchorTop=_mapPanel.AnchorBottom=1;
        _mapPanel.OffsetLeft=-216;_mapPanel.OffsetRight=-12;_mapPanel.OffsetTop=-494;_mapPanel.OffsetBottom=-286;
        var mapColumn=new VBoxContainer();_mapPanel.AddChild(mapColumn);
        mapColumn.AddChild(Text("地表概览 · 点击定位",12,Muted));
        _overview=new WorldOverview{World=_world,CustomMinimumSize=new Vector2(180,174),MouseDefaultCursorShape=Control.CursorShape.PointingHand};
        _overview.Navigate=p=>{_focus=TerrainView.Point(p.X,_focus.Y,p.Y);PositionCamera();};
        mapColumn.AddChild(_overview);_overview.RefreshMap();_mapPanel.Hide();
        var notices=Panel(root,Vector2.Zero,new Vector2(250,0));
        notices.AddThemeStyleboxOverride("panel",HudStyle("151a20c9",8));
        notices.AnchorLeft=notices.AnchorRight=1;notices.OffsetLeft=-262;notices.OffsetRight=-12;notices.OffsetTop=18;
        var notes=new VBoxContainer();notices.AddChild(notes);notes.AddChild(Text("营地消息",12,Accent));
        _lifeNotices=new VBoxContainer();notes.AddChild(_lifeNotices);
        _events=Text("",13,Ink,true);_events.CustomMinimumSize=new Vector2(226,0);notes.AddChild(_events);
        _diagnosticsPanel=Panel(root,new Vector2(120,92),new Vector2(430,24));
        _diagnosticsPanel.MouseFilter=Control.MouseFilterEnum.Ignore;
        _stats=Text("",11,Muted);_stats.MouseFilter=Control.MouseFilterEnum.Ignore;_diagnosticsPanel.AddChild(_stats);_diagnosticsPanel.Hide();
        // Keep tool/drag status for interaction bookkeeping, without a HUD hint bar.
        _hint=Text("",12,Ink);_hint.MouseFilter=Control.MouseFilterEnum.Ignore;_hint.Hide();root.AddChild(_hint);
        _hint.AnchorTop=_hint.AnchorBottom=1;_hint.OffsetLeft=12;_hint.OffsetRight=700;_hint.OffsetTop=-100;_hint.OffsetBottom=-42;_hint.AutowrapMode=TextServer.AutowrapMode.WordSmart;_hint.AddThemeStyleboxOverride("normal",RStyle("181e26","66727a",8,5));
        BuildActionFeedback(root);
        _location=Text("",11,Muted);viewColumn.AddChild(_location);
        var credit=new LinkButton{Text="PureBDcraft 材质鸣谢",Uri="https://bdcraft.net",TooltipText="Textures/3D Models/Sounds from PureBDcraft ResourcePack by https://bdcraft.net"};
        UiAccess.Name(credit,"PureBDcraft 材质鸣谢","在浏览器中打开材质作者网站。");
        credit.AddThemeFontSizeOverride("font_size",11);worldColumn.AddChild(credit);
        var combatActions=new HBoxContainer();selectionColumn.AddChild(combatActions);
        BuildCombatInterface(root,tabs,combatActions);
        BuildCharacterEditor(root); BuildResidentPanel(root);
        _buildMenu.Hide();_selectionPanel.Hide();_inspector.Hide();_worldMenu.Hide();
    }

    private static void BottomLeft(Control control, float heightAboveBottom, float width)
    {
        control.AnchorTop = control.AnchorBottom = 1;
        control.OffsetLeft = 12; control.OffsetRight = 12 + width;
        control.OffsetTop = -heightAboveBottom; control.OffsetBottom = -96;
    }

    private static Label Text(string value, int size, Color color, bool wrap = false)
    {
        var label = new Label { Text = value, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color);
        if (wrap) label.CustomMinimumSize = new Vector2(210, 0);
        return label;
    }
    private static PanelContainer Panel(Control root, Vector2 position, Vector2 minimum)
    {
        var panel = new PanelContainer { Position = position, CustomMinimumSize = minimum };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("181e26f7"),
            BorderColor = new Color("5c6871"), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6,
            CornerRadiusTopLeft = 0, CornerRadiusTopRight = 0, CornerRadiusBottomLeft = 0, CornerRadiusBottomRight = 0
        });
        root.AddChild(panel); return panel;
    }
    private static Button Button(Node parent, string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(52, 30), MouseDefaultCursorShape = Control.CursorShape.PointingHand, FocusMode = Control.FocusModeEnum.All };
        button.AddThemeStyleboxOverride("normal", ButtonStyle("303942"));
        button.AddThemeStyleboxOverride("focus",UiAccess.FocusRing());
        if(text=="×")button.AccessibilityName="关闭面板";
        button.AddThemeStyleboxOverride("hover", ButtonStyle("455563"));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle("566c7b"));
        button.AddThemeColorOverride("font_color", Ink);
        button.Pressed += action; parent.AddChild(button); return button;
    }
    private static StyleBoxFlat ButtonStyle(string color) => new()
    {
        BgColor = new Color(color), ContentMarginLeft = 10, ContentMarginRight = 10,
        BorderColor=new Color("586570"),BorderWidthTop=1,BorderWidthBottom=1,BorderWidthLeft=1,BorderWidthRight=1,
        CornerRadiusTopLeft = 1, CornerRadiusTopRight = 1, CornerRadiusBottomLeft = 1, CornerRadiusBottomRight = 1
    };

    private void RotateStairs()
    {
        _facing = (_facing + 1) % 4; SelectTool(_tool);
    }
    private void SelectTool(string tool)
    {
        CancelLeftSelection();
        _digStart=null;_digEnd=null;_stockpileStart=null;_farmStart=null;
        if(tool is "dig" or "digstairs" or "canceldig")
        {

            _placement=0;_cutaway=true;
        }
        if (tool != "select") { _selectedResidents.Clear();_selectionPanel.Hide(); _selectedCell = null; _selectedWorker = -1; }
        if (tool != "foundation") _foundationStart = null;
        if(tool is "select" or "rally" or "erase" or "foundation" || tool is "Floodgate" or "Door" or "Furnace" or "Anvil" or "Stove" or "Bed" or "Campfire" or "Mill" or "Loom" or "Workbench" or "chop" or "planttree" || (Enum.TryParse<BuildKind>(tool,out var chosen)&&BlockCatalog.IsStairs(chosen))) _placement=0;
        if(_foundation is not null && Enum.TryParse<BuildKind>(tool,out _)) _cutaway=true;
        if(tool!="select"&&tool!="rally"){_interiorView=false;_floorWalls=false;}
        _revision=-1; _tool = tool; _hoverCell = null; Clear(_hover);
        foreach (var (id, button) in _toolButtons) button.AddThemeStyleboxOverride("normal", ButtonStyle(id == tool ? "566c7b" : "303942"));
        _hint.Text = Enum.TryParse<BuildKind>(tool, out var kind)
            ? $"{DemoWorld.NameOf(kind)} · {DemoWorld.CostOf(kind)} / 格    " + (BlockCatalog.IsStairs(kind) ? $"R 旋转 · 上行方向 {new[] { "东", "南", "西", "北" }[_facing]} · 连接高一层平台" : "左键规划，居民施工；右键取消工具。")
            : tool == "growingzone" ? "左键拖框或点击同高两角划种植区；完成后点击区域设置。右键取消。"
            : tool == "unfarm" ? "拖框或点击两角选择移除范围；确认后损失未收获作物。右键取消。"
            : tool is "stockpile" or "unstockpile" ? "左键拖框或点击同高两角设置储存范围；右键取消。"
            : tool == "chop" ? "左键拖框批量标记伐木，也可点击单棵；松开确认，右键或 Esc 取消。"
            : tool == "planttree" ? "点击露天土壤种树；消耗实体树苗，需要居民执行。"
            : tool == "canceldig" ? "取消挖掘：左键选起点，移动鼠标 / +− 选层，再次左键确认；仅撤销未完成挖掘，不填回已挖方块。右键或 Esc 放弃框选。"
            : tool == "cancelplan" ? "点击地格取消尚未完成的任务，材料保留；不会拆除已有建筑。"
            : tool == "dig" ? "左键选起点，移动鼠标并用 +/− 选层；再次左键确认长方体，右键或 Esc 取消。"
            : tool == "digstairs" ? $"挖楼梯坑 · 仅清空 3 米岩土，不生成楼梯 · 挖完后选择楼梯材质安装"
            : tool == "foundation" ? "左键拖框或点击地基两角；要求同高平地。顶面为第 0 层，右键取消。"
            : tool == "erase" ? "左键规划拆除 / 取消规划；居民按耐久拆除，完整退料。右键回到选择。"
            : tool == "blueprint" ? "移动鼠标预览，左键放置蓝图；右键退出。蓝图只保存在本次游玩。"
            : "左键拖框选人 · Shift 追加 · WASD 移动 · 中键平移 · 右键旋转 / 指令 · 滚轮缩放 · 空格暂停";
    }
    private void RefreshUi()
    {
        RefreshStoryInterface();
        RefreshResidentMindInterface();
        if (_selectedWorker >= _world.Workers.Count)_selectedWorker=-1;
        RefreshDeveloperUi();
        _hint.TooltipText=_hint.Text;
        foreach(var (mode,button) in _placementButtons) button.AddThemeStyleboxOverride("normal",ButtonStyle(mode==_placement?"566c7b":"303942"));
        _floorLabel.Text = _foundation is null ? "地基未设置 · 1 格 = 1 m" : $"第 {_foundation.Floor} 层 · 工作高度 {ActiveLevel} m · 落块 {BuildLevel} m · 层高 {_foundation.FloorHeight} m";
        _layerStatus.Text=_foundation is null?$"高度 {ActiveLevel} m · 未设地基":"F"+_foundation.Floor+$" · {ActiveLevel} m";
        _cutButton.Text=!_cutaway?"全貌":_interiorView?"切墙":MiningCutaway?"开采切墙":_floorWalls?"本层墙":"精确";
        RefreshCombatUi();
        _editCharacter.Visible = _selectedWorker >= 0;
        _residentDetails.Visible=_selectedWorker>=0;
        _selectionTabs.Visible=_selectedWorker>=0;
        _storageAction.Visible=_selectedWorker<0&&_selectedCell is Cell storageCell&&_world.Stockpiles.Contains(storageCell);
        _waterPointAction.Visible=_selectedWorker<0&&_selectedCell is Cell water&&(_world.Fluids.Amount(water)>=5||_world.WaterPoints.Contains(water));
        _waterPointAction.Text=_selectedCell is Cell wp&&_world.WaterPoints.Contains(wp)?"取消取水点":"设为取水点";
        _medicalBedAction.Visible=_selectedWorker<0&&_selectedCell is Cell bedCell&&_world.Buildings.TryGetValue(bedCell,out var selectedBed)&&selectedBed.Kind==BuildKind.Bed;
        _medicalBedAction.Text=_selectedCell is Cell mb&&_world.IsMedicalBed(mb)?"改为普通床":"设为医疗床";
        RefreshHudTabs();
        RefreshRecipeDetail();
        RefreshOrders();
        RefreshLifeNotices();
        _events.Text = string.Join("\n", _world.Events.Where(e=>!e.StartsWith("[开发]")&&!e.StartsWith("开发模式")&&!_feedbackEvents.Contains(e)).Take(2));
        if (_selectedWorker >= _world.Workers.Count)_selectedWorker=-1;
        if (_selectedWorker >= 0)
        {
            var worker = _world.Workers[_selectedWorker];
            _selection.Text = $"{worker.Name} · {worker.Person.Adulthood}\n{worker.Person.Age} 岁 · 心情 {worker.Person.Mood:F0}% · 技能点 {worker.Person.Points}\n{worker.Status}\nR 征召 / 解除；右键地面移动、敌人攻击";
            if(worker.Combat.Destination is Cell goal)_selection.Text+=$"\n目的地 {goal.X}, {goal.Z} / {goal.Level}m";
            if(worker.Combat.Target!="")_selection.Text+="\n追击："+(_world.Enemies.FirstOrDefault(e=>e.Id==worker.Combat.Target)?.Name??"目标已失效");
            if(worker.MedicalIssue!="")_selection.Text+="\n"+worker.MedicalIssue;
        }
        else if (_selectedCell is Cell selected)
        {
            var building = _world.Buildings.GetValueOrDefault(selected);
            var plan = _world.Jobs.FirstOrDefault(j => j.Construction && j.Cell == selected);
            string name = plan?.Build is BuildKind plannedKind ? DemoWorld.NameOf(plannedKind)+"（建设中）" : building is not null ? DemoWorld.NameOf(building.Kind) : _world.Fluids.Amount(selected)>0?"积水":plan is not null ? "挖掘规划" : _world.Items.Any(s=>s.Owner==""&&s.Cell==selected)?"物资堆":BlockCatalog.Get(_world.BlockAt(selected)).Name;
            _selection.Text = CurrentView.Known(_world,selected) ? name : "未探索区域";
            if(plan?.Build is not null)_selection.Text=$"{name}\n施工 {plan.BuildFraction:P0} · {ConstructionStatus(plan)}";
        }
        if(_selectedCell is Cell blockCell&&CurrentView.Known(_world,blockCell))
        {
            var plan=_world.Jobs.FirstOrDefault(j=>j.Cell==blockCell&&j.Build is not null);
            double max=plan?.Build is BuildKind kind?BlockCatalog.Get(kind).Durability:BlockCatalog.Get(_world.BlockAt(blockCell)).Durability;
            if(max>0)_selection.Text+=double.IsPositiveInfinity(max)?"\n不可破坏":$"\n耐久 {_world.RemainingDurability(blockCell):0.0} / {max:0}";
        }
        if(_selectedCell is Cell itemCell)
        {
            if(_world.WaterPoints.Contains(itemCell))_selection.Text+="\n饮用水源 · 需要空桶与安全岸边";
            var stacks=_world.Items.Where(s=>s.Owner==""&&(s.Cell==itemCell||s.Cell==itemCell+new Cell(0,0,1)));
            if(_world.Trees.TryGetValue(itemCell,out var tree))_selection.Text+=$"\n树木 {tree.Height} m · 成长 {tree.Age/DemoWorld.GrowTime(tree.Species):P0}";
            if(_world.StorageAt(itemCell) is {} zone)_selection.Text+=$"\n堆场 #{zone.Id} · 优先级 {zone.Priority}/5";
            foreach(var stack in stacks)_selection.Text+=$"\n{ItemCatalog.Get(stack.Item).Name} ×{stack.Count}"+(stack.Properties.ExpiresAt is double expiry?$" · {Math.Max(0,(expiry-_world.SimulationTime)/600):F1} 天后腐败":"");
        }
        RefreshFarms();
        RefreshConstructionSelection();RefreshFluidSelection();RefreshGroupSelection();RefreshDigRegionSelection();
        RefreshObjectCommands();
        for (int i = 0; i < _residentButtons.Count; i++)
        {
            _residentButtons[i].Visible=i<_world.Workers.Count;
            if(i>=_world.Workers.Count) continue;
            _residentButtons[i].TooltipText = _world.Workers[i].Status;
            _residentButtons[i].AddThemeStyleboxOverride("normal", ButtonStyle(IsResidentSelected(i) ? "566c7b" : "303942"));
        }
        _buildTab.AddThemeStyleboxOverride("normal", ButtonStyle(_buildMenu.Visible ? "566c7b" : "303942"));
        RefreshTimeControls();
        RefreshCalendar();
        _devBadge.Text=_developerMode?"开发模式":"";_devBadge.Visible=_developerMode;
        int[] stocks={_world.Total("cobblestone"),_world.Total("wood"),_world.Total("iron"),_world.Total("coal"),_world.Swords,_world.Total("raw_iron"),_world.Total("deep_rubble"),_world.Total("soil"),_world.Total("sand"),new[]{"raw_food","wheat","flour","potato","mushroom"}.Sum(_world.Total),_world.Total("meal"),_world.Total("cotton"),_world.Total("cloth")};
        for(int i=0;i<stocks.Length;i++)_resourceCounts[i].Text=stocks[i].ToString();
        _stats.Text = $"FPS {Engine.GetFramesPerSecond()}   |   TPS {_actualTps:F1}/{_clock.TargetTicksPerSecond}   |   本帧模拟 {_tickMs:F2} ms   |   积压 {_clock.PendingSeconds:F2}s   |   待办 {_world.Jobs.Count} | 水 {_world.Fluids.Cells.Count}格 / 队列 {_world.Fluids.Pending}{(_world.Fluids.AtLimit?" · 水格达到上限":"")}";
        _location.Text = _hoverCell is Cell c ? $"  {c.X}, {c.Z} / 层 {c.Level}" : "";
        _overview.CameraCell = new Vector2(_focus.X + (TerrainMap.Size-1)/2f, _focus.Z + (TerrainMap.Size-1)/2f); _overview.ViewSize = _camera.Size; _overview.QueueRedraw();
    }
    private void TogglePause() { _clock.Paused = !_clock.Paused; RefreshUi(); }
    private void ToggleWalls() => SetViewMode(!_cutaway?3:_floorWalls?1:_interiorView?2:0);
    private void Reset()
    {
        _seed.Apply(); _seed.GetLineEdit().ReleaseFocus();
        _pendingSeed = (int)_seed.Value;
        _pendingRegion=_nextRegion;
        if (_resetDialog.Visible) return;
        _pauseBeforeDialog = _clock.Paused; _clock.Paused = true;
        _resetDialog.DialogText = $"生成{(_pendingRegion is null?"旧版地形":RegionProfile.Get(_pendingRegion).Name)} · 种子 {_pendingSeed} 的新地图？\n当前营地和订单将清空。已保存的居民外观会保留，请先按 F5 保存营地进度。";
        _resetDialog.PopupCentered(new Vector2I(460, 180)); RefreshUi();
    }
    private void CancelReset() { _clock.Paused = _pauseBeforeDialog; RefreshUi(); }
    private void ConfirmReset()
    {
        _selectedWorker = -1; _selectedCell = null; _selectionPanel.Hide();
        _foundation = null; _heightSelected=false; _interiorView=false;_foundationStart = null; _layerOffset=0; _placement=0; Clear(_foundationOutline);
        _terrainRevision=-1; _world = new DemoWorld(_pendingSeed,region:_pendingRegion); LoadCharacters(); _clock = new SimulationClock(); _revision = -1;
        _terrainView.PriorityCell=new(_focus.X+(TerrainMap.Size-1)/2f,_focus.Z+(TerrainMap.Size-1)/2f);
        _terrainView.Generate(_world,CurrentView); _overview.World = _world; _overview.RefreshMap();
        CreatePeople(); FocusCamp(); SelectTool("select"); ApplyDefaultEditor();
    }
}

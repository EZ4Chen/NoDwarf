using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private void RunDevelopmentChecks()
    {
        if(OS.GetCmdlineUserArgs().Any(a=>a is "--smoke-test" or "--interaction-test" or "--appearance-preview-test" or "--resident-preview-test"))
        {
            if(!_developerMode||!_clock.Paused||_world.Workers.Count!=0)throw new InvalidOperationException("Default editor must be paused and resident-free");
            _world.DeveloperResidents(true);_developerMode=false;_clock.Paused=false;CreatePeople();
        }
        if(OS.GetCmdlineUserArgs().Contains("--hud-review"))
        {_world.DeveloperResidents(true);_developerMode=false;_creative=false;_clock.Paused=true;CreatePeople();FocusCamp();RefreshUi();}
        if (OS.GetCmdlineUserArgs().Contains("--dialogue-test")) ReviewDialogues();
        if (OS.GetCmdlineUserArgs().Contains("--appearance-preview-test")) CaptureCharacterPreview();
        if (OS.GetCmdlineUserArgs().Contains("--startup-preview-test")) CheckStartupArrival();
        if (OS.GetCmdlineUserArgs().Contains("--geography-preview-test")) CheckGeographyPreview();
        if (OS.GetCmdlineUserArgs().Contains("--map-display-test")) CheckMapDisplay();
        if (OS.GetCmdlineUserArgs().Contains("--water-safety-ui-test")) CheckWaterSafetyUi();
        if (OS.GetCmdlineUserArgs().Contains("--safety-areas-test")) ReviewSafetyAreas();
        if (OS.GetCmdlineUserArgs().Contains("--supplies-ui-test")) ReviewSupplies();
        if (OS.GetCmdlineUserArgs().Contains("--mint-menu-test")) CheckMintMenus();
        if (OS.GetCmdlineUserArgs().Contains("--ui-cleanup-test")) CheckUiCleanup();
        if (OS.GetCmdlineUserArgs().Contains("--life-preview-test")) CaptureLifePreview();
        if (OS.GetCmdlineUserArgs().Contains("--resident-preview-test")) CaptureResidentPreview();
        if (OS.GetCmdlineUserArgs().Contains("--resident-test")) { try { ResidentChecks.Run(); GetTree().Quit(); } catch(Exception e) { GD.PushError(e.ToString());GetTree().Quit(1); } }
        if (OS.GetCmdlineUserArgs().Contains("--region-preview-test")) CaptureRegionPreview();
        if (OS.GetCmdlineUserArgs().Contains("--voxel-tree-preview-test")) CaptureVoxelTreePreview();
        if (OS.GetCmdlineUserArgs().Contains("--placement-test")) ReviewBlockPlacement();
        if (OS.GetCmdlineUserArgs().Contains("--spatial-content-test")) ReviewSpatialContent();
        if (OS.GetCmdlineUserArgs().Contains("--block-face-test")) ReviewBlockFaces();
        if (OS.GetCmdlineUserArgs().Contains("--gravity-test")) ReviewGravity();
        if (OS.GetCmdlineUserArgs().Contains("--climbing-test")) ReviewClimbing();
        if (OS.GetCmdlineUserArgs().Contains("--equipment-test")) ReviewEquipment();
        if (OS.GetCmdlineUserArgs().Contains("--session-async-test")) ReviewSession();
        if (OS.GetCmdlineUserArgs().Contains("--dig-volume-test")) ReviewDigVolume();
        if (OS.GetCmdlineUserArgs().Contains("--mining-cutaway-test")) ReviewMiningCutaway();
        if (OS.GetCmdlineUserArgs().Contains("--designation-test")) ReviewDesignations();
        if (OS.GetCmdlineUserArgs().Contains("--level-guide-test")) ReviewLevelGuide();
        if (OS.GetCmdlineUserArgs().Contains("--corner-hud-test")) ReviewCornerHud();
        if (OS.GetCmdlineUserArgs().Contains("--camera-cutaway-test")) ReviewCameraCutaway();
        if (OS.GetCmdlineUserArgs().Contains("--object-commands-test")) ReviewObjectCommands();
        if (OS.GetCmdlineUserArgs().Contains("--melee-test")) ReviewMelee();
        if (OS.GetCmdlineUserArgs().Contains("--sprite-integration-test")) ReviewSpriteIntegration();
        if (OS.GetCmdlineUserArgs().Contains("--cache-performance-test")) ReviewCachePerformance();
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
        if (OS.GetCmdlineUserArgs().Contains("--creature-framework-test")) { try { CreatureChecks.Run(); GetTree().Quit(); } catch(Exception e) { GD.PushError(e.ToString());GetTree().Quit(1); } }
        if (OS.GetCmdlineUserArgs().Contains("--production-bill-test")) { try { ProductionBillChecks.Run(); CropProductionChecks.Run(); LogisticsChecks.Run(); GetTree().Quit(); } catch(Exception e) { GD.PushError(e.ToString());GetTree().Quit(1); } }
        if (OS.GetCmdlineUserArgs().Contains("--production-target-test")) { try { ProductionOrderChecks.Run(); GetTree().Quit(); } catch(Exception e) { GD.PushError(e.ToString());GetTree().Quit(1); } }
        if (OS.GetCmdlineUserArgs().Contains("--fluid-test")) { try { FluidChecks.Run(); GetTree().Quit(); } catch(Exception e) { GD.PushError(e.ToString());GetTree().Quit(1); } }
        if (OS.GetCmdlineUserArgs().Contains("--weather-test")) { try { WeatherChecks.Run(); CropClimateChecks.Run(); GetTree().Quit(); } catch(Exception e) { GD.PushError(e.ToString());GetTree().Quit(1); } }
        if (OS.GetCmdlineUserArgs().Contains("--weather-preview-test")) CaptureWeatherPreview();
        if (OS.GetCmdlineUserArgs().Contains("--calendar-preview-test")) CaptureCalendarPreview();
        if (OS.GetCmdlineUserArgs().Contains("--crop-production-preview-test")) CaptureCropProductionPreview();
        if (OS.GetCmdlineUserArgs().Contains("--farm-preview-test")) CaptureFarmPreview();
        if (OS.GetCmdlineUserArgs().Contains("--colony-preview-test")) CaptureColonyPreview();
        if (OS.GetCmdlineUserArgs().Contains("--adjacency-test")) ReviewAdjacency();
        if (OS.GetCmdlineUserArgs().Contains("--construction-line-test")) ReviewConstructionLine();
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

}

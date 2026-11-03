using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewBlockPlacement()
    {
        try
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("PLACEMENT_UI: "+why);}
            var content=_world.Content;Check(content.Item("copper:ingot").Placements.Count==2,"enable sample Mod for UI test");
            _world=new DemoWorld(flat:true,content:content);_world.DeveloperResidents(false);_world.Jobs.Clear();_world.Items.Clear();_world.Stockpiles.Clear();
            _clock.Paused=true;_developerMode=false;_creative=false;_foundation=null;_cutaway=false;_heightSelected=false;_interiorView=false;
            CreatePeople();_overview.World=_world;
            var sand=_world.SpawnItem("sand",5,new(20,20));var copper=_world.SpawnItem("copper:ingot",8,new(20,20));
            _focus=WorldPosition(sand.Cell);_camera.Size=14;PositionCamera();SyncWorld();RefreshUi();
            _placeBlockMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(_blockPicker!.Visible&&_blockChoiceButtons.ContainsKey(("sand","nodwarf:sand"))&&_blockChoiceButtons.ContainsKey(("copper:ingot","copper:solid_block")),"shared catalog menu");
            _blockPicker.Hide();Check(_tool=="select","closing without a choice restores selection");_placeBlockMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            _blockSearch!.Text="铜";_blockSearch.EmitSignal(LineEdit.SignalName.TextChanged,"铜");Check(_blockChoiceButtons.Count==2,"search new Mod blocks");
            _blockOrigin!.Select(1);RefreshBlockChoices();Check(_blockChoiceButtons.Count==0,"origin filter");
            _blockOrigin.Select(0);_blockSearch.Text="沙";RefreshBlockChoices();
            var sandButton=_blockChoiceButtons[("sand","nodwarf:sand")];Check(!sandButton.Disabled&&sandButton.AccessibilityName.Length>0,"accessible selectable entry");
            sandButton.EmitSignal(BaseButton.SignalName.Pressed);Check(_tool=="placeblock"&&!_blockPicker.Visible,"choose enters placement");
            var first=new Cell(22,20);Act(first);Check(_world.Jobs.Single().Placing&&_world.Jobs.Single().Build is null,"queued terrain task rather than building");
            Check(_constructionVisuals.Count==1,"construction preview supports placement");
            _world.DeveloperFinishPlans();Check(_world.BlockAt(first)=="nodwarf:sand"&&!_world.Buildings.ContainsKey(first),"finish actual terrain");
            var upper=first+new Cell(0,0,1);Act(upper);_world.DeveloperFinishPlans();
            var mesh=TerrainMeshBuilder.Build(new(_world.Terrain.CaptureMeshSnapshot(),new WorldView(null),new()),(16,16));
            Check(mesh.Surfaces.Any(p=>p.Key.Id=="nodwarf:sand"&&p.Value.Vertices.Any(v=>v.Y>1.5f)),"mesh renders above original surface");
            _overview.RefreshMap();_overview.AdvanceMapRefresh(5);Check(_overview.MapPixel(first.X,first.Z)==new Color(content.Block("nodwarf:sand").Color),"minimap reflects placed material");
            SelectTool("select");SyncWorld();
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            var screen=_camera.UnprojectPosition(_itemNodes[sand.Id].Root.GlobalPosition);
            Check(SelectDroppedItem(screen)&&_selectedItemId==sand.Id,"ray selects individual dropped stack");
            Check(_groundItemIds.Count==2&&_placeItemButton.Visible,"multiple stacks and contextual button");
            _placeItemButton.EmitSignal(BaseButton.SignalName.Pressed);Check(_activePlacement?.Item=="sand"&&_placementSource==sand.Id,"one result enters directly");
            Act(new(22,21));Check(_world.Jobs.Single().Materials.Single().StackId==sand.Id,"clicked source reserved");_world.CancelJob(_world.Jobs.Single());
            SelectTool("select");Act(copper.Cell);_selectedItemId=copper.Id;RefreshUi();_placeItemButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(_blockPicker!.Visible&&_blockChoiceButtons.Count==2,"multiple results require choice");
            _blockChoiceButtons[("copper:ingot","copper:compact_block")].EmitSignal(BaseButton.SignalName.Pressed);Act(new(23,20));
            Check(_world.Jobs.Single().PlaceBlock=="copper:compact_block"&&_world.Jobs.Single().Materials.Sum(r=>r.Count)==4,"Mod selection cost");
            _world=DemoWorld.LoadJson(_world.SaveJson(),content:content);_world.DeveloperFinishPlans();Check(_world.BlockAt(new(23,20))=="copper:compact_block","saved Mod placement");
            BeginBlockPlacement(content.Placement("sand","nodwarf:sand"),0);CommitPlacementArea(new(25,20),new(26,20));Check(_world.Jobs.Count==2,"shared drag planning");
            _world.CancelJob(_world.Jobs[0]);_world.CancelJob(_world.Jobs[0]);
            _world.SpawnItem("coal",1,new(30,20));SelectTool("select");Act(new(30,20));Check(!_placeItemButton.Visible,"non-placeable item has no place button");
            GD.Print("PLACEMENT_UI_OK: catalog search/filter, accessible controls, actual item picking, multi-result Mod placement, plans, upper terrain mesh, minimap and save");GetTree().Quit();
        }
        catch(Exception ex){GD.PushError(ex.ToString());GetTree().Quit(1);}
    }
}

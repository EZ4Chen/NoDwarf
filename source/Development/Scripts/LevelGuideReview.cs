using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewLevelGuide()
    {
        try
        {
            void Check(bool ok,string why){if(!ok)throw new Exception("LEVEL_GUIDE: "+why);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;_clock.Paused=true;
            _developerMode=false;_creative=false;_foundation=null;_heightSelected=true;_layerOffset=-1;
            _cutaway=true;_interiorView=false;_floorWalls=false;_camera.Size=18;
            _focus=WorldPosition(new(127,127,-1));PositionCamera();CreatePeople();SelectTool("select");
            _buildMenu.Hide();_inspector.Hide();_worldMenu.Hide();SyncWorld();_terrainView.FlushMeshes();await Frame();
            Act(new(126,127,-1));RefreshUi();UpdateLevelGuide();await Frame();
            Check(_selectionTitle.Visible&&_selectionTitle.Text.Length>0&&_selection.Text.Split('\n').Length==1&&_selection.Text.Contains("耐久")&&!_selection.Text.Contains("nodwarf:"),"block information must contain only name and durability");
            Check(_levelGuide is {Visible:true}&&_levelGuideLabel!.Text=="当前层 -1 m","layer guide missing");
            var mesh=_levelGuide!.Mesh;var center=GetViewport().GetVisibleRect().GetCenter();
            var hit=PickTarget(center);_showLevelGuide=false;UpdateLevelGuide();
            Check(!_levelGuide.Visible&&PickTarget(center)==hit,"guide changed picking");
            _showLevelGuide=true;UpdateLevelGuide();
            if(DisplayServer.GetName()!="headless")
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/current-layer-guide.png"));
            }
            float y=_levelGuide.Position.Y;StepLayer(1);UpdateLevelGuide();
            Check(Math.Abs(_levelGuide.Position.Y-y-1)<.001&&_levelGuideLabel!.Text=="当前层 0 m","guide did not follow floor");
            _cameraYaw+=1;_focus.X+=4;PositionCamera();UpdateLevelGuide();Check(_levelGuide.Mesh==mesh,"guide rebuilt mesh while moving");
            _camera.Size=90;UpdateLevelGuide();Check(!_levelGuide.Visible,"distant clutter");
            _camera.Size=18;SetViewMode(0);UpdateLevelGuide();Check(!_levelGuide.Visible,"full view should hide layer guide");
            GD.Print("LEVEL_GUIDE_OK: concise block text, layer changes, shared mesh, toggle, distant/full view hiding and unchanged picking");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

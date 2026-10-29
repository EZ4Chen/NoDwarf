using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewCornerHud()
    {
        try
        {
            void Check(bool ok,string why){if(!ok)throw new Exception("CORNER_HUD: "+why);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            async Task Click(Button button)
            {
                var p=button.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion{Position=p,GlobalPosition=p},true);
                foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventMouseButton{Position=p,GlobalPosition=p,ButtonIndex=MouseButton.Left,Pressed=down},true);
                await Frame();
            }
            var clock=new SimulationClock();clock.SetSpeed(2);Check(clock.Advance(1)==60,"2x tick rate");
            var restored=new SimulationClock();restored.Restore(clock.Tick,clock.Speed,true,clock.PendingSeconds);
            Check(restored.Speed==2&&restored.Advance(1)==0,"2x clock restore/pause");
            _clock.Paused=true;_world=new DemoWorld(region:"temperate");_world.Update(100);_overview.World=_world;
            _developerMode=false;_creative=false;_foundation=null;_heightSelected=false;_cutaway=false;
            _focus=WorldPosition(_world.Camp);_camera.Size=28;PositionCamera();CreatePeople();SelectTool("select");
            _selectionPanel.Hide();_buildMenu.Hide();_inspector.Hide();_worldMenu.Hide();SyncWorld();_terrainView.FlushMeshes();RefreshUi();await Frame();
            foreach(string weather in new[]{"clear","rain"})
            {
                _world.SetWeather(weather);RefreshUi();await Frame();
                Check(_weatherStatus.Text==_world.Weather.Name&&_seasonStatus.Text==_world.Calendar.Season,"weather/season mismatch");
                Check(_timeStatus.Text==$"{_world.Calendar.Hour}时"&&_temperatureStatus.Text.StartsWith("室外 "),"time/temperature mismatch");
                var labels=new[]{_temperatureStatus,_weatherStatus,_timeStatus,_calendarLabel,_seasonStatus};
                for(int i=0;i<labels.Length;i++)
                {
                    Check(_cornerStatus.GetGlobalRect().Encloses(labels[i].GetGlobalRect()),"label overflow");
                    if(i>0)Check(labels[i].GetGlobalRect().Position.Y>labels[i-1].GetGlobalRect().Position.Y,"wrong reading order");
                }
                if(DisplayServer.GetName()!="headless")
                {
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/corner-hud-{weather}.png"));
                }
            }
            foreach(var (button,speed) in new[]{(_normalSpeed,1),(_doubleSpeed,2),(_fastSpeed,3)})
            {
                await Click(button);Check(!_clock.Paused&&_clock.Speed==speed,"speed click failed");
                Check(button.GetThemeStylebox("normal") is StyleBoxEmpty,"speed button retained solid background");
                await Click(_pause);Check(_clock.Paused,"pause click failed");
            }
            Check(_resourceCounts[0].GetParent().GetParent().GetParent<PanelContainer>().GetThemeStylebox("panel") is StyleBoxEmpty,"resource background retained");
            Check(!_layerStatus.IsVisibleInTree()&&!_location.IsVisibleInTree(),"view metadata leaked into clock HUD");
            GetWindow().ContentScaleSize=new(960,640);await Frame();await Frame();
            Check(GetViewport().GetVisibleRect().Encloses(_cornerStatus.GetGlobalRect()),"narrow HUD overflow");
            GD.Print("CORNER_HUD_OK: transparent corners, ordered live climate/date labels, real pause/1x/2x/3x clicks, 2x restore and narrow bounds");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureLifePreview()
    {
        try
        {
            _clock.Paused=true;_world=CombatChecks.Fixture();_overview.World=_world;_terrainRevision=-1;
            var origin=new Cell(60,60);
            foreach(var (offset,kind) in new[]{(new Cell(4,0),BuildKind.Table),(new Cell(3,0),BuildKind.Chair),(new Cell(5,0),BuildKind.Chair),(new Cell(5,3),BuildKind.GameTable),(new Cell(4,5),BuildKind.Bed)})
                _world.Buildings[origin+offset]=new(origin+offset,kind);
            var bed=origin+new Cell(4,5);_world.SetMedicalBed(bed,true);var patient=_world.Workers[0];patient.X=bed.X-1;patient.Z=bed.Z;patient.Person.MedicalBed=bed;_world.InjureResident(patient.Id,"torso","fracture",90);
            _world.Workers[1].X=61;_world.Workers[1].Z=61;
            _world.Workers[2].X=61;_world.Workers[2].Z=61;_world.InjureResident("civilian","torso","fracture",90);
            var secondBed=origin+new Cell(6,5);_world.Buildings[secondBed]=new(secondBed,BuildKind.Bed);_world.SetMedicalBed(secondBed,true);
            _world.Workers[1].Person.Activity=new(){Kind="rescue_carry",Target="civilian",Destination=origin+new Cell(5,5),RescueBed=secondBed};
            CreatePeople();_foundation=new FoundationPlan(origin,origin+new Cell(8,8));_heightSelected=true;_layerOffset=0;
            _focus=WorldPosition(origin+new Cell(3,3));_camera.Size=12;_cameraYaw=0;_topDown=false;PositionCamera();SetViewMode(1);SyncWorld();_world.RefreshLifeAlerts();RefreshUi();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(_lifeNotices.GetChildCount()==0)throw new Exception("Life alerts missing");
            foreach(var c in new[]{origin+new Cell(4,0),origin+new Cell(3,0),origin+new Cell(5,3)})if(!_buildingVisuals.ContainsKey(c))throw new Exception("Furniture missing");
            GetViewport().GetTexture().GetImage().SavePng("res://.local/life-facilities.png");
            SelectResident(0);OpenPersonPage(2);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var careMenu=_residentBody.FindChildren("*","MenuButton",true,false).OfType<MenuButton>().First();
            if(careMenu.GetPopup().ItemCount!=4||careMenu.AccessibilityName=="")throw new Exception("Care menu inaccessible");
            careMenu.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed,3L);
            if(!patient.Person.Isolated)throw new Exception("Isolation action failed");
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng("res://.local/life-health.png");
            GD.Print("LIFE_UI_OK: furniture, alerts, care menu and isolation; carrying and bed pose preview captured");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

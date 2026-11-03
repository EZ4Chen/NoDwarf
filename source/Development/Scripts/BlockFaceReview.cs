using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewBlockFaces()
    {
        try
        {
            BlockExposureChecks.Run();
            _clock.Paused=true;_world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;
            _foundation=null;_heightSelected=false;_interiorView=false;_floorWalls=false;_cutaway=false;_wallCaps=null;
            // Use a supported 3x3x3 block of masonry. The old cross-shaped
            // fixture floated 2 m above the floor and produced misleading shadows.
            var c=new Cell(127,127,1);var remote=new Cell(135,127,0);
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)for(int y=-1;y<=1;y++)
            {var cell=c+new Cell(x,z,y);_world.Buildings[cell]=new(cell,BuildKind.Wall);}
            _world.Buildings[remote]=new(remote,BuildKind.Log);
            if(_world.Surface(remote.X,remote.Z).Level!=remote.Level||_world.Surface(c.X,c.Z).Level!=c.Level-1)
                throw new InvalidOperationException("Shadow fixture must touch the terrain");
            CreatePeople();_focus=WorldPosition(c);_camera.Size=16;PositionCamera();SyncWorld();_terrainView.FlushMeshes();
            int Vertices(Cell cell)=>_buildingVisuals[cell].Root.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().Sum(m=>m.Mesh.GetFaces().Length);
            if(Vertices(c)!=0||Vertices(remote)!=30)throw new InvalidOperationException("Enclosed/grounded isolated geometry");
            ulong retained=_buildingVisuals[remote].Root.GetInstanceId();
            _world.DeveloperErase(c+new Cell(1,0));SyncWorld();
            if(Vertices(c)!=6)throw new InvalidOperationException("Removing neighbor did not restore a quad");
            if(_buildingVisuals[remote].Root.GetInstanceId()!=retained)throw new InvalidOperationException("Remote building was rebuilt");
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var ray=PhysicsRayQueryParameters3D.Create(WorldPosition(c)+new Vector3(2,.5f,0),WorldPosition(c)+new Vector3(0,.5f,0),2);
            var hit=GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if(hit.Count==0||hit["collider"].AsGodotObject() is not Node body||body.GetMeta("cell").AsVector3I()!=new Vector3I(c.X,c.Level,c.Z))throw new InvalidOperationException("Restored single-face block cannot be picked");
            _foundation=new FoundationPlan(c,c);_placement=0;_layerOffset=0;_cutaway=true;SyncWorld();_terrainView.FlushMeshes();RefreshUi();
            if(Vertices(c)!=12)throw new InvalidOperationException("Cut must restore top and retain exposed side");
            if(_buildingVisuals.Keys.Any(cell=>cell.Level>c.Level))throw new InvalidOperationException("Hidden upper floor retained a visual/shadow caster");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/block-faces-cut.png"));
            _foundation=null;_cutaway=false;SyncWorld();
            if(Vertices(c)!=6)throw new InvalidOperationException("Restoring upper neighbor did not remove cap");
            GD.Print("BLOCK_FACE_UI_OK: enclosed geometry omitted, demolition exposes one quad, remote nodes retained, full picking bounds, cut cap and restoration");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

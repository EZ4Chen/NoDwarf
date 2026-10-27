using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private bool _orbitHeld,_orbitMoved;
    private Vector2 _orbitOrigin,_orbitLast;
    private Cell? _digStart,_digEnd;
    private Cell? DigPick(Vector2 screen)
    {
        var hit=PickObject(screen,1);
        if(hit.Count==0)return null;
        var collider=hit["collider"].AsGodotObject();
        if(collider.HasMeta("tree_root")){var root=collider.GetMeta("cell").AsVector3I();return new Cell(root.X,root.Z,root.Y);}
        var p=(Vector3)hit["position"]-(Vector3)hit["normal"]*.01f;
        var cell=new Cell((int)Math.Floor(p.X+DemoWorld.Width/2f),(int)Math.Floor(p.Z+DemoWorld.Depth/2f),(int)Math.Floor(p.Y));
        return DemoWorld.Inside(cell)?cell:null;
    }
    private void RightClick(Vector2 position)
    {
                if (_tool == "select" && _selectedWorker >= 0 && Pick(position) is Cell destination)
                {
                    var worker = _world.Workers[_selectedWorker];
                    var path = _world.FindPath(worker.Tile, destination);
                    if (worker.Person.Incapacitated||worker.Person.Mobility<.05) _world.Log("居民无法行动，需要先照料。");
                    else if (worker.Job is not null || worker.Person.Activity is not null) _world.Log("居民正在工作或进行个人活动，请先在人物档案中安排。");
                    else if (path is null) _world.Log("无法到达：需要连通的平地或坡道。");
                    else { worker.Path.Clear(); foreach (var step in path) worker.Path.Enqueue(step); _world.Log($"{worker.Name}前往 {destination.X}, {destination.Z} / 第 {destination.Level} 层。"); }
                }
                else SelectTool("select");

    }
    private bool HandleMapGesture(InputEvent input)
    {
        if(!GetWindow().HasFocus() && !OS.GetCmdlineUserArgs().Contains("--interaction-test"))
        { _orbitHeld=false;_digStart=null;_digEnd=null;return false; }
        if((_consolePanel is not null&&_consolePanel.Visible)||(_developerPanel is not null&&_developerPanel.Visible)||(_characterEditor is not null&&_characterEditor.Visible))
        { _orbitHeld=false;_digStart=null;_digEnd=null;return false; }
        if(_orbitHeld)
        {
            if(input is InputEventMouseMotion motion)
            {
                _orbitMoved|=motion.Position.DistanceTo(_orbitOrigin)>5;
                if(_orbitMoved)
                {
                    var delta=motion.Position-_orbitLast;
                    _cameraYaw=Mathf.PosMod(_cameraYaw-delta.X*.006f,Mathf.Tau);
                    if(delta.Y!=0)
                    {
                        _orbitPitch=Mathf.Clamp(_cameraPitch+delta.Y*.006f,MinCameraPitch,MaxCameraPitch);
                        _topDown=false;_angleButton.Text="俯视";
                        if(_interiorView)_revision=-1;
                    }
                    _digStart=null;_digEnd=null;PositionCamera();_hoverCell=null;Clear(_hover);
                }
                _orbitLast=motion.Position;GetViewport().SetInputAsHandled();return true;
            }
            if(input is InputEventMouseButton{ButtonIndex:MouseButton.Right,Pressed:false} release)
            {
                bool moved=_orbitMoved;_orbitHeld=false;
                if(!moved && GetViewport().GuiGetHoveredControl() is null)RightClick(release.Position);
                GetViewport().SetInputAsHandled();return true;
            }
        }
        if(_digStart is Cell start)
        {
            if(input is InputEventMouseMotion motion)
            {
                if(PlanePick(motion.Position,start.Level+1) is Cell end)_digEnd=end with{Level=start.Level};
                _hoverCell=null;GetViewport().SetInputAsHandled();return true;
            }
            if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false} release)
            {
                var end=_digEnd??start;_digStart=null;_digEnd=null;
                if(GetViewport().GuiGetHoveredControl() is null)CommitDigDrag(start,end);
                Clear(_hover);_hoverCell=null;GetViewport().SetInputAsHandled();return true;
            }
            if(input is InputEventKey{Pressed:true,Keycode:Key.Escape} || input is InputEventMouseButton{ButtonIndex:MouseButton.Right,Pressed:true})
            { _digStart=null;_digEnd=null;_hoverCell=null;Clear(_hover);GetViewport().SetInputAsHandled();return true; }
        }
        return false;
    }
    private void CommitDigDrag(Cell start,Cell end)
    {
        int count=0,skipped=0;bool stairs=_tool=="digstairs";
        for(int x=Math.Min(start.X,end.X);x<=Math.Max(start.X,end.X);x++)
        for(int z=Math.Min(start.Z,end.Z);z<=Math.Max(start.Z,end.Z);z++)
        {
            var c=new Cell(x,z,start.Level);
            if(!_world.CanDig(c,stairs)){skipped++;continue;}
            bool ok=_developerMode&&_creative?_world.DeveloperDig(c,stairs,_facing):_world.QueueDig(c,stairs,_facing,false);
            if(ok)count++;else skipped++;
        }
        _world.Log($"挖掘：{count} 格{(_developerMode&&_creative?"已挖除":"已规划")}，跳过 {skipped} 格；目标高度 {start.Level} m，深度 {(stairs?3:1)} 格。");
        SyncWorld();RefreshUi();
    }
    private void DrawDigDrag(Cell start,Cell end)
    {
        if(_hoverCell==end)return;_hoverCell=end;
        Clear(_hover);
        var center=WorldPosition(new Cell(0,0,start.Level))+new Vector3((start.X+end.X)*.5f,1.025f,(start.Z+end.Z)*.5f);
        float w=Math.Abs(start.X-end.X)+1,d=Math.Abs(start.Z-end.Z)+1;
        var mat=Mat("edc47b");
        foreach(float sign in new[]{-1f,1f})
        {
            Box(_hover,center+new Vector3(sign*w/2,0,0),new Vector3(.035f,.035f,d),mat);
            Box(_hover,center+new Vector3(0,0,sign*d/2),new Vector3(w,.035f,.035f),mat);
        }
        _hint.Text=$"选区 {(int)w} × {(int)d} · 高度 {start.Level} m · 松开左键确认 / 右键或 Esc 取消";
    }
}

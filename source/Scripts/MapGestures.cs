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
        if(_tool=="select"&&SelectedResidents().Length>1){OrderSelectedResidents(position);return;}
        if(_tool=="select"&&_selectedWorker>=0&&_world.Workers[_selectedWorker].Combat.Drafted)
        {
            var w=_world.Workers[_selectedWorker];var hit=PickObject(position,16);
            if(hit.Count>0&&hit["collider"].AsGodotObject() is {} body&&body.HasMeta("enemy"))CombatNotice(_world.MilitaryAttack(w.Id,body.GetMeta("enemy").AsString()));
            else if(Pick(position) is Cell c)CombatNotice(_world.MilitaryMove(w.Id,c));
            return;
        }
        if(_world.CivilianAlert&&_selectedWorker>=0){_world.Log("集合警报期间请先征召居民，或解除警报。");return;}

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
        if(!GetWindow().HasFocus() && !GestureTest)
        { CancelLeftSelection();_orbitHeld=false;_digStart=null;_digEnd=null;return false; }
        if((_consolePanel is not null&&_consolePanel.Visible)||(_characterEditor is not null&&_characterEditor.Visible))
        { CancelLeftSelection();_orbitHeld=false;_digStart=null;_digEnd=null;return false; }
        if(HandleLeftSelection(input))return true;
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
                        if(LocalWallCutaway)_revision=-1;
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
                if(PlanePick(motion.Position,(_digEnd??start).Level+1) is Cell end)_digEnd=end with{Level=(_digEnd??start).Level};
                _hoverCell=null;GetViewport().SetInputAsHandled();return true;
            }
            if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:true} release)
            {
                if(GetViewport().GuiGetHoveredControl() is not null)return false;
                var end=_digEnd??start;_digStart=null;_digEnd=null;
                CommitDigDrag(start,end);
                Clear(_hover);_hoverCell=null;GetViewport().SetInputAsHandled();return true;
            }
            if(input is InputEventKey{Pressed:true,Keycode:Key.Escape} || input is InputEventMouseButton{ButtonIndex:MouseButton.Right,Pressed:true})
            { _digStart=null;_digEnd=null;_hoverCell=null;Clear(_hover);GetViewport().SetInputAsHandled();return true; }
        }
        return false;
    }
    private void CommitDigDrag(Cell start,Cell end)
    {
        if(_tool=="canceldig"){CommitCancelDig(start,end);return;}
        string region=Guid.NewGuid().ToString("N");
        var regionMin=new Cell(Math.Min(start.X,end.X),Math.Min(start.Z,end.Z),Math.Min(start.Level,end.Level));
        var regionMax=new Cell(Math.Max(start.X,end.X),Math.Max(start.Z,end.Z),Math.Max(start.Level,end.Level)+(_tool=="digstairs"?2:0));
        int count=0,skipped=0;bool stairs=_tool=="digstairs";
        long volume=(long)(Math.Abs(start.X-end.X)+1)*(Math.Abs(start.Z-end.Z)+1)*(Math.Abs(start.Level-end.Level)+1);
        if(volume>4096){_world.Log("挖掘选区超过 4096 格，请分批规划。");RefreshUi();return;}
        for(int x=Math.Min(start.X,end.X);x<=Math.Max(start.X,end.X);x++)
        for(int z=Math.Min(start.Z,end.Z);z<=Math.Max(start.Z,end.Z);z++)
        for(int y=Math.Max(start.Level,end.Level);y>=Math.Min(start.Level,end.Level);y-=stairs?3:1)
        {
            var c=new Cell(x,z,y);
            if(!_world.CanDig(c,stairs)){skipped++;continue;}
            bool ok=_developerMode&&_creative?_world.DeveloperDig(c,stairs,_facing):_world.QueueDig(c,stairs,_facing,false);
            if(ok)
            {
                count++;
                if(!(_developerMode&&_creative)&&_world.Jobs.LastOrDefault() is {Dig:true,Demolish:false} job&&job.Cell==c)
                {job.DigRegion=region;job.DigRegionMin=regionMin;job.DigRegionMax=regionMax;}
            }
            else skipped++;
        }
        _world.Log($"挖掘：{count} 格{(_developerMode&&_creative?"已挖除":"已规划")}，跳过 {skipped} 格；高度 {Math.Min(start.Level,end.Level)} 至 {Math.Max(start.Level,end.Level)+(stairs?2:0)} m。");
        SyncWorld();RefreshUi();
    }
    private void CommitCancelDig(Cell start,Cell end)
    {
        // Intersect task volume too: legacy three-metre excavations can start below the selection.
        var jobs=_world.Jobs.Where(j=>j.Dig&&!j.Demolish&&j.Cell.X>=Math.Min(start.X,end.X)&&j.Cell.X<=Math.Max(start.X,end.X)
            &&j.Cell.Z>=Math.Min(start.Z,end.Z)&&j.Cell.Z<=Math.Max(start.Z,end.Z)
            &&j.Cell.Level<=Math.Max(start.Level,end.Level)&&j.Cell.Level+j.DigHeight-1>=Math.Min(start.Level,end.Level)).ToArray();
        foreach(var job in jobs)_world.CancelJob(job,false);
        _world.Log($"已取消 {jobs.Length} 个挖掘任务；已有建筑、其他规划与已挖地形保持不变。");
        SyncWorld();RefreshUi();
    }
    private void DrawDigDrag(Cell start,Cell end)
    {
        if(_hoverCell==end)return;_hoverCell=end;
        Clear(_hover);
        float w=Math.Abs(start.X-end.X)+1,d=Math.Abs(start.Z-end.Z)+1,h=Math.Abs(start.Level-end.Level)+(_tool=="digstairs"?3:1);
        var center=WorldPosition(new Cell(0,0,Math.Min(start.Level,end.Level)))+new Vector3((start.X+end.X)*.5f,h/2,(start.Z+end.Z)*.5f);
        bool oversized=_tool!="canceldig"&&(long)w*(long)d*(long)h>4096;
        var mat=Mat(oversized?"ff6464":_tool=="canceldig"?"ff927b":"ffd36b");
        mat.ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded;mat.NoDepthTest=true;
        foreach(float a in new[]{-1f,1f}) foreach(float b in new[]{-1f,1f})
        {
            Box(_hover,center+new Vector3(a*w/2,b*h/2,0),new Vector3(.035f,.035f,d),mat);
            Box(_hover,center+new Vector3(0,a*h/2,b*d/2),new Vector3(w,.035f,.035f),mat);
            Box(_hover,center+new Vector3(a*w/2,0,b*d/2),new Vector3(.035f,h,.035f),mat);
        }
        // Layer rings make vertical extent readable without one mesh per selected voxel.
        for(int y=1;y<h;y++)
        foreach(float sign in new[]{-1f,1f})
        {
            Box(_hover,center+new Vector3(sign*w/2,y-h/2,0),new Vector3(.015f,.015f,d),mat);
            Box(_hover,center+new Vector3(0,y-h/2,sign*d/2),new Vector3(w,.015f,.015f),mat);
        }
        _hint.Text=$"{(_tool=="canceldig"?"取消挖掘（仅未完成任务）":"挖掘")} {(int)w} × {(int)d} × {(int)h} · {Math.Min(start.Level,end.Level)} 至 {Math.Max(start.Level,end.Level)} m · +/− 选层 · 再次左键确认 / 右键或 Esc 取消{(oversized?" · 超过 4096 格，请缩小范围":"")}";
    }
}

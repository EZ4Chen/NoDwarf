using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private Cell? ChopPick(Vector2 screen)
    {
        var hit=PickObject(screen,1);
        if(hit.Count>0&&hit["collider"].AsGodotObject() is {} body&&body.HasMeta("tree_root"))
        {var root=body.GetMeta("cell").AsVector3I();return new(root.X,root.Z,root.Y);}
        return Pick(screen,1);
    }
    private IEnumerable<Cell> ChopAreaTrees(Cell a,Cell b)=>_world.Trees.Keys.Where(c=>
        c.X>=Math.Min(a.X,b.X)&&c.X<=Math.Max(a.X,b.X)&&c.Z>=Math.Min(a.Z,b.Z)&&c.Z<=Math.Max(a.Z,b.Z)&&VisibleAt(c));
    private void CommitChopArea(Cell a,Cell b)
    {
        var trees=ChopAreaTrees(a,b).OrderBy(c=>c.X).ThenBy(c=>c.Z).ToArray();
        var planned=_world.Jobs.Where(j=>j.Forestry).Select(j=>j.Cell).ToHashSet();int added=0;
        foreach(var c in trees)if(!planned.Contains(c)&&_world.QueueTree(c,true,announce:false))added++;
        _world.Log($"伐木：新增标记 {added} 棵"+(trees.Length>added?$"，跳过已安排的 {trees.Length-added} 棵。":"。"));
        SyncWorld();RefreshUi();
    }

    private sealed record DesignationMarker(Node3D Root,Sprite3D Icon,StaticBody3D Body,bool Chop);
    private readonly Dictionary<int,DesignationMarker> _designationMarkers=new();
    private DemoWorld? _designationWorld;
    private static readonly SphereShape3D DesignationHitShape=new(){Radius=.3f};
    private void SyncDesignationMarkers()
    {
        if(_designationWorld!=_world)
        {foreach(var marker in _designationMarkers.Values)RemoveVisual(marker.Root);_designationMarkers.Clear();_designationWorld=_world;}
        var jobs=_world.Jobs.Where(j=>j.Dig||j.Chop&&_world.Trees.ContainsKey(j.Cell)).ToDictionary(j=>j.Id);
        foreach(var id in _designationMarkers.Keys.Where(id=>!jobs.ContainsKey(id)).ToArray())
        {RemoveVisual(_designationMarkers[id].Root);_designationMarkers.Remove(id);}
        foreach(var job in jobs.Values)
        {
            if(!_designationMarkers.TryGetValue(job.Id,out var marker))
            {
                var root=new Node3D{Name="Designation"+job.Id};AddChild(root);
                var icon=new Sprite3D{Name="TaskIcon",Texture=UiIcons.Get(job.Chop?"mark_chop":"mark_dig"),
                    PixelSize=job.Chop?.022f:.0125f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Shaded=false,
                    NoDepthTest=false,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};root.AddChild(icon);
                var body=new StaticBody3D{CollisionLayer=2,CollisionMask=0};root.AddChild(body);
                body.AddChild(new CollisionShape3D{Shape=DesignationHitShape});
                body.SetMeta("cell",new Vector3I(job.Cell.X,job.Cell.Level,job.Cell.Z));body.SetMeta("designation",job.Id);
                _designationMarkers[job.Id]=marker=new(root,icon,body,job.Chop);
            }
            marker.Icon.Modulate=job.WaterPaused?new Color("f2a541"):Colors.White;
            bool visible=job.Dig&&CurrentView.Level is not null?job.Cell.Level==ActiveLevel:VisibleAt(job.Cell);
            marker.Root.Visible=visible;marker.Body.CollisionLayer=visible?2u:0u;
            var position=WorldPosition(job.Cell);
            // Above the visible crown, so foliage does not hide a queued felling order.
            // Mining badges follow the displayed cut surface, not the original full cube.
            position.Y=job.Chop?Math.Min(job.Cell.Level+_world.TreeHeight(job.Cell),CurrentView.Top)+.65f:
                Math.Min(job.Cell.Level+1,CurrentView.CellTop(job.Cell))+.33f;
            marker.Root.Position=position;
        }
    }
}

using Godot;
using NoDwarf.Simulation;

namespace NoDwarf;

public partial class Main
{
    private Button _cancelConstructionAction=null!;
    private ProgressBar _constructionProgress=null!;
    private sealed class ConstructionVisual
    {
        public required DemoJob Job;
        public MultiMesh? OutlineBatch;
        public int OutlineIndex;
        public int OutlineMask;
        public readonly List<(BaseMaterial3D Material,Color Color)> Materials=new();
        public float Fraction=-1;
        public bool Selected;
    }
    private readonly Dictionary<int,ConstructionVisual> _constructionVisuals=new();
    private DemoJob? SelectedConstruction => _selectedWorker<0&&_selectedCell is Cell cell
        ? _world.Jobs.FirstOrDefault(j=>j.Cell==cell&&j.Construction) : null;

    private string ConstructionStatus(DemoJob job)
        => job.Worker>=0&&job.Worker<_world.Workers.Count ? _world.Workers[job.Worker].Status
        : job.Progress>0 ? "等待继续施工" : "等待居民施工";

    private void CancelSelectedConstruction()
    {
        if(SelectedConstruction is not {} job)return;
        _world.CancelJob(job);
        _selectedCell=null;_selectionPanel.Hide();_hoverCell=null;ClearHover();
        SyncWorld();RefreshUi();
    }
    private void RefreshConstructionSelection()
    {
        var job=SelectedConstruction;
        _cancelConstructionAction.Visible=job is not null;
        _cancelConstructionAction.Text=job?.BuildingWork==true?"取消施工":job?.Demolish==true?"取消拆除":"取消开采";
        UiAccess.Name(_cancelConstructionAction,_cancelConstructionAction.Text,_cancelConstructionAction.TooltipText);
        if(job is not null)_demolishAction.Hide();
        _constructionProgress.Visible=job?.BuildingWork==true;
        if(job?.BuildingWork==true)_constructionProgress.Value=job.BuildFraction;
    }

    private void AddConstructionVisual(Node3D root,DemoJob job,BlockFaces faces=BlockFaces.All)
    {
        var shape=new Node3D();root.AddChild(shape);
        bool solid=_world.BuildBlock(job).Shape==BlockShape.Solid;
        if(solid)DrawPlanBlock(shape,job.Cell,_world.BuildBlock(job),faces);
        else DrawBuilding(shape,job.Cell,job.Build!.Value,facing:job.Facing,fullHeight:true,definitionId:job.BuildDefinitionId);
        BoxSurfaceCulling.Apply(shape);
        var visual=new ConstructionVisual{Job=job};
        void Prepare(Node node)
        {
            foreach(var child in node.GetChildren())Prepare(child);
            if(node is not MeshInstance3D mesh||mesh.Mesh is null)return;
            mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
            for(int i=0;i<mesh.Mesh.GetSurfaceCount();i++)
            {
                // Each plan owns its tint and opacity; completed buildings keep their shared materials.
                var source=mesh.GetActiveMaterial(i) as BaseMaterial3D;
                var material=source is null?new StandardMaterial3D():(BaseMaterial3D)source.Duplicate();
                var color=material.AlbedoColor;
                material.Transparency=BaseMaterial3D.TransparencyEnum.Alpha;
                mesh.SetSurfaceOverrideMaterial(i,material);
                visual.Materials.Add((material,color));
            }
            mesh.MaterialOverride=null;
            if(solid)return;
            var bounds=mesh.HasMeta("voxel_bounds")?mesh.GetMeta("voxel_bounds").AsAabb():mesh.Mesh.GetAabb();
            var body=new Area3D{Position=bounds.GetCenter(),CollisionLayer=8,CollisionMask=0,Monitoring=false,Monitorable=false};
            body.SetMeta("cell",new Vector3I(job.Cell.X,job.Cell.Level,job.Cell.Z));
            body.SetMeta("construction",job.Id);mesh.AddChild(body);
            using(var collisionShape=new BoxShape3D{Size=bounds.Size.Max(new Vector3(.02f,.02f,.02f))})body.AddChild(new CollisionShape3D{Shape=collisionShape});
            if(!mesh.Visible)body.CollisionLayer=0;
        }
        Prepare(shape);
        if(solid)
        {
            // Selection-only area: no physics body, pathfinding obstacle or support volume.
            // Keep a full cell target even when every rendered face is hidden.
            float height=Math.Clamp(CurrentView.CellTop(job.Cell)-job.Cell.Level,0,1);
            var area=new Area3D{Position=WorldPosition(job.Cell)+Vector3.Up*height/2,CollisionLayer=8,CollisionMask=0,Monitoring=false,Monitorable=false};
            area.SetMeta("cell",new Vector3I(job.Cell.X,job.Cell.Level,job.Cell.Z));area.SetMeta("construction",job.Id);shape.AddChild(area);
            using(var collisionShape=new BoxShape3D{Size=new(1,height,1)})area.AddChild(new CollisionShape3D{Shape=collisionShape});
        }
        _constructionVisuals[job.Id]=visual;
        _constructionOutlinesDirty=true;
        UpdateConstructionVisual(visual);
    }
    private void UpdateConstructionVisuals()
    {
        // Progress never rebuilds meshes, collisions, terrain or navigation.
        foreach(var visual in _constructionVisuals.Values)UpdateConstructionVisual(visual);
        SyncConstructionOutlines();
    }
    private void UpdateConstructionVisual(ConstructionVisual visual)
    {
        bool selected=_selectedWorker<0&&_selectedCell==visual.Job.Cell;
        if(selected!=visual.Selected)
        {visual.Selected=selected;_constructionOutlinesDirty=true;}
        float fraction=(float)visual.Job.BuildFraction;
        if(visual.Fraction==fraction)return;
        visual.Fraction=fraction;
        foreach(var (material,color) in visual.Materials)
        {
            var tint=new Color(.45f,.75f,.9f).Lerp(Colors.White,fraction);
            material.AlbedoColor=new Color(color.R*tint.R,color.G*tint.G,color.B*tint.B,color.A*Mathf.Lerp(.22f,1,fraction));
        }
    }
}

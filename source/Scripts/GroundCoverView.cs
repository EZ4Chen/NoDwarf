using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class TerrainView
{
    private readonly Dictionary<(int,int),Node3D> _coverChunks=new();
    private DemoWorld? _coverWorld;
    private float _coverTop=float.NaN;
    private int _coverRevision=-1,_coverWorldRevision=-1;
    private HashSet<Cell> _coverOccupants=new();
    private readonly Dictionary<string,Mesh> _coverMeshes=new();
    public int VisibleCoverInstances {get;private set;}
    private readonly Dictionary<(int,int),int> _coverCounts=new();
    private void UpdateGroundCover(DemoWorld world,WorldView view)
    {
        bool all=_coverWorld!=world||_coverTop!=view.Top;
        if(!all&&_coverRevision==world.Terrain.Revision&&_coverWorldRevision==world.Revision)return;
        if(_coverWorld!=world){foreach(var n in _coverChunks.Values)if(IsInstanceValid(n)){n.GetParent()?.RemoveChild(n);n.QueueFree();}_coverChunks.Clear();_coverCounts.Clear();_coverMeshes.Clear();}
        var occupied=world.Buildings.Keys.Concat(world.Farms.Keys).Concat(world.Stockpiles).ToHashSet();
        var dirty=new HashSet<(int,int)>();
        void Dirty(Cell c){if(DemoWorld.Inside(c))dirty.Add((c.X/32*32,c.Z/32*32));}
        if(all)for(int x=0;x<TerrainMap.Size;x+=32)for(int z=0;z<TerrainMap.Size;z+=32)dirty.Add((x,z));
        else
        {
            foreach(var c in world.Terrain.ChangesSince(_coverRevision))Dirty(c);
            foreach(var c in occupied.Except(_coverOccupants).Concat(_coverOccupants.Except(occupied)))Dirty(c);
        }
        foreach(var key in dirty)
        {
            if(_coverChunks.Remove(key,out var old)&&IsInstanceValid(old)){RemoveChild(old);old.QueueFree();}
            _coverCounts[key]=0;
            if(world.Terrain.Region?.Modern!=true)continue;
            var groups=new Dictionary<string,List<Transform3D>>();
            for(int x=key.Item1;x<Math.Min(key.Item1+32,TerrainMap.Size);x++)for(int z=key.Item2;z<Math.Min(key.Item2+32,TerrainMap.Size);z++)
            {
                var c=world.Surface(x,z);if(c.Level+.85f>view.Top)continue;
                string? type=GroundCover.At(world,c);if(type is null)continue;
                if(!groups.TryGetValue(type,out var list))groups[type]=list=new();
                float roll=(float)world.Terrain.Hash(x+211,z+997),height=type=="dead_bush"?.45f:.65f+roll*.35f;
                var basis=new Basis(Vector3.Up,roll*Mathf.Tau).Scaled(new Vector3(.55f+roll*.3f,height,.55f+roll*.3f));
                list.Add(new(basis,Point(x+(roll-.5f)*.35f,c.Level+.015f,z+((float)world.Terrain.Hash(x+93,z+313)-.5f)*.35f)));
            }
            var holder=new Node3D();AddChild(holder);_coverChunks[key]=holder;
            foreach(var group in groups)
            {
                if(!_coverMeshes.TryGetValue(group.Key,out var mesh))
                {
                    var st=new SurfaceTool();st.Begin(Mesh.PrimitiveType.Triangles);
                    var mat=(StandardMaterial3D)BlockTextures.Material("nodwarf:cover_"+group.Key).Duplicate();
                    if(group.Key!="dead_bush")mat.AlbedoColor=BlockTextures.BiomeGrassColor(world.Terrain.Region!);
                    mat.BacklightEnabled=true;mat.Backlight=new Color(.6f,.6f,.6f);
                    st.SetMaterial(mat);
                    BlockTextures.Quad(st,new(-.5f,1,0),new(.5f,1,0),new(.5f,0,0),new(-.5f,0,0));
                    BlockTextures.Quad(st,new(0,1,-.5f),new(0,1,.5f),new(0,0,.5f),new(0,0,-.5f));
                    st.GenerateNormals();mesh=st.Commit();_coverMeshes[group.Key]=mesh;
                }
                var mm=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=mesh,InstanceCount=group.Value.Count};
                for(int i=0;i<group.Value.Count;i++)mm.SetInstanceTransform(i,group.Value[i]);
                holder.AddChild(new MultiMeshInstance3D{Multimesh=mm,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
                _coverCounts[key]+=group.Value.Count;
            }
        }
        VisibleCoverInstances=_coverCounts.Values.Sum();_coverWorld=world;_coverTop=view.Top;
        _coverRevision=world.Terrain.Revision;_coverWorldRevision=world.Revision;_coverOccupants=occupied;
    }
}

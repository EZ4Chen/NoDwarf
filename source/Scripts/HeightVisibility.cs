using Godot;
namespace NoDwarf;

public partial class TerrainView
{
    private sealed record HeightBatch(MultiMesh Mesh,float[] Tops)
    {
        public int ShowBelow(float height)
        {
            int lo=0,hi=Tops.Length;
            while(lo<hi){int mid=(lo+hi)/2;if(Tops[mid]<=height)lo=mid+1;else hi=mid;}
            Mesh.VisibleInstanceCount=lo;return lo;
        }
    }
    private readonly List<HeightBatch> _treeSlices=new();
    private readonly List<(StaticBody3D Body,int Level)> _treeBodies=new();
    private float _treeClip=float.NaN;
    private void UpdateTreeClip(float clip)
    {
        if(_treeClip==clip)return;_treeClip=clip;
        foreach(var batch in _treeSlices)batch.ShowBelow(clip);
        foreach(var item in _treeBodies)item.Body.CollisionLayer=item.Level<clip?1u:0u;
    }
    private readonly Dictionary<(int,int),List<HeightBatch>> _coverSlices=new();
    private void UpdateCoverClip(float clip)
    {
        foreach(var pair in _coverSlices)_coverCounts[pair.Key]=pair.Value.Sum(batch=>batch.ShowBelow(clip));
        VisibleCoverInstances=_coverCounts.Values.Sum();_coverTop=clip;
    }
}

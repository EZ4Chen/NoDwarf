using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private sealed class FallVisual
    {
        public required Node3D Root;
        public required HashSet<Cell> Targets;
        public required int[] ItemIds;
        public float Age,Duration,Distance;
    }
    private readonly List<FallVisual> _fallVisuals=new();
    private readonly List<(Node3D Root,uint[] Layers)> _fallHidden=new();
    private WorldView _fallView;
    private void RestoreFallHidden()
    {
        foreach(var (root,layers) in _fallHidden)
        {
            if(!GodotObject.IsInstanceValid(root)||root.IsQueuedForDeletion())continue;
            root.Visible=true;int i=0;
            foreach(var body in root.FindChildren("*","CollisionObject3D",true,false).OfType<CollisionObject3D>())
                if(i<layers.Length)body.CollisionLayer=layers[i++];
        }
        _fallHidden.Clear();
    }
    private void HideDuringFall(Node3D root)
    {
        if(!root.Visible)return;
        var bodies=root.FindChildren("*","CollisionObject3D",true,false).OfType<CollisionObject3D>().ToArray();
        _fallHidden.Add((root,bodies.Select(b=>b.CollisionLayer).ToArray()));root.Visible=false;
        foreach(var b in bodies)b.CollisionLayer=0;
    }
    private void ClearFallVisuals()
    {
        RestoreFallHidden();foreach(var f in _fallVisuals)RemoveVisual(f.Root);_fallVisuals.Clear();
    }
    private void BeginFallVisuals(bool reset)
    {
        RestoreFallHidden();
        if(reset){ClearFallVisuals();_world.VisualFalls.Clear();return;}
        while(_world.VisualFalls.TryDequeue(out var fall))
        {
            if(_fallVisuals.Count>=16){RemoveVisual(_fallVisuals[0].Root);_fallVisuals.RemoveAt(0);}
            var root=new Node3D();AddChild(root);var targets=new HashSet<Cell>();int count=0;
            foreach(var b in fall.Buildings)
            {
                var target=b.Cell+new Cell(0,0,-fall.Distance);
                if(!VisibleAt(b.Cell)||!VisibleAt(target))continue;
                Node3D moving;
                if(_buildingVisuals.Remove(b.Cell,out var old))moving=old.Root;
                else {moving=new Node3D();root.AddChild(moving);DrawBuilding(moving,b.Cell,b.Kind,facing:b.Facing,fullHeight:true,definitionId:b.DefinitionId);}

                // Reuse existing meshes/materials; one parent transform moves the group.
                if(moving.GetParent()!=root)moving.Reparent(root);
                foreach(var body in moving.FindChildren("*","CollisionObject3D",true,false).OfType<CollisionObject3D>())body.CollisionLayer=0;
                targets.Add(target);count++;
            }
            if(count==0){RemoveVisual(root);continue;}
            _fallView=CurrentView;
            _fallVisuals.Add(new(){Root=root,Targets=targets,ItemIds=fall.ItemIds,Distance=fall.Distance,Duration=Math.Clamp(MathF.Sqrt(2*fall.Distance/20f),.18f,.85f)});
        }
    }
    private void AnimateBuildingFalls(double delta)
    {
        if(_fallVisuals.Count==0&&_fallHidden.Count==0)return;
        // A changed slice must never reveal geometry from the previous view.
        if(_fallView!=CurrentView){ClearFallVisuals();return;}
        if(_fallVisuals.Any(f=>f.Age+(float)delta>=f.Duration))RestoreFallHidden();
        for(int i=_fallVisuals.Count-1;i>=0;i--)
        {
            var f=_fallVisuals[i];f.Age+=(float)delta;
            float t=Math.Clamp(f.Age/f.Duration,0,1);f.Root.Position=Vector3.Down*(f.Distance*t*t);
            if(t>=1){RemoveVisual(f.Root);_fallVisuals.RemoveAt(i);continue;}
            foreach(var c in f.Targets)if(_buildingVisuals.TryGetValue(c,out var b))HideDuringFall(b.Root);
            // Dropped resources appear at impact, rather than underneath a falling ghost.
            foreach(int id in f.ItemIds)
                if(_itemNodes.TryGetValue(id,out var n))HideDuringFall(n.Root);
        }
    }
}

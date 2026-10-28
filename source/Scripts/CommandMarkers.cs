using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private readonly Dictionary<string,Node3D> _commandMarkers=new();
    private DemoWorld? _markerWorld;
    private void UpdateCommandMarkers()
    {
        if(_markerWorld!=_world){foreach(var n in _commandMarkers.Values)n.QueueFree();_commandMarkers.Clear();_markerWorld=_world;}
        var live=new HashSet<string>();
        void Show(string id,Cell cell,string text,string color,bool flag=false)
        {
            live.Add(id);
            if(!_commandMarkers.TryGetValue(id,out var root))
            {
                root=new Node3D{Name="CommandMarker"};AddChild(root);_commandMarkers[id]=root;
                var material=new StandardMaterial3D{AlbedoColor=new Color(color),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,NoDepthTest=false};
                void Bar(Vector3 p,Vector3 size){var mesh=new MeshInstance3D{Mesh=new BoxMesh{Size=size},MaterialOverride=material,Position=p,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};root.AddChild(mesh);}
                if(flag){Bar(new(0,.5f,0),new(.035f,1,.035f));Bar(new(.18f,.85f,0),new(.36f,.22f,.025f));}
                else
                {
                    for(int k=0;k<4;k++){bool x=k<2;float side=k%2==0?-.43f:.43f;Bar(x?new(side,0,0):new(0,0,side),x?new(.045f,.025f,.9f):new(.9f,.025f,.045f));}
                    if(id.StartsWith("attack:")){Bar(new(0,0,0),new(.6f,.025f,.045f));Bar(new(0,0,0),new(.045f,.025f,.6f));}
                }
                root.AddChild(new Label3D{Name="Caption",Font=GameFont.Regular,FontSize=32,PixelSize=.0075f,Position=new(0,flag?1.2f:.25f,0),Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Modulate=new Color(color),OutlineSize=8,NoDepthTest=false});
            }
            root.Position=WorldPosition(cell)+new Vector3(0,.065f,0);root.Visible=VisibleAt(cell);
            var label=root.GetNode<Label3D>("Caption");if(label.Text!=text)label.Text=text;
        }
        if(_world.RallyPoint is Cell rally)Show("rally",rally,$"集合点 · {rally.Level}m","edce78",true);
        var targets=new Dictionary<string,List<string>>();
        for(int i=0;i<_world.Workers.Count;i++)
        {
            var w=_world.Workers[i];if(w.Person.Incapacitated)continue;
            Cell? destination=w.Combat.Destination;
            if(destination is null&&IsResidentSelected(i)&&w.Path.Count>0)destination=w.Path.Last();
            if(destination is Cell goal&&(w.Combat.Drafted||_world.CivilianAlert||IsResidentSelected(i)))
                Show("move:"+w.Id,goal,$"{w.Name}\n→ {goal.Level}m","8ed1ee");
            if(w.Combat.Drafted&&w.Combat.Target!="")
            {if(!targets.TryGetValue(w.Combat.Target,out var names)){names=new();targets[w.Combat.Target]=names;}names.Add(w.Name);}
        }
        foreach(var target in targets)
            if(_world.Enemies.FirstOrDefault(e=>e.Id==target.Key&&!e.Person.Incapacitated) is {} enemy)
                Show("attack:"+target.Key,enemy.Tile,$"攻击：{enemy.Name} · {enemy.Level}m\n{string.Join("、",target.Value.Take(3))}{(target.Value.Count>3?$" 等 {target.Value.Count} 人":"")}","ff947c");
        foreach(var id in _commandMarkers.Keys.Where(id=>!live.Contains(id)).ToArray()){_commandMarkers[id].QueueFree();_commandMarkers.Remove(id);}
    }
}

using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

// Frozen pre-worker terrain emitter, used only by --terrain-pipeline-test as an
// independent geometry/UV/normal/collision oracle. Never used in normal gameplay.
internal static class TerrainMeshReference
{
    private static Vector3 Point(float x,float y,float z)=>TerrainView.Point(x,y,z);
    private static readonly StandardMaterial3D Fog=new(){AlbedoColor=Colors.Black,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
    public static Node3D Build(DemoWorld world,WorldView view,(int,int) key)
    {
        var terrain=world.Terrain;float clip=view.Top;
        var extra=new Dictionary<(int,int),HashSet<int>>();
        void Candidate(Cell c)
        {
            if(!DemoWorld.Inside(c))return;
            if(!extra.TryGetValue((c.X,c.Z),out var ys))extra[(c.X,c.Z)]=ys=new();
            ys.Add(c.Level);
        }
        foreach(var c in terrain.Excavated.Concat(terrain.PlacedBlocks.Select(p=>p.Cell)).Concat(view.WallCaps?.Keys ?? Enumerable.Empty<Cell>()))
            foreach(var d in new[]{new Cell(0,0),new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1),new Cell(0,0,1),new Cell(0,0,-1)})Candidate(c+d);
            var holder=new Node3D();
            var surfaces=new Dictionary<(string,bool,bool,bool,bool,bool),SurfaceTool>();
            SurfaceTool Surface(string id,bool top,bool section=false,bool cut=false,bool rim=false,bool underground=false)
            {
                if(surfaces.TryGetValue((id,top,section,cut,rim,underground),out var found))return found;
                var material=id.StartsWith("nodwarf:",StringComparison.Ordinal)?BlockTextures.TerrainMaterial(id,top,terrain.Region):new StandardMaterial3D{AlbedoColor=new Color(world.Content.Block(id).Color),Roughness=1};
                if(section)material=Fog;
                else if(rim){material=(StandardMaterial3D)material.Duplicate();material.AlbedoColor=new Color(.26f,.28f,.30f,1);}
                else if(cut){material=(StandardMaterial3D)material.Duplicate();material.AlbedoColor*=new Color(.62f,.65f,.68f,1);}
                if(underground&&!section)
                {
                    material=(StandardMaterial3D)material.Duplicate();
                    material.ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded;
                    if(rim)material.AlbedoColor=new Color(.18f,.20f,.22f,1);
                    else if(cut){material.AlbedoColor=new Color(.34f,.40f,.45f,1);}
                    else material.AlbedoColor=top?new Color(.80f,.83f,.85f,1):new Color(.43f,.49f,.55f,1);
                }
                var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);tool.SetMaterial(material);surfaces[(id,top,section,cut,rim,underground)]=tool;return tool;
            }
            var edges=new SurfaceTool();edges.Begin(Mesh.PrimitiveType.Triangles);
            edges.SetMaterial(new StandardMaterial3D{AlbedoColor=new Color("20272e"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,CullMode=BaseMaterial3D.CullModeEnum.Disabled});
            bool hasEdges=false;
            for(int x=key.Item1;x<Math.Min(key.Item1+16,TerrainMap.Size);x++) for(int z=key.Item2;z<Math.Min(key.Item2+16,TerrainMap.Size);z++)
            {
                var g=terrain[x,z];bool underground=clip<=g.Level;int highest=Math.Min(g.Level,(int)Math.Min(clip,TerrainMap.MaxLevel+1));
                int low=g.Level-1;
                foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                    low=Math.Min(low,DemoWorld.Inside(new Cell(x+dx,z+dz))?terrain[x+dx,z+dz].Level-1:TerrainMap.MinLevel);
                var levels=new HashSet<int>{highest,highest-1};
                for(int level=Math.Max(TerrainMap.MinLevel,low);level<=highest;level++)levels.Add(level);
                if(extra.TryGetValue((x,z),out var additional))levels.UnionWith(additional);
                foreach(int y in levels)

                {
                    if(y<TerrainMap.MinLevel||y>TerrainMap.MaxLevel||y>highest&&y>=clip)continue;
                    var cell=new Cell(x,z,y);bool ramp=terrain.Ramp(cell);
                    float cellClip=view.CellTop(cell);
                    if(y>=cellClip||(!terrain.Solid(cell)&&!ramp))continue;
                    bool rim=view.CornerRim(world,cell);bool fog=!rim&&!view.Known(world,cell);
                    string id=(fog||rim)?"nodwarf:stone":terrain.BlockAt(cell);float top=Math.Min(y+1,cellClip);
                    Vector3 Corner(float dx,float dz)=>Point(x+dx,ramp?Math.Min(terrain.TileHeight(x,z,dx,dz),cellClip):top,z+dz);
                    var a=Corner(-.5f,-.5f);var b=Corner(.5f,-.5f);var c=Corner(.5f,.5f);var d=Corner(-.5f,.5f);
                    var above=cell+new Cell(0,0,1);
                    bool solidAbove=terrain.Solid(above)||terrain.Ramp(above)||world.Buildings.TryGetValue(above,out var aboveBuilding)&&BlockCatalog.IsWall(aboveBuilding.Kind);
                    // A camera cut also exposes the top below a hidden solid. Seal it
                    // with the existing knowledge/material policy, never the sky background.
                    bool cutAbove=solidAbove&&view.CellTop(above)<=above.Level;
                    if(ramp||y+1>=cellClip||!solidAbove||cutAbove)
                        BlockTextures.Quad(Surface(id,true,fog,(y+1>=cellClip&&solidAbove)||cutAbove,rim,underground),a,d,c,b);
                    if(underground&&!fog&&!rim&&!ramp&&!terrain.Solid(cell+new Cell(0,0,1)))
                    {
                        foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                        {
                            // Ground contact line is independent of sun shadows and has no collision.
                            var neighbor=cell+new Cell(dx,dz,1);
                            if(!world.Solid(neighbor))continue;
                            var center=Point(x+dx*.475f,top+.008f,z+dz*.475f);
                            var half=new Vector3(dx==0?.5f:.025f,0,dz==0?.5f:.025f);
                            BlockTextures.Quad(edges,center+new Vector3(-half.X,0,-half.Z),center+new Vector3(-half.X,0,half.Z),center+new Vector3(half.X,0,half.Z),center+new Vector3(half.X,0,-half.Z));
                            hasEdges=true;
                        }
                    }
                    foreach(var (p,q,dx,dz) in new[]{(a,b,0,-1),(b,c,1,0),(c,d,0,1),(d,a,-1,0)})
                    {
                        if((terrain.Solid(cell+new Cell(dx,dz))||world.Buildings.TryGetValue(cell+new Cell(dx,dz),out var sideBuilding)&&BlockCatalog.IsWall(sideBuilding.Kind)) && view.CellTop(cell+new Cell(dx,dz))>=top)continue;
                        if(p.Y<=y&&q.Y<=y)continue;
                        BlockTextures.Quad(Surface(id,false,fog,rim:rim,underground:underground),p,q,new(q.X,y,q.Z),new(p.X,y,p.Z),1,top-y);
                    }
                    if(y>TerrainMap.MinLevel&&!terrain.Solid(cell+new Cell(0,0,-1))&&!(world.Buildings.TryGetValue(cell+new Cell(0,0,-1),out var belowBuilding)&&BlockCatalog.IsWall(belowBuilding.Kind)&&view.CellTop(cell+new Cell(0,0,-1))>=y))
                        BlockTextures.Quad(Surface(id,false,fog,rim:rim,underground:underground),Point(x-.5f,y,z+.5f),Point(x-.5f,y,z-.5f),Point(x+.5f,y,z-.5f),Point(x+.5f,y,z+.5f));
                }
            }
            if(hasEdges)
            {
                edges.GenerateNormals();var edgeMesh=new ArrayMesh();edges.Commit(edgeMesh);
                holder.AddChild(new MeshInstance3D{Mesh=edgeMesh,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
            }
            var mesh=new ArrayMesh();foreach(var surface in surfaces.Values){surface.GenerateNormals();surface.Commit(mesh);}
            if(mesh.GetSurfaceCount()>0)
            {
                var instance=new MeshInstance3D{Mesh=mesh};holder.AddChild(instance);
                using var collisionTiming = world.Probe?.Measure("view.terrain-collision-create") ?? default;
                instance.CreateTrimeshCollision();
            }
        return holder;
    }
}

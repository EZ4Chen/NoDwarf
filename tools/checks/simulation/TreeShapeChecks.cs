namespace NoDwarf.Simulation;
public static class TreeShapeChecks
{
    private static void Check(bool b,string message){if(!b)throw new InvalidOperationException(message);}
    public static void Run()
    {
        var w=new DemoWorld();w.DeveloperResidents(false);var tree=w.Trees.Values.First(t=>t.Species=="sapling");var root=tree.Cell;
        var shape=TreeShape.Generate(w.Terrain.Seed,tree);
        Check(shape.SequenceEqual(TreeShape.Generate(w.Terrain.Seed,tree)),"Same tree shape deterministic");
        Check(shape.Select(v=>v.Offset).Distinct().Count()==shape.Count&&shape.All(v=>DemoWorld.Inside(root+v.Offset)),"Tree voxels unique and bounded");
        Check(shape.Any(v=>v.Leaf)&&shape.Any(v=>!v.Leaf&&v.Offset.X!=0||!v.Leaf&&v.Offset.Z!=0),"Leaves and branches generated");
        Check(w.Trees.Values.Where(t=>t.Species==tree.Species).Take(12).Select(t=>string.Join(';',TreeShape.Generate(w.Terrain.Seed,t))).Distinct().Count()>1,"Tree shapes vary by location");
        Check(w.CanDig(root)&&w.BlockAt(root)=="nodwarf:tree","Bottom trunk is targetable and diggable");
        Check(!w.DamageBlock(root,20)&&w.RemainingDurability(root)==60,"Trunk damage retained");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.RemainingDurability(root)==60&&shape.SequenceEqual(TreeShape.Generate(w.Terrain.Seed,w.Trees[root])),"Save restores damage and exact tree shape");
        int wood=w.Total("wood");Check(w.DamageBlock(root,60)&&!w.Trees.ContainsKey(root)&&!w.Terrain[root.X,root.Z].Tree,"Breaking base removes entire tree");
        Check(w.TreeFalls.Count==1&&w.Total("wood")==wood+tree.Height*2,"One fall and one drop settlement");
        Check(!w.DamageBlock(root,100)&&w.TreeFalls.Count==1,"Destroyed base cannot duplicate drops");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.TreeFalls.Count==0&&!w.Trees.ContainsKey(root)&&w.Total("wood")==wood+tree.Height*2,"Reload during fall cannot regrow or duplicate tree");
        foreach(var c in new[]{new Cell(0,0,114),new Cell(254,254,114)})
        {var edge=new TreeState{Cell=c,Species="pine_sapling",Age=780,MaximumHeight=14};Check(TreeShape.Generate(42,edge).All(v=>DemoWorld.Inside(c+v.Offset)),"Edge crown stays inside world");}
        Console.WriteLine("TREE_SHAPE_OK: deterministic voxel variations, bounds, trunk damage, whole-tree falling, single drops and save/load");
    }
}

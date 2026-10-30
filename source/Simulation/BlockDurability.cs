namespace NoDwarf.Simulation;
public sealed record BlockDamage(Cell Cell,string Block,double Damage);
public sealed partial class DemoWorld
{
    private readonly Dictionary<Cell,BlockDamage> _blockDamage=new();
    public double RemainingDurability(Cell c)
    {
        string id=BlockAt(c);
        if(id=="nodwarf:air"&&Jobs.FirstOrDefault(j=>j.Cell==c&&j.BuildingWork) is {} plan)return plan.BuildDurability;
        double max=Content.Block(id).Durability;
        return Math.Max(0,max-(_blockDamage.TryGetValue(c,out var d)&&d.Block==id?d.Damage:0));
    }
    // Shared by mining and future destructive actions. Creative edits bypass damage.
    public bool DamageBlock(Cell c,double amount)
    {
        if(!Inside(c)||!double.IsFinite(amount)||amount<=0)return false;
        string id=BlockAt(c);
        if(id=="nodwarf:air"&&Jobs.FirstOrDefault(j=>j.Cell==c&&j.BuildingWork) is {} plan)
        {
            if(plan.BuildDurability<=0)return false; // An unstarted blueprint has no physical structure yet.
            if(amount>=plan.BuildDurability){CancelJob(plan);return true;}
            plan.Progress=Math.Max(0,plan.Progress-amount/BuildBlock(plan).Durability*DemoJob.BuildWorkRequired);
            return false;
        }
        double remaining=RemainingDurability(c);
        if(remaining<=0||double.IsPositiveInfinity(remaining))return false;
        if(Buildings.ContainsKey(c)&&Workers.Any(w=>w.Tile==c+new Cell(0,0,1)))return false;
        if(amount<remaining){_blockDamage[c]=new(c,id,Content.Block(id).Durability-remaining+amount);return false;}
        _supportSafety.Clear();if(RemovalEndangersResident(c))return false;
        _blockDamage.Remove(c);
        if(Buildings.Remove(c,out var building)){Refund(building,c);ValidateWorkshop();Revision++;}
        else if(Trees.ContainsKey(c))FellTree(c);
        else MineWithDrop(c);
        return true;
    }
    public void QueueDemolish(Cell c)
    {
        if(Trees.ContainsKey(c)){QueueTree(c,true);return;}
        if(Jobs.Any(j=>j.Construction&&j.Cell==c)){Erase(c);return;}
        if(!Buildings.ContainsKey(c))return;
        Jobs.Add(new(){Id=++_nextJob,Cell=c,Dig=true,Demolish=true});Revision++;Log("已规划拆除，居民会逐步破坏方块并回收材料。");
    }
    private bool MineStep(DemoJob job,double work)
    {
        if(job.Demolish){DamageBlock(job.Cell,work*50);return !Buildings.ContainsKey(job.Cell);}
        for(int y=0;y<job.DigHeight;y++)
        {
            var c=job.Cell+new Cell(0,0,y);
            if(!NaturalSolid(c)&&!NaturalRamp(c))continue;
            DamageBlock(c,work*50);break;
        }
        return Enumerable.Range(0,job.DigHeight).All(y=>!NaturalSolid(job.Cell+new Cell(0,0,y))&&!NaturalRamp(job.Cell+new Cell(0,0,y)));
    }
}

namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    private ResidentResult BeginMedicalSupply(DemoWorker medic,string patientId,string origin)
    {
        var patient=Workers.FirstOrDefault(w=>w.Id==patientId);
        if(patient is null||patient.Person.Dead||!patient.Person.Injuries.Any(i=>!i.Treated&&i.Kind!="missing"))return new(false,"伤员已不需要护理");
        if(Workers.Any(w=>w!=medic&&w.Person.Activity is {Kind:"treat" or "care_supply"} a&&a.Target==patientId))return new(false,"已有护理者负责这名伤员");
        RefreshWeight(medic);
        if(medic.Person.CarriedWeight+.25>medic.Person.Capacity)return new(false,"护理负重不足，请先放下物资");
        var supplies=Items.Where(s=>s.Item=="bandage"&&s.Owner==""&&s.Count>Reserved(s.Id)).ToArray();
        if(supplies.Length==0)return new(false,"护理缺料：没有可用的公共绷带");
        foreach(var stack in supplies.OrderBy(s=>Math.Abs(s.Cell.X-medic.Tile.X)+Math.Abs(s.Cell.Z-medic.Tile.Z)))
        {
            var positions=ItemPositions(stack.Cell).Where(c=>Walkable(c)&&Navigation.CanReach(c,medic.Tile)&&CanReachAny(c,ItemPositions(patient.Tile).Where(Walkable).ToArray()));
            var path=Navigation.FindNearest(medic.Tile,positions,out var destination);if(path is null)continue;
            if(medic.Job is not null)Release(medic.Job);CancelResidentActivity(medic);
            medic.Person.Activity=new(){Kind="care_supply",Target=patientId,ItemId=stack.Id,Destination=destination,Origin=origin};
            foreach(var c in path)medic.Path.Enqueue(c);medic.CheckedNavigationVersion=Navigation.Version;
            medic.Status="取用公共绷带 → "+patient.Name;medic.Person.DecisionVersion++;return new(true,medic.Status);
        }
        return new(false,"护理断路：无法取到绷带并抵达伤员");
    }
    private bool CompleteMedicalSupply(DemoWorker medic,ResidentActivity activity)
    {
        var stack=Items.FirstOrDefault(s=>s.Id==activity.ItemId&&s.Item=="bandage"&&s.Owner=="");
        if(stack is null||stack.Count<1||Math.Abs(medic.Tile.X-stack.Cell.X)+Math.Abs(medic.Tile.Z-stack.Cell.Z)>1||Math.Abs(medic.Level-stack.Cell.Level)>1)
        {CancelResidentActivity(medic);medic.Status="护理用品已移动或不可达";return true;}
        RefreshWeight(medic);
        if(medic.Person.CarriedWeight+.25>medic.Person.Capacity){CancelResidentActivity(medic);medic.Status="护理负重不足";return true;}
        stack.Count--;if(stack.Count==0)Items.Remove(stack);
        Items.Add(new(){Id=++_nextItem,Item="bandage",Count=1,Owner=medic.Id,Cell=medic.Tile});
        CancelResidentActivity(medic);RefreshWeight(medic);Revision++;
        var result=BeginResidentActivity(medic,"treat",activity.Target,origin:activity.Origin);
        if(!result.Ok)medic.Status=result.Message;
        return true;
    }
}

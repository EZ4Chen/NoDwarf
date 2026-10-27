using NoDwarf.Simulation;

internal static class ResidentHotPathChecks
{
    public static void Run()
    {
        var random=new Random(74219);var p=ResidentProfile.Create(0);
        void Same(double actual,double expected,string label)
        {if(Math.Abs(actual-expected)>1e-12)throw new InvalidOperationException("Resident hot path differs: "+label);}
        for(int sample=0;sample<500;sample++)
        {
            p.Injuries.Clear();
            for(int i=0;i<sample%41;i++)p.Injuries.Add(new(){Part=ResidentCatalog.Parts[random.Next(10)].Id,Kind=i%3==0?"missing":"cut",Severity=random.NextDouble()*100});
            p.Blood=random.NextDouble()*100;p.Hunger=random.NextDouble()*100;p.Rest=random.NextDouble()*100;p.CarriedWeight=random.NextDouble()*60;
            double Integrity(string part)=>Math.Clamp(1-p.Injuries.Where(i=>i.Part==part).Sum(i=>i.Severity)/100,0,1);
            double manipulation=(Integrity("left_arm")*Integrity("left_hand")+Integrity("right_arm")*Integrity("right_hand"))/2;
            double mobility=(Integrity("left_leg")*Integrity("left_foot")+Integrity("right_leg")*Integrity("right_foot"))/2;
            double consciousness=Math.Min(Math.Min(Integrity("head"),Integrity("torso")),p.Blood/100);
            foreach(var part in ResidentCatalog.Parts)Same(p.Integrity(part.Id),Integrity(part.Id),part.Id);
            Same(p.Pain,Math.Min(100,p.Injuries.Where(i=>i.Kind!="missing").Sum(i=>i.Severity)*.5),"pain");
            Same(p.MoveRate,consciousness<.2?0:Math.Clamp(mobility*consciousness*Math.Min(1,p.Capacity/Math.Max(1,p.CarriedWeight)),.08,1),"movement");
            foreach(var skill in ResidentCatalog.Skills)
            {
                double expected=consciousness<.2||skill.Id!="social"&&manipulation<.05?0:
                    (1+p.Level(skill.Id)*.02+p.Talents.GetValueOrDefault(skill.Id)*.15)*(skill.Id=="social"?1:manipulation)*consciousness*(p.Traits.Contains("diligent")?1.05:1)*(p.Hunger<20?.75:1)*(p.Rest<20?.75:1);
                Same(p.WorkRate(skill.Id),expected,"work "+skill.Id);
            }
            foreach(var type in ResidentWork.Types)
            {
                int priority=random.Next(5);p.WorkPriorities[type.Id]=priority;
                int index=Array.FindIndex(ResidentWork.Types,t=>t.Id==type.Id);
                Same(ResidentWork.Rank(p,type.Id),!type.Available||priority==0?int.MaxValue:priority*ResidentWork.Types.Length+index,"rank");
            }
        }
        Console.WriteLine("RESIDENT_HOT_PATH_OK: 500 mutable injury/priority states match original formulas");
    }
}

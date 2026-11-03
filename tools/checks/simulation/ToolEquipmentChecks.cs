namespace NoDwarf.Simulation;

public static class ToolEquipmentChecks
{
    public static readonly string[] Items=["axe","battleaxe","hammer","shovel","hoe"];
    private static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("TOOL_EQUIPMENT: "+why);}
    private static ItemStack Equip(DemoWorld world,DemoWorker worker,string id)
    {
        var item=world.SpawnItem(id,1,worker.Tile);item.Owner=worker.Id;
        Check(world.ActResident(new(worker.Id,"equip",ItemId:item.Id)).Ok,"equip "+id);return item;
    }
    public static void Run()
    {
        foreach(string id in Items)
        {
            var world=CombatChecks.Fixture();var worker=world.Workers[0];
            Equip(world,worker,"shield");var tool=Equip(world,worker,id);
            Check(world.Inventory(worker).Count(i=>i.Slot=="main")==1,"one main hand");
            Check(world.Inventory(worker).Any(i=>i.Slot=="off")!=ResidentGear.Get(id)!.TwoHanded,"two-handed shield exclusion: "+id);
            Check(world.EquippedWeapon(worker).Id==id,"real weapon profile: "+id);
            if(id=="axe")
            {
                Check(world.GearWorkBonus(worker,new(){Chop=true})==.15,"axe speeds forestry");
                Check(world.GearWorkBonus(worker,new(){Recipe="shield"})==0,"axe does not speed carpentry");
            }
            if(id=="hoe")
            {
                Check(world.GearWorkBonus(worker,new(){Sow=true})==.15,"hoe speeds sowing");
                Check(world.GearWorkBonus(worker,new(){Harvest=true})==0,"hoe does not speed harvesting");
            }
            if(id=="shovel")Check(world.GearWorkBonus(worker,new(){Dig=true,Cell=new(60,60,-1)})==0,"shovel does not speed stone mining");
            var saved=DemoWorld.LoadJson(world.SaveJson());
            Check(saved.Inventory(saved.Workers[0]).Any(i=>i.Id==tool.Id&&i.Item==id&&i.Slot=="main"),"save equipped "+id);
            Check(saved.Workers[0].Person.GearBonuses.OrderBy(p=>p.Key).SequenceEqual(worker.Person.GearBonuses.OrderBy(p=>p.Key)),"cached bonuses restored");
            world.ActResident(new(worker.Id,"unequip",ItemId:tool.Id));
            Check(world.EquippedWeapon(worker).Id=="unarmed"&&worker.Person.GearBonuses.Count==0,"unequip clears tool effect");
            world.ActResident(new(worker.Id,"equip",ItemId:tool.Id));
            var enemy=new DemoWorker{Id="tool-target",Name="目标",X=60,Z=61};world.Enemies.Add(enemy);
            Check(world.BeginMelee(worker,enemy),"attack begins "+id);
            for(int i=0;i<60;i++)world.AdvanceMelee(1d/30);
            Check(enemy.Person.Injuries.Count>0,"visible tool actually hits: "+id);
        }
        var natural=new DemoWorld();var actor=natural.Workers[0];Equip(natural,actor,"shovel");
        var soil=FarmingChecks.SurfacePlot(natural)+new Cell(0,0,-1);
        Check(natural.GearWorkBonus(actor,new(){Dig=true,Cell=soil})==.15,"shovel speeds actual natural soil: "+natural.BlockAt(soil));

        var production=new DemoWorld(flat:true);production.Items.Clear();production.Stockpiles.Clear();
        foreach(var resident in production.Workers)resident.Person.Schedule=ResidentWork.FreeDay();
        var station=new Cell(5,9);production.Buildings[station]=new(station,BuildKind.Anvil);
        production.SpawnItem("iron",9,new(2,10));production.SpawnItem("wood",8,new(2,10));
        foreach(var id in Items)production.SetProductionTarget(id,1);
        for(int i=0;i<1800*30&&!Items.All(id=>production.Total(id)==1);i++)production.Update(1d/30);
        Check(Items.All(id=>production.Total(id)==1),"all five tools manufactured");
        Check(production.Total("iron")==0&&production.Total("wood")==0,"exact recipe material consumption");
        var loaded=DemoWorld.LoadJson(production.SaveJson());
        Check(loaded.ProductionOrders.Count==5&&Items.All(id=>loaded.Total(id)==1),"production archive");
        Console.WriteLine("TOOL_EQUIPMENT_OK: manufacturing, materials, equip/unequip, two hands, scoped bonuses, real melee and persistence");
    }
}

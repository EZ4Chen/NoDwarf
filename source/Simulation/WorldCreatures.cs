namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    private CreaturePopulation? _creatures;
    public CreaturePopulation Creatures=>_creatures??=new(Content.Creatures,new CreatureWorldEnvironment(this));
    public void ConfigureCreatures(CreatureCatalog catalog,CreatureBehaviorRegistry? behaviors=null)
    {
        if(_creatures?.Count>0)throw new InvalidOperationException("已有生物时不能替换定义，需显式迁移。");
        var population=new CreaturePopulation(catalog,new CreatureWorldEnvironment(this),behaviors);
        var residents=Workers.Concat(Enemies).Concat(_removedResidents??new()).ToArray();
        // Validate the complete binding before changing any actor or scheduler.
        foreach(var actor in residents)
        {
            var source=catalog.Species.ContainsKey(actor.Person.SpeciesId)?catalog:Content.Creatures;
            if(!source.Species.TryGetValue(actor.Person.SpeciesId,out var definition)||definition.Controller!="resident")throw new InvalidDataException("居民物种控制器不兼容："+actor.Person.SpeciesId);
        }
        foreach(var actor in residents)actor.Person.BindSpeciesCatalog(catalog.Species.ContainsKey(actor.Person.SpeciesId)?catalog:Content.Creatures);
        _creatures=population;
    }
    public long SpawnCreature(string species,Cell cell,uint seed=1)
    {long id=Creatures.Spawn(species,cell,SimulationTime,seed);Revision++;return id;}
    public bool DamageCreature(long id,string part,double amount)
    {bool changed=Creatures.Damage(id,part,amount,SimulationTime);if(changed)Revision++;return changed;}
    private sealed class CreatureWorldEnvironment(DemoWorld world):ICreatureEnvironment
    {
        public bool CanStand(Cell cell,CreatureMovementDefinition movement)=>world.Walkable(cell)&&
            (movement.Ladders||!world.LadderAt(cell)&&!world.LadderAt(cell+new Cell(0,0,-1)))&&
            world.Fluids.Amount(cell)<=movement.MaxWater*1000;
        public bool CanTraverse(Cell from,Cell to,CreatureMovementDefinition movement)=>CanStand(from,movement)&&CanStand(to,movement)&&
            to.Level-from.Level<=movement.StepUp&&from.Level-to.Level<=movement.DropDown&&
            (movement.Ladders||!world.LadderLink(from,to))&&world.Connected(from,to);
    }
}

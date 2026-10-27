namespace NoDwarf.Simulation;

public enum GroundKind { Grass, Soil, Rock, Iron, Coal, Sand, Humus, DryGrass, Gravel, Granite, Limestone, Sandstone }
public readonly record struct Ground(int Level, GroundKind Kind, int RampX = 0, int RampZ = 0, bool Tree = false)
{
    public string BlockId => Ramp ? BlockCatalog.RampId(Kind) : BlockCatalog.Id(Kind);
    public string? FeatureId => Tree ? "nodwarf:tree" : null;
    public bool Ramp => RampX != 0 || RampZ != 0;
}

// Generation is pure data. A surface lookup is an index, not the identity of a world cell.
public sealed class TerrainMap
{
    public const int Size = 255, VerticalSize = 192, MinLevel = -64, MaxLevel = MinLevel + VerticalSize - 1;
    public const int DeepLayers = 64, NormalMinLevel = MinLevel + DeepLayers, SeaLevel = 64, MaxTerrainHeight = SeaLevel + 24;
    public static bool InBounds(Cell c) => c.X >= 0 && c.X < Size && c.Z >= 0 && c.Z < Size && c.Level >= MinLevel && c.Level <= MaxLevel;
    public static bool IsDeep(Cell c) => c.Level < NormalMinLevel;
    private readonly Dictionary<Cell, GroundKind> _ores = new();
    public IReadOnlyDictionary<Cell, GroundKind> Ores => _ores;
    private readonly HashSet<Cell> _excavated = new();
    private readonly List<Cell> _changes = new();
    public IEnumerable<Cell> ChangesSince(int revision) => _changes.Skip(Math.Max(0,revision));
    public int Revision { get; private set; }
    public event Action<Cell>? Changed;
    public IReadOnlyCollection<Cell> Excavated => _excavated;
    public bool Solid(Cell c) => DemoWorld.Inside(c) && c.Level < this[c.X,c.Z].Level && !_excavated.Contains(c);
    public bool Ramp(Cell c) => Contains(c) && this[c.X,c.Z].Ramp && !_excavated.Contains(c);
    public string BlockAt(Cell c)
    {
        if(!InBounds(c))return "nodwarf:air";
        if(Ramp(c))return this[c.X,c.Z].BlockId;
        if(!Solid(c))return "nodwarf:air";
        if(c.Level==MinLevel)return "nodwarf:bedrock";
        if(_ores.TryGetValue(c,out var ore))return OreId(c,ore);
        if(IsDeep(c))return "nodwarf:deepslate";
        var ground=this[c.X,c.Z];
        if(c.Level==ground.Level-1)return BlockCatalog.Id(ground.Kind);
        if(c.Level>=ground.Level-3 && ground.Kind is GroundKind.Grass or GroundKind.Soil or GroundKind.Sand)
            return ground.Kind==GroundKind.Sand ? "nodwarf:sand" : "nodwarf:soil";
        if(Region is not null)
        {
            if(c.Level>=ground.Level-3&&ground.Kind is GroundKind.Humus or GroundKind.DryGrass)return "nodwarf:soil";
            // Broad rock beds, continuous across neighboring columns; ores override the host.
            double bed=Noise(c.X/65d+103,c.Z/65d+17)*8;
            return c.Level<24+bed?"nodwarf:granite":"nodwarf:"+Region.Rock;
        }
        return "nodwarf:stone";
    }
    public static string OreId(Cell c,GroundKind ore) => IsDeep(c)
        ? (ore==GroundKind.Iron ? "nodwarf:deepslate_iron_ore" : "nodwarf:deepslate_coal_ore") : BlockCatalog.Id(ore);
    public bool Excavate(Cell c)
    { if(c.Level<=MinLevel || (!Solid(c)&&!Ramp(c))) return false; _excavated.Add(c);_changes.Add(c);Revision++;Changed?.Invoke(c);return true; }
    public void SetTree(Cell c,bool exists)
    {
        if(!Contains(c))throw new ArgumentOutOfRangeException(nameof(c));
        _surface[c.X,c.Z]=_surface[c.X,c.Z] with {Tree=exists};
        _changes.Add(c);Revision++;
        for(int y=0;y<14&&c.Level+y<=MaxLevel;y++)Changed?.Invoke(c+new Cell(0,0,y));
    }
    public const float LayerHeight = WorldScale.BlockMeters;
    private readonly Ground[,] _surface = new Ground[Size, Size];
    public int Seed { get; }
    public bool Flat { get; }
    public RegionProfile? Region {get;}
    public WorldGenerationSettings? Generation {get;}
    public int EcologyVersion {get;}
    public int GeneratorVersion => Generation is not null?3:Region is null ? 1 : 2;
    public Ground this[int x, int z] => _surface[x, z];
    public Cell At(int x, int z) => new(x, z, _surface[x, z].Level);
    public TerrainMap(int seed, bool flat = false, string? region = null,WorldGenerationSettings? generation=null,int ecologyVersion=2)
    {
        Seed = seed; Flat = flat;
        EcologyVersion=region is null?1:generation?.EcologyVersion??ecologyVersion;
        if(EcologyVersion is not (1 or 2))throw new ArgumentException("不支持的生态生成版本。");
        Region=region is null?null:RegionProfile.Get(region,EcologyVersion);
        Generation=generation;
        if(generation is not null)
        {
            generation.Validate();
            if(flat||seed!=generation.LocalSeed||region!=generation.Site.Region)throw new ArgumentException("世界参数与落脚地区不一致。");
            Region=generation.Profile();
        }
        if(flat&&Region is not null)throw new ArgumentException("平坦测试图不使用地区生成器。");
        for (int x = 0; x < Size; x++)
        for (int z = 0; z < Size; z++)
        {
            double n = Noise(x / 24.0, z / 24.0) * 0.72 + Noise(x / 9.0 + 37, z / 9.0 + 51) * 0.28;
            int level = flat ? 0 : SeaLevel + Math.Clamp((int)Math.Floor(n * 9 + Math.Pow(Noise(x / 70.0 + 19,z / 70.0 + 61),2)*18 - 3), 0, MaxTerrainHeight-SeaLevel);
            double distance = Math.Max(Math.Abs(x - Size / 2), Math.Abs(z - Size / 2));
            if (!flat && distance < 48) level = (int)Math.Round(SeaLevel + 2 + (level - SeaLevel - 2) * Math.Clamp((distance - 16) / 32, 0, 1));
            double r = Hash(x + 381, z + 129);
            var kind = level >= SeaLevel + 10 ? GroundKind.Rock : r < .13 ? GroundKind.Soil : GroundKind.Grass;
            if (level <= SeaLevel + 2 && Noise(x / 12.0 + 79, z / 12.0 + 82) > .57) kind = GroundKind.Sand;
            bool clearing = Math.Abs(x - Size / 2) <= 11 && Math.Abs(z - Size / 2) <= 11;
            bool tree = !flat && !clearing && kind != GroundKind.Sand && level < SeaLevel + 10 && r > .82;
            if(Region is not null)
            {
                double broad=Noise(x/75d+19,z/75d+61), detail=Noise(x/17d+37,z/17d+51);
                level=Region.Id switch {
                    "temperate" or "grassland" or "wetland"=>65+(int)Math.Round(broad*8+detail*3),
                    "boreal"=>67+(int)Math.Round(broad*22+detail*7),
                    "savanna" or "arid" or "rainforest" or "tundra"=>66+(int)Math.Round(broad*10+detail*2),
                    "desert"=>65+(int)Math.Round(broad*10+Math.Pow(detail,2)*7),
                    _=>74+(int)Math.Round(Math.Pow(broad,.65)*43+detail*5)};
                if(Generation is not null)level=Math.Clamp(level+Region.CampHeight-RegionProfile.Get(Region.Id,EcologyVersion).CampHeight,60,112);
                if(distance<48)level=(int)Math.Round(Region.CampHeight+(level-Region.CampHeight)*Math.Clamp((distance-16)/32,0,1));
                double patch=Noise(x/13d+7,z/13d+91);
                kind=Region.Id switch {
                    "temperate"=>patch>.62?GroundKind.Humus:GroundKind.Grass,
                    "boreal"=>patch>.65?GroundKind.Granite:GroundKind.Humus,
                    "savanna"=>patch>.68?GroundKind.Sand:GroundKind.DryGrass,
                    "grassland"=>patch>.78?GroundKind.Soil:GroundKind.Grass,
                    "desert"=>patch>.82?GroundKind.Sandstone:GroundKind.Sand,
                    "arid"=>patch>.52?GroundKind.Sand:GroundKind.DryGrass,
                    "rainforest" or "wetland"=>patch>.58?GroundKind.Humus:GroundKind.Grass,
                    "tundra"=>patch>.58?GroundKind.Gravel:GroundKind.Grass,
                    _=>level>=104?GroundKind.Granite:patch>.48?GroundKind.Gravel:GroundKind.Grass};
                if(Generation is not null&&kind is GroundKind.Grass or GroundKind.Humus)
                    kind=Generation.Site.Wetness<.3?GroundKind.DryGrass:Generation.Site.Wetness>.7?GroundKind.Humus:kind;
                tree=!clearing&&level<=112&&kind is (GroundKind.Grass or GroundKind.Humus or GroundKind.DryGrass)&&r>1-Region.TreeChance;
            }
            _surface[x, z] = new Ground(level, flat ? GroundKind.Grass : kind, Tree: tree);
        }
        if (flat) return;
        GenerateVeins();
        // A subset of terrace edges become explicit ramps. Other edges remain cliffs.
        var landings = new HashSet<(int, int)>();
        for (int x = 1; x < Size - 1; x++)
        for (int z = 1; z < Size - 1; z++)
        {
            var g = _surface[x, z];
            if (landings.Contains((x, z)) || g.Tree || Hash(x + 77, z + 93) < .38) continue;
            foreach (var (dx, dz) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                if (_surface[x + dx, z + dz].Level == g.Level + 1 && !_surface[x + dx, z + dz].Tree && !_surface[x + dx, z + dz].Ramp)
                { _surface[x, z] = g with { RampX = dx, RampZ = dz, Kind = g.Kind is GroundKind.Grass ? GroundKind.Soil : g.Kind }; landings.Add((x + dx, z + dz)); break; }
        }
        // Deterministic local minima thin the original candidates without scan-direction bias.
        var candidates=new bool[Size,Size];for(int x=0;x<Size;x++)for(int z=0;z<Size;z++)candidates[x,z]=_surface[x,z].Tree;
        for(int x=0;x<Size;x++)for(int z=0;z<Size;z++)if(candidates[x,z])
        {
            bool keep=true;double rank=Hash(x+541,z+197);
            for(int dx=-3;dx<=3&&keep;dx++)for(int dz=-3;dz<=3;dz++)
            {
                int nx=x+dx,nz=z+dz;if(nx<0||nz<0||nx>=Size||nz>=Size||(dx==0&&dz==0)||!candidates[nx,nz])continue;
                double other=Hash(nx+541,nz+197);
                if(other<rank||other==rank&&(nx<x||nx==x&&nz<z)){keep=false;break;}
            }
            if(!keep)_surface[x,z]=_surface[x,z] with {Tree=false};
        }
    }
    private void GenerateVeins()
    {
        // Connected six-neighbor random walks, with a persistent direction and thick nodes.
        // Ore identity is independent of excavation and camera visibility.
        var random = new Random(unchecked(Seed * 397 ^ 0x51a7));
        var directions = new[] { new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1),new Cell(0,0,1),new Cell(0,0,-1) };
        int count = Size * Size / 90;
        for(int vein=0;vein<count;vein++)
        {
            int x=random.Next(3,Size-3),z=random.Next(3,Size-3);
            bool deep=vein%3==0;
            var kind=deep ? (random.Next(5)==0 ? GroundKind.Coal : GroundKind.Iron)
                : (random.Next(2)==0 ? GroundKind.Coal : GroundKind.Iron);
            int lower=deep ? MinLevel+2 : kind==GroundKind.Coal ? SeaLevel-28 : NormalMinLevel;
            int upper=deep ? NormalMinLevel-1 : this[x,z].Level-3;
            int y=random.Next(lower,upper+1);
            var cell=new Cell(x,z,y);
            var direction=directions[random.Next(directions.Length)];
            int length=random.Next(18,55);
            void Place(Cell c)
            {
                if(DemoWorld.Inside(c)&&c.Level>=lower&&c.Level<=upper&&c.Level<this[c.X,c.Z].Level-3)
                    _ores.TryAdd(c,kind);
            }
            for(int step=0;step<length;step++)
            {
                Place(cell);
                foreach(var d in directions) Place(cell+d);
                if(random.NextDouble()<.3)direction=directions[random.Next(directions.Length)];
                var next=cell+direction;
                if(next.X<2||next.X>=Size-2||next.Z<2||next.Z>=Size-2||next.Level<lower||next.Level>upper||next.Level>=this[next.X,next.Z].Level-3)
                    direction=directions[random.Next(directions.Length)];
                else cell=next;
            }
        }
        // Intersecting veins and near-surface clipping must not leave isolated ore specks.
        foreach(var p in _ores.Where(p=>!directions.Any(d=>_ores.TryGetValue(p.Key+d,out var k)&&k==p.Value)).ToArray())
            _ores.Remove(p.Key);
    }
    public bool Contains(Cell c) => InBounds(c) && _surface[c.X, c.Z].Level == c.Level;
    public bool Connected(Cell a, Cell b)
    {
        if (!Contains(a) || !Contains(b) || Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z) != 1) return false;
        var ga = this[a.X, a.Z]; var gb = this[b.X, b.Z];
        if (a.Level == b.Level)
        {
            int dx = b.X - a.X, dz = b.Z - a.Z;
            return Math.Abs(TileHeight(a.X, a.Z, dx * .5, dz * .5) - TileHeight(b.X, b.Z, -dx * .5, -dz * .5)) < .001f;
        }
        var low = a.Level < b.Level ? a : b; var high = a.Level < b.Level ? b : a;
        var g = this[low.X, low.Z];
        int dxHigh = high.X - low.X, dzHigh = high.Z - low.Z;
        return high.Level == low.Level + 1 && g.RampX == dxHigh && g.RampZ == dzHigh &&
            Math.Abs(TileHeight(low.X, low.Z, dxHigh * .5, dzHigh * .5) - TileHeight(high.X, high.Z, -dxHigh * .5, -dzHigh * .5)) < .001f;
    }
    public float Height(double x, double z)
    {
        int ix = Math.Clamp((int)Math.Floor(x + .5), 0, Size - 1), iz = Math.Clamp((int)Math.Floor(z + .5), 0, Size - 1);
        return TileHeight(ix, iz, x - ix, z - iz);
    }
    public float TileHeight(int x, int z, double localX, double localZ)
    {
        var g = this[x, z];
        return (float)(g.Level + (g.Ramp ? Math.Clamp(.5 + localX * g.RampX + localZ * g.RampZ, 0, 1) : 0)) * LayerHeight;
    }
    private double Noise(double x, double z)
    {
        int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z); double tx = x - ix, tz = z - iz;
        tx = tx * tx * (3 - 2 * tx); tz = tz * tz * (3 - 2 * tz);
        return (Hash(ix, iz) * (1 - tx) + Hash(ix + 1, iz) * tx) * (1 - tz) + (Hash(ix, iz + 1) * (1 - tx) + Hash(ix + 1, iz + 1) * tx) * tz;
    }
    public double Hash(int x, int z)
    {
        unchecked { uint h = (uint)(x * 374761393 + z * 668265263 + Seed * 1274126177); h = (h ^ (h >> 13)) * 1274126177; return (h ^ (h >> 16)) / (double)uint.MaxValue; }
    }
}

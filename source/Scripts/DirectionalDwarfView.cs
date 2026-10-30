using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

/// <summary>Five modular flat pawn bodies; action-only hands and camera-relative four-way facing.</summary>
public partial class DirectionalDwarfView : Node3D
{
    // Authoring coordinates are 400x560; packed textures are half resolution.
    public const float Pixel=WorldScale.DwarfHeightMeters/480f;
    public const string Root="res://Assets/Characters/FlatPawn/";
    private const string HeldRoot="res://Assets/Characters/Sprites4/";
    private static readonly Dictionary<string,Texture2D> Textures=new();
    [Flags] private enum Clothes {None=0,Shirt=1,Trousers=2,Armor=4,Helmet=8,Cap=16}
    private readonly record struct WardrobeKey(CharacterAppearance Appearance,int Direction,Clothes Worn);
    private readonly record struct PreviewKey(WardrobeKey Body,bool Mirrored);
    // Source appearances are immutable. Weak ownership keeps old editor drafts and
    // unloaded worlds collectable; each live appearance resolves a variant only once.
    private sealed class VisualVariants(CharacterAppearance source)
    {
        public readonly CharacterAppearance Appearance=source.Normalize();
        public readonly Dictionary<(int Direction,Clothes Worn),WardrobeKey> Keys=new();
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CharacterAppearance,VisualVariants> VisualKeys=new();
    private static readonly NoDwarf.Collections.ByteBudgetCache<WardrobeKey,ImageTexture> Wardrobes=PawnTextureCache.Create<WardrobeKey>("performance/cache/pawn_body_mib",128);
    private static readonly NoDwarf.Collections.ByteBudgetCache<string,ImageTexture> Hands=PawnTextureCache.Create<string>("performance/cache/pawn_hands_mib",8);
    private static readonly NoDwarf.Collections.ByteBudgetCache<PreviewKey,ImageTexture> Previews=PawnTextureCache.Create<PreviewKey>("performance/cache/pawn_portraits_mib",128,shared:true);
    private WardrobeKey? _bodyKey;
    private readonly Dictionary<string,string> _palmKeys=new();
    private readonly Dictionary<string,Node3D> _hands=new();
    private readonly Dictionary<string,string> _equipment=new();
    private CharacterAppearance _appearance=new();
    private CharacterAppearance? _sourceAppearance;
    private Node3D _paper=null!;
    private Sprite3D _body=null!;
    private Vector3 _facing=Vector3.Back;
    private int _direction=-1;
    private Color _tint=Colors.White;
    public int Direction=>_direction;
    public string Sex=>_appearance.BodySex;
    public bool Wears(string item)=>_equipment.Values.Contains(item);
    public static Texture2D Atlas(string name)
    {if(!Textures.TryGetValue(name,out var t))Textures[name]=t=GD.Load<Texture2D>(Root+name+".png");return t;}
    public static AtlasTexture Frame(string name,int direction)=>new(){Atlas=Atlas(name),Region=new Rect2((direction==3?1:direction)*200,0,200,280)};
    private static Sprite3D Sprite()=>new(){Shaded=false,DoubleSided=true,TextureFilter=BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,AlphaCut=SpriteBase3D.AlphaCutMode.Discard,AlphaScissorThreshold=.35f};
    private static Vector3 P(float x,float y,float z=0)=>new(x*Pixel,y*Pixel,z);
    public void Initialize()
    {
        _paper=new Node3D{Name="Paper"};AddChild(_paper);
        _body=Sprite();_body.Name="body";_body.PixelSize=Pixel*2;_paper.AddChild(_body);
        foreach(var slot in new[]{"main","off"})
        {
            var hand=new Node3D{Name=slot};_paper.AddChild(hand);_hands[slot]=hand;
            var held=Sprite();held.Name="Held";hand.AddChild(held);
            var palm=Sprite();palm.Name="Palm";palm.Position=new(0,0,.004f);hand.AddChild(palm);
            hand.Visible=false;
        }
        Refresh();
    }
    public void SetAppearance(CharacterAppearance appearance)
    {
        if(appearance==_sourceAppearance)return;
        _sourceAppearance=appearance;var a=appearance.Normalize();if(a==_appearance)return;_appearance=a;Refresh();
    }
    public void SetEquipment(IEnumerable<ItemStack> items)
    {
        var next=items.Where(i=>i.Slot!="").GroupBy(i=>i.Slot).ToDictionary(g=>g.Key,g=>g.Last().Item);
        if(next.Count==_equipment.Count&&next.All(p=>_equipment.GetValueOrDefault(p.Key)==p.Value))return;
        _equipment.Clear();foreach(var pair in next)_equipment[pair.Key]=pair.Value;Refresh();
    }
    private void Refresh()
    {
        if(_paper is null||!IsInsideTree())return;
        int d=Math.Max(0,_direction),frame=d==3?1:d;bool flip=d==3;
        var bodyKey=VisualKey(_appearance,_equipment.Values,frame);
        if(_bodyKey!=bodyKey)
        {
            _body.Texture=AcquireBody(bodyKey);
            var oldBodyKey=_bodyKey;_bodyKey=bodyKey;if(oldBodyKey is {} old)Wardrobes.Release(old);
        }
        _body.FlipH=flip;
        foreach(var slot in _hands.Keys)
        {
            bool main=slot=="main";var hand=_hands[slot];var palm=hand.GetNode<Sprite3D>("Palm");
            if(!Wears("gloves"))
            {
                string key=$"{frame}|{PawnColors.Skin(_appearance.Skin).ToHtml(false)}";
                if(_palmKeys.GetValueOrDefault(slot)!=key)
                {
                    palm.Texture=Hands.Acquire(key,()=>
                    {
                        using var pixels=Atlas($"hand-bare-{frame}").GetImage();
                        using var recolored=PawnColors.Recolor(pixels,PawnColors.Skin(_appearance.Skin),"skin");
                        return (ImageTexture.CreateFromImage(recolored),PawnTextureCache.RgbaBytes(recolored));
                    });
                    if(_palmKeys.Remove(slot,out var oldKey))Hands.Release(oldKey);
                    _palmKeys[slot]=key;
                }
            }
            else
            {
                palm.Texture=Atlas($"hand-glove-{frame}");
                if(_palmKeys.Remove(slot,out var oldKey))Hands.Release(oldKey);
            }
            palm.PixelSize=(Wears("gloves")?48:42)*Pixel/palm.Texture.GetHeight();
            // Front right hand is screen-left; rear reverses the anatomical ordering.
            palm.FlipH=frame==1?flip:(!main)^(frame==2);
            var held=hand.GetNode<Sprite3D>("Held");string item=_equipment.GetValueOrDefault(slot,"");
            string path=HeldRoot+item+"-held.png";
            held.Texture=item==""?null:ResourceLoader.Exists(path)?GD.Load<Texture2D>(path):EquipmentArt.Icon(item);
            held.Visible=held.Texture is not null;held.FlipH=flip;
            if(held.Texture is {} tex)
            {float height=item=="shield"?.55f:item=="sword"?.85f:item=="pickaxe"?360*Pixel:.72f;held.PixelSize=height/tex.GetHeight();held.Position=new(0,item=="shield"?0:height*.32f,0);}
        }
        Tint(_tint);
    }
    public override void _EnterTree(){if(_paper is not null)Refresh();}
    public override void _ExitTree()
    {
        if(_bodyKey is {} key){_body.Texture=null;Wardrobes.Release(key);_bodyKey=null;}
        foreach(var pair in _palmKeys){_hands[pair.Key].GetNode<Sprite3D>("Palm").Texture=null;Hands.Release(pair.Value);}
        _palmKeys.Clear();
    }
    public void Tint(Color color)
    {
        _tint=color;if(_body is null)return;_body.Modulate=color;
        foreach(var hand in _hands.Values)
        {hand.GetNode<Sprite3D>("Held").Modulate=color;hand.GetNode<Sprite3D>("Palm").Modulate=color;}
    }
    public static int SelectDirection(Vector3 facing,Basis camera)
    {
        Vector3 right=camera.X;right.Y=0;right=right.Normalized();Vector3 toward=camera.Z;toward.Y=0;toward=toward.Normalized();
        float x=facing.Dot(right),y=facing.Dot(toward);return Math.Abs(x)>Math.Abs(y)?x>0?1:3:y>=0?0:2;
    }
    public void Sample(double time,string state,Vector3? facing,double actionTime=0)
    {
        if(facing is {} v&&v.LengthSquared()>.0001f)_facing=v.Normalized();
        var camera=GetViewport().GetCamera3D();if(camera is null)return;
        GlobalBasis=camera.GlobalBasis.Orthonormalized();int d=SelectDirection(_facing,camera.GlobalBasis);
        if(d!=_direction){_direction=d;Refresh();}
        bool side=d is 1 or 3,walk=state is "walk" or "carry",mine=state=="work"&&_equipment.GetValueOrDefault("main")=="pickaxe";
        float sign=d is 2 or 3?-1:1,phase=(float)time*Mathf.Tau*1.35f,stride=walk?Mathf.Sin(phase):0;
        _paper.Position=Vector3.Zero;_paper.Rotation=Vector3.Zero;
        _body.Position=P(stride*5,240+(walk?Math.Abs(Mathf.Cos(phase))*8:Mathf.Sin((float)time*2.4f)*1.6f));
        _body.Rotation=new(0,0,-stride*.028f);_body.Scale=Vector3.One;
        if(state=="down"){_paper.Rotation=new(0,0,Mathf.Pi/2);_paper.Position=new(.55f,.12f,0);}
        float attack=state=="attack"?Mathf.Sin(Mathf.Clamp((float)actionTime/.65f,0,1)*Mathf.Pi):0;
        foreach(var slot in _hands.Keys)
        {
            bool main=slot=="main",near=side?(d==1?main:!main):d!=2;float alt=main?1:-1,s=stride*alt;
            var hand=_hands[slot];
            hand.Visible=state is "work" or "attack" or "block_hold" or "parry" or "carry";
            float width=_appearance.Body switch{"thin"=>106,"female"=>119,"hulk"=>153,"fat"=>151,_=>135};
            float hx=side?60*sign:-alt*width*(d==2?-1:1);
            hand.Position=P(hx+(side?s*26*sign:s*6),226+s*26,near?.025f:-.025f);
            hand.Rotation=new(0,0,s*.18f);var palm=hand.GetNode<Sprite3D>("Palm");palm.Position=new(0,0,_equipment.GetValueOrDefault(slot)=="shield"&&d!=2?-.004f:.004f);
            hand.GetNode<Sprite3D>("Held").Visible=hand.GetNode<Sprite3D>("Held").Texture is not null;
            if(!main&&state is "block_hold" or "parry")hand.Position+=P(side?28*sign:-sign*25,70);
            if(main&&state=="attack")
            {hand.Rotation=new(0,0,-(-.7f+attack*1.8f)*sign);hand.Position+=P(attack*30*sign,attack*20);}
            if(main&&state=="work"&&!mine){hand.Rotation=new(0,0,-(-.4f+Mathf.Sin((float)time*5f)*.8f)*sign);hand.Position+=P(18*sign,25);}
            if(state=="carry")hand.Position+=P(side?25*sign:0,35);
        }
        if(mine)SampleMining(time,sign,side);
    }
    private void SampleMining(double time,float sign,bool side)
    {
        float cycle=(float)(time%1.45/1.45);
        ReadOnlySpan<float> keys=[0,.30f,.46f,.62f,.70f,1];
        ReadOnlySpan<Vector2> poses=[new(122,224),new(174,356),new(182,370),new(164,276),new(164,276),new(122,224)];
        ReadOnlySpan<float> angles=[22,-27,-35,108,108,22];
        int index=0;while(index<4&&cycle>keys[index+1])index++;
        float u=Mathf.InverseLerp(keys[index],keys[index+1],cycle);u=index==2?u*u:Mathf.SmoothStep(0,1,u);
        Vector2 pos=poses[index].Lerp(poses[index+1],u);float angle=Mathf.Lerp(angles[index],angles[index+1],u);
        var main=_hands["main"];var off=_hands["off"];
        main.Position=P(pos.X*sign,pos.Y,_direction==2?-.04f:.04f);main.Rotation=new(0,0,-Mathf.DegToRad(angle)*sign);
        // Both palms share the tool transform; the right palm is at the upper shaft grip.
        main.GetNode<Sprite3D>("Palm").Position=P(0,104,.006f);
        off.Transform=main.Transform;off.Position+=new Vector3(0,0,.008f);off.GetNode<Sprite3D>("Held").Visible=false;
        float forward=Mathf.Clamp((angle+35)/143,0,1);
        _body.Position+=P(Mathf.Lerp(-10,22,forward)*sign,Mathf.Lerp(8,-10,forward));
        _body.Rotation=new(0,0,-Mathf.Lerp(-.035f,.055f,forward)*sign);_body.Scale=new(1+forward*.018f,1-forward*.025f,1);
    }
    private static string Body(CharacterAppearance a)=>a.Normalize().Body;
    private static string Head(CharacterAppearance a)=>a.Face switch{"square"=>"square","oval"=>"lean",_=>"round"};
    private static string Hair(CharacterAppearance a)=>a.Hair switch{"bald"=>"none","braid"=>"braids","bob"=>"waves","sweep"=>"swept",_=>a.Hair};
    private static WardrobeKey VisualKey(CharacterAppearance appearance,IEnumerable<string> items,int direction)
    {
        if(direction is <0 or >3)throw new ArgumentOutOfRangeException(nameof(direction));
        int d=direction==3?1:direction;var variants=VisualKeys.GetValue(appearance,static a=>new(a));var a=variants.Appearance;
        Clothes worn=Clothes.None;
        foreach(var item in items)worn|=item switch {"shirt"=>Clothes.Shirt,"trousers"=>Clothes.Trousers,"armor"=>Clothes.Armor,"helmet"=>Clothes.Helmet,"cap"=>Clothes.Cap,_=>Clothes.None};
        bool armor=(worn&Clothes.Armor)!=0,shirt=(worn&Clothes.Shirt)!=0;
        if(armor)worn&=~(Clothes.Shirt|Clothes.Trousers);
        else if(shirt)worn&=~Clothes.Trousers;
        if((worn&Clothes.Helmet)!=0)worn&=~Clothes.Cap;
        if(variants.Keys.TryGetValue((d,worn),out var cached))return cached;
        bool hair=a.Hair!="bald"&&(worn&(Clothes.Helmet|Clothes.Cap))==0;
        bool beard=a.Beard!="none"&&d!=2;
        // Keep only inputs that Compose actually draws. Hidden choices stay in
        // _appearance/the save, and become visible when the occluding gear is removed.
        a=a with
        {
            Cloth="sage",BodySex=!armor&&!shirt&&a.Body!="female"?a.BodySex:"male",
            Outfit=!armor&&shirt&&a.Outfit=="apron"?"apron":"tunic",
            Skin="#"+PawnColors.Skin(a.Skin).ToHtml(false),
            Hair=hair?a.Hair:"bald",HairColor=hair?"#"+PawnColors.Hair(a.HairColor).ToHtml(false):"chestnut",
            Beard=beard?a.Beard:"none",BeardColor=beard?"#"+PawnColors.Hair(a.BeardColor).ToHtml(false):"silver"
        };
        var key=new WardrobeKey(a,d,worn);variants.Keys.Add((d,worn),key);return key;
    }
    private static ImageTexture AcquireBody(WardrobeKey key)=>Wardrobes.TryAcquire(key,out var texture)?texture:CreateComposite(key);
    private static ImageTexture Composite(CharacterAppearance a,IEnumerable<string> items,int d,out WardrobeKey key)
    {key=VisualKey(a,items,d);return AcquireBody(key);}
    private static ImageTexture CreateComposite(WardrobeKey key)=>Wardrobes.Acquire(key,()=>Compose(key.Appearance,key.Worn,key.Direction));
    private static (ImageTexture,long) Compose(CharacterAppearance a,Clothes worn,int d)
    {
        bool Wears(Clothes garment)=>(worn&garment)!=0;
        using var image=Image.CreateEmpty(200,280,false,Image.Format.Rgba8);
        void Layer(string name,Color? tint=null,string material="hair")
        {
            using var atlas=Atlas(name).GetImage();
            using var src=atlas.GetRegion(new Rect2I(d*200,0,200,280));src.Convert(Image.Format.Rgba8);
            if(tint is {} c){using var recolored=PawnColors.Recolor(src,c,material);image.BlendRect(recolored,new Rect2I(0,0,200,280),Vector2I.Zero);}
            else image.BlendRect(src,new Rect2I(0,0,200,280),Vector2I.Zero);
        }
        string body=Body(a);
        // These opaque garments replace the torso surface. Drawing a differently
        // shaped naked base underneath would leave a skin fringe at its edges.
        if(Wears(Clothes.Armor))Layer("armor-"+body);
        else if(Wears(Clothes.Shirt))Layer((a.Outfit=="apron"?"apron-":"work-")+body);
        else
        {
            Layer("body-"+body,PawnColors.Skin(a.Skin),"skin");
            if(a.BodySex=="female"&&body!="female")Layer("underwear-"+body);
            if(Wears(Clothes.Trousers))Layer("trousers-"+body);
        }
        Layer("head-"+Head(a),PawnColors.Skin(a.Skin),"skin");
        if(Hair(a)!="none"&&!Wears(Clothes.Helmet)&&!Wears(Clothes.Cap))
        {
            Layer("roots-"+Head(a),PawnColors.Hair(a.HairColor));
            Layer("hair-"+Hair(a),PawnColors.Hair(a.HairColor));
        }
        if(a.Beard!="none"&&d!=2)Layer("beard-"+(a.Beard=="full"?"full":"short"),PawnColors.Hair(a.BeardColor),"beard");
        if(Wears(Clothes.Helmet))Layer("helmet");else if(Wears(Clothes.Cap))Layer("cap");
        image.GenerateMipmaps();var result=ImageTexture.CreateFromImage(image);
        return (result,PawnTextureCache.RgbaBytes(image));
    }
    internal static ulong CompositeIdentity(CharacterAppearance a,IEnumerable<string> items,int direction)
    {
        var texture=Composite(a,items,direction,out var key);
        try{return texture.GetInstanceId();}finally{Wardrobes.Release(key);}
    }
    internal static object CacheStatistics=>new {bodyEntries=Wardrobes.Count,bodyBytes=Wardrobes.Bytes,bodyBudget=Wardrobes.Budget,pinnedBodyBytes=Wardrobes.PinnedBytes,hits=Wardrobes.Hits,misses=Wardrobes.Misses,evictions=Wardrobes.Evictions,handBytes=Hands.Bytes,portraitEntries=Previews.Count,portraitBytes=Previews.Bytes,portraitHits=Previews.Hits,portraitMisses=Previews.Misses,portraitEvictions=Previews.Evictions};
    internal static void CheckPinnedTextures(Node parent)
    {
        long bodyBudget=Wardrobes.Budget,handBudget=Hands.Budget;
        long pinnedBody=Wardrobes.PinnedBytes,pinnedHands=Hands.PinnedBytes;var views=new List<DirectionalDwarfView>();
        try
        {
            Wardrobes.Budget=0;Hands.Budget=0;
            for(int i=0;i<8;i++)
            {
                var view=new DirectionalDwarfView();parent.AddChild(view);view.Initialize();views.Add(view);
                view.SetAppearance(new CharacterAppearance{Skin=$"#80{90+i:x2}60"});
            }
            foreach(var view in views)
            {
                using var image=view._body.Texture.GetImage();
                if(image.GetWidth()!=200)throw new Exception("Pinned body texture lost under pressure");
                foreach(var hand in view._hands.Values)
                    if(hand.GetNode<Sprite3D>("Palm").Texture.GetWidth()==0)throw new Exception("Pinned palm texture lost under pressure");
                view.SetEquipment([new ItemStack{Item="gloves",Slot="hands",Count=1}]);
                view.SetEquipment([]);
            }
            foreach(var view in views){parent.RemoveChild(view);parent.AddChild(view);}
        }
        finally{foreach(var view in views)view.Free();Wardrobes.Budget=bodyBudget;Hands.Budget=handBudget;}
        if(Wardrobes.PinnedBytes!=pinnedBody||Hands.PinnedBytes!=pinnedHands)throw new Exception("Removed pawn views retained cache leases");
    }
    public static ImageTexture Portrait(CharacterAppearance appearance,IEnumerable<ItemStack> items)
    {
        return Preview(appearance,items,0);
    }
    public static ImageTexture Preview(CharacterAppearance appearance,IEnumerable<ItemStack> items,int direction)
    {
        var key=new PreviewKey(VisualKey(appearance,items.Where(i=>i.Slot!="").Select(i=>i.Item),direction),direction==3);
        var result=Previews.Acquire(key,()=>
        {
            var body=AcquireBody(key.Body);
            try
            {
                using var image=body.GetImage();image.ClearMipmaps();if(key.Mirrored)image.FlipX();
                return (ImageTexture.CreateFromImage(image),PawnTextureCache.RgbaBytes(image));
            }
            finally{Wardrobes.Release(key.Body);}
        });
        Previews.Release(key);return result;
    }
}

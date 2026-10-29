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
    private static readonly Dictionary<string,ImageTexture> Wardrobes=new();
    private readonly Dictionary<string,Node3D> _hands=new();
    private readonly Dictionary<string,string> _equipment=new();
    private CharacterAppearance _appearance=new();
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
    {var a=appearance.Normalize();if(a==_appearance)return;_appearance=a;Refresh();}
    public void SetEquipment(IEnumerable<ItemStack> items)
    {
        var next=items.Where(i=>i.Slot!="").GroupBy(i=>i.Slot).ToDictionary(g=>g.Key,g=>g.Last().Item);
        if(next.Count==_equipment.Count&&next.All(p=>_equipment.GetValueOrDefault(p.Key)==p.Value))return;
        _equipment.Clear();foreach(var pair in next)_equipment[pair.Key]=pair.Value;Refresh();
    }
    private void Refresh()
    {
        if(_paper is null)return;
        int d=Math.Max(0,_direction),frame=d==3?1:d;bool flip=d==3;
        _body.Texture=Composite(_appearance,_equipment.Values,frame);_body.FlipH=flip;
        foreach(var slot in _hands.Keys)
        {
            bool main=slot=="main";var hand=_hands[slot];var palm=hand.GetNode<Sprite3D>("Palm");
            palm.Texture=Atlas($"hand-{(Wears("gloves")?"glove":"bare")}-{frame}");
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
    public void Tint(Color color)
    {
        _tint=color;if(_body is null)return;_body.Modulate=color;
        foreach(var hand in _hands.Values)
        {hand.GetNode<Sprite3D>("Held").Modulate=color;hand.GetNode<Sprite3D>("Palm").Modulate=Wears("gloves")?color:color*SkinTint(_appearance.Skin);}
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
    private static string Hair(CharacterAppearance a)=>a.Hair switch{"bald"=>"none","braid"=>"braids","bob"=>"waves",_=>"swept"};
    private static Color SkinTint(string skin)=>skin switch{"porcelain"=>new(1,.94f,.88f),"tan"=>new(.88f,.81f,.73f),"brown"=>new(.65f,.55f,.45f),"deep"=>new(.42f,.34f,.28f),_=>Colors.White};
    private static Color HairTint(string color)=>color switch{"black"=>new(.35f,.35f,.36f),"gold"=>new(1,.88f,.57f),"auburn"=>new(.85f,.48f,.3f),"silver"=>new(.86f,.9f,1),"white"=>new(1,1,1),_=>Colors.White};
    private static Image Tinted(Image image,Color color)
    {
        if(color==Colors.White)return image;
        // Work on packed bytes instead of one native GetPixel/SetPixel call per pixel.
        byte[] data=image.GetData();
        for(int i=0;i<data.Length;i+=4)
        {
            if(data[i+3]==0)continue;
            data[i]=(byte)Math.Clamp((int)(data[i]*color.R),0,255);
            data[i+1]=(byte)Math.Clamp((int)(data[i+1]*color.G),0,255);
            data[i+2]=(byte)Math.Clamp((int)(data[i+2]*color.B),0,255);
        }
        return Image.CreateFromData(image.GetWidth(),image.GetHeight(),false,Image.Format.Rgba8,data);
    }
    private static ImageTexture Composite(CharacterAppearance a,IEnumerable<string> items,int d)
    {
        var worn=items.ToHashSet();string key=$"{a}|{d}|"+string.Join(';',worn.Where(n=>n is "shirt" or "trousers" or "armor" or "helmet" or "cap").OrderBy(n=>n));
        if(Wardrobes.TryGetValue(key,out var cache))return cache;
        var image=Image.CreateEmpty(200,280,false,Image.Format.Rgba8);
        void Layer(string name,Color? tint=null)
        {
            var src=Atlas(name).GetImage().GetRegion(new Rect2I(d*200,0,200,280));src.Convert(Image.Format.Rgba8);
            if(tint is {} c)src=Tinted(src,c);
            image.BlendRect(src,new Rect2I(0,0,200,280),Vector2I.Zero);
        }
        string body=Body(a);
        // These opaque garments replace the torso surface. Drawing a differently
        // shaped naked base underneath would leave a skin fringe at its edges.
        if(worn.Contains("armor"))Layer("armor-"+body);
        else if(worn.Contains("shirt"))Layer((a.Outfit=="apron"?"apron-":"work-")+body);
        else
        {
            Layer("body-"+body,SkinTint(a.Skin));
            if(a.BodySex=="female"&&body!="female")Layer("underwear-"+body);
            if(worn.Contains("trousers"))Layer("trousers-"+body);
        }
        Layer("head-"+Head(a),SkinTint(a.Skin));
        if(Hair(a)!="none"&&!worn.Contains("helmet")&&!worn.Contains("cap"))Layer("hair-"+Hair(a),HairTint(a.HairColor));
        if(a.Beard!="none"&&d!=2)Layer("beard-"+(a.Beard=="full"?"full":"short"),HairTint(a.HairColor));
        if(worn.Contains("helmet"))Layer("helmet");else if(worn.Contains("cap"))Layer("cap");
        image.GenerateMipmaps();var result=ImageTexture.CreateFromImage(image);
        if(Wardrobes.Count>=48)Wardrobes.Clear();Wardrobes[key]=result;return result;
    }
    public static ImageTexture Portrait(CharacterAppearance appearance,IEnumerable<ItemStack> items)
    {
        return Preview(appearance,items,0);
    }
    public static ImageTexture Preview(CharacterAppearance appearance,IEnumerable<ItemStack> items,int direction)
    {
        var image=Composite(appearance.Normalize(),items.Where(i=>i.Slot!="").Select(i=>i.Item),direction==3?1:direction).GetImage();
        image.ClearMipmaps();if(direction==3)image.FlipX();return ImageTexture.CreateFromImage(image);
    }
}

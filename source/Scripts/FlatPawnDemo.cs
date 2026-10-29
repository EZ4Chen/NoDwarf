using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

/// <summary>Uses the production compositor and renderer, so the review is also an integration preview.</summary>
public partial class FlatPawnDemo : Node3D
{
    private static readonly string[] Names=["Male · 男体","Female · 女体","Thin · 瘦型","Hulk · 壮型","Fat · 胖型"];
    private static readonly Vector3[] Facings=[Vector3.Back,Vector3.Right,Vector3.Forward,Vector3.Left];
    private static readonly string[] States=["idle","walk","work","attack","block_hold"];
    private static readonly string[] StateNames=["站立","行走","采矿","挥剑","举盾"];
    private readonly List<(DirectionalDwarfView View,CharacterAppearance Appearance)> _actors=[];
    private readonly List<TextureRect> _portraits=[];
    private Control _sheet=null!;
    private Node3D _stage=null!;
    private Label _status=null!;
    private int _garment,_direction,_action;
    private bool _animation,_playing=true,_recording;
    private double _time;
    private Theme _theme=null!;

    public override void _Ready()
    {
        GetWindow().Title="NoDwarf · 五体型纸片矮人";
        GetWindow().ContentScaleSize=new(1440,1000);
        GetWindow().ContentScaleAspect=Window.ContentScaleAspectEnum.Keep;
        _theme=new Theme{DefaultFont=GD.Load<Font>("res://Assets/Fonts/NotoSansCJK/NotoSansCJKsc-Regular.otf"),DefaultFontSize=18};
        AddChild(new WorldEnvironment{Environment=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("202c33")}});
        var camera=new Camera3D{Projection=Camera3D.ProjectionType.Orthogonal,Size=5.8f,Position=new(0,.8f,10)};AddChild(camera);
        _stage=new Node3D{Visible=false};AddChild(_stage);
        for(int row=0;row<2;row++)for(int col=0;col<5;col++)
        {
            var anchor=new Node3D{Position=new((col-2)*1.6f,row==0?1.1f:-1f,0)};_stage.AddChild(anchor);
            var view=new DirectionalDwarfView();anchor.AddChild(view);view.Initialize();
            _actors.Add((view,Appearance(col)));
        }
        var canvas=new CanvasLayer();AddChild(canvas);
        var ui=new Control{Theme=_theme};canvas.AddChild(ui);
        ui.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddLabel(ui,"五种体型 · 四方向纸片矮人",new(34,22),26);
        var buttons=new HBoxContainer{Position=new(34,65)};buttons.AddThemeConstantOverride("separation",12);ui.AddChild(buttons);
        void Button(string text,Action action){var button=new Button{Text=text};UiAccess.Name(button,text);button.Pressed+=action;buttons.AddChild(button);}
        Button("三视图 / 动作预览",()=>{_animation=!_animation;_sheet.Visible=!_animation;_stage.Visible=_animation;Refresh();});
        Button("衣服：工作衫 / 围裙 / 护甲",()=>{_garment=(_garment+1)%3;Refresh();});
        Button("朝向：正 / 右 / 背 / 左",()=>{_direction=(_direction+1)%4;Refresh();});
        Button("动作：站 / 走 / 挖 / 战斗",()=>{_action=(_action+1)%5;Refresh();});
        Button("暂停 / 继续",()=>_playing=!_playing);
        _status=AddLabel(ui,"",new(34,110),17);
        _sheet=new Control{Position=new(0,145)};ui.AddChild(_sheet);
        string[] cols=["白身 · 正面","白身 · 右侧","白身 · 背面","穿衣 · 正面","穿衣 · 右侧","穿衣 · 背面"];
        for(int col=0;col<6;col++)AddLabel(_sheet,cols[col],new(215+col*195,0),17);
        for(int row=0;row<5;row++)
        {
            AddLabel(_sheet,Names[row],new(34,75+row*157),19);
            for(int col=0;col<6;col++)
            {
                var texture=new TextureRect{Position=new(195+col*195,24+row*157),Size=new(174,154),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered};
                _sheet.AddChild(texture);_portraits.Add(texture);
            }
        }
        Refresh();Sample(0);
        if(OS.GetCmdlineUserArgs().Contains("--capture-flat-pawns")){_recording=true;CallDeferred(nameof(Capture));}
    }
    private Label AddLabel(Control parent,string text,Vector2 position,int size)
    {var label=new Label{Text=text,Position=position};label.AddThemeFontSizeOverride("font_size",size);parent.AddChild(label);return label;}
    private static CharacterAppearance Appearance(int i)=>new(){Body=CharacterAppearance.Bodies[i],BodySex=i==1?"female":"male",Face=i==3?"square":"round",Hair=i==1?"braid":"sweep",Beard=i==1?"none":"full",Outfit="tunic"};
    private ItemStack[] Clothes(bool tools)
    {
        var items=new List<ItemStack>{new(){Item=_garment==2?"armor":"shirt",Slot="body"}};
        if(tools)
        {
            items.Add(new(){Item=_action==2?"pickaxe":"sword",Slot="main"});
            if(_action!=2)items.Add(new(){Item="shield",Slot="off"});
            if(_garment==2){items.Add(new(){Item="helmet",Slot="head"});items.Add(new(){Item="gloves",Slot="hands"});}
        }
        return items.ToArray();
    }
    private void Refresh()
    {
        string garment=new[]{"工作衫","工匠围裙","护甲"}[_garment];
        _status.Text=_animation?$"上排白身 / 下排{garment}  ·  {new[]{"正面","右侧","背面","左侧"}[_direction]}  ·  {StateNames[_action]}  ·  平时隐藏手脚，动作时出现手和工具":$"五种体型 × 三个绘制朝向  ·  左侧镜像右侧  ·  当前衣物：{garment}";
        for(int row=0;row<5;row++)for(int col=0;col<6;col++)
            _portraits[row*6+col].Texture=DirectionalDwarfView.Preview(Appearance(row) with{Outfit=_garment==1?"apron":"tunic"},col<3?[]:Clothes(false),col%3);
        for(int i=0;i<_actors.Count;i++)
        {
            _actors[i].View.SetAppearance(_actors[i].Appearance with{Outfit=_garment==1?"apron":"tunic"});
            _actors[i].View.SetEquipment(i<5?[]:Clothes(true));
        }
    }
    public override void _Process(double delta){if(_recording)return;if(_playing)_time+=delta;Sample(_time);}
    private void Sample(double time)
    {for(int i=0;i<_actors.Count;i++)_actors[i].View.Sample(time,i<5?"idle":States[_action],Facings[_direction],time%0.9);}
    private async void Capture()
    {
        try
        {
            string output=ProjectSettings.GlobalizePath("res://../art/dwarf/flat-pawn-five/");
            for(int outfit=0;outfit<3;outfit++)
            {
                _garment=outfit;Refresh();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);RenderingServer.ForceDraw();
                GetViewport().GetTexture().GetImage().SavePng(output+new[]{"comparison-work.png","comparison-apron.png","comparison-armor.png"}[outfit]);
            }
            // Exercise every body/direction/garment, including both lateral views.
            foreach(var body in CharacterAppearance.Bodies)for(int d=0;d<4;d++)
            {
                var a=new CharacterAppearance{Body=body,BodySex="female"};
                foreach(var items in new ItemStack[][]{[],[new(){Item="trousers",Slot="legs"}],[new(){Item="shirt",Slot="body"}],[new(){Item="armor",Slot="body"}]})
                    if(DirectionalDwarfView.Preview(a,items,d).GetImage().GetUsedRect().Size.Y<150)throw new Exception("Empty composite: "+body);
            }
            _animation=true;_sheet.Hide();_stage.Show();_garment=0;_action=2;_direction=1;Refresh();Sample(.55);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);RenderingServer.ForceDraw();
            GetViewport().GetTexture().GetImage().SavePng(output+"action-mining.png");
            foreach(var actor in _actors)
            {
                actor.View.Sample(.2,"walk",Vector3.Back);
                if(actor.View.GetNode<Node3D>("Paper/main").Visible)throw new Exception("Walking shows hands");
                var pose=actor.View.GetNode<Sprite3D>("Paper/body").Transform;
                actor.View.Sample(.2,"walk",Vector3.Back);
                if(pose!=actor.View.GetNode<Sprite3D>("Paper/body").Transform)throw new Exception("Pause drift");
                actor.View.Sample(.4,"walk",Vector3.Back);
                if(pose==actor.View.GetNode<Sprite3D>("Paper/body").Transform)throw new Exception("Walking frozen");
                actor.View.Sample(.55,"work",Vector3.Right);
                if(!actor.View.GetNode<Node3D>("Paper/main").Visible)throw new Exception("Working hides hands");
            }
            GD.Print("FLAT_PAWN_REVIEW_OK: five types, all views/outfits, female modesty overlays, walking/pause, action-only hands");
            GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

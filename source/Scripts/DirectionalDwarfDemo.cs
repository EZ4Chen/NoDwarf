using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class DirectionalDwarfDemo : Node3D
{
    private readonly List<(ActorModelView View,DemoWorker Actor)> _actors=new();
    private Camera3D _camera=null!;
    private Label _status=null!;
    private readonly List<Label> _directionLabels=new();
    private double _time;
    private bool _playing=true,_recording,_swap;
    private int _outfit=1,_action=-1;
    private static readonly Vector3[] Facings={Vector3.Back,Vector3.Right,Vector3.Forward,Vector3.Left};
    private static readonly string[] States={"idle","walk","work","attack","block_hold"};
    private static readonly string[] Actions={"站立","行走","采矿","挥剑","举盾"};
    public override void _Ready()
    {
        GetWindow().Title="NoDwarf · 四方向 2D 矮人";
        var environment=new WorldEnvironment{Environment=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("202c33")}};AddChild(environment);
        _camera=new Camera3D{Projection=Camera3D.ProjectionType.Orthogonal,Size=4.8f,Position=new(0,.8f,10)};AddChild(_camera);
        for(int row=0;row<2;row++)for(int col=0;col<4;col++)
        {
            var actor=new DemoWorker{Name="贴图预览",Appearance=new(){BodySex=row==0?"male":"female",Face=row==0?"square":"round",Hair=row==0?"sweep":"braid",Beard=row==0?"full":"none",Outfit="tunic"}};
            var anchor=new Node3D{Position=new((col-1.5f)*1.65f,row==0?1f:-.85f,0)};AddChild(anchor);
            var view=new ActorModelView();anchor.AddChild(view);view.Initialize();_actors.Add((view,actor));
        }
        var canvas=new CanvasLayer();AddChild(canvas);
        var column=new VBoxContainer{Position=new(28,18)};canvas.AddChild(column);
        column.Theme=new Theme{DefaultFont=GD.Load<Font>("res://Assets/Fonts/NotoSansCJK/NotoSansCJKsc-Regular.otf"),DefaultFontSize=18};
        column.AddChild(new Label{Text="四方向矮人 · 五种体型 · 工作和战斗时显示手"});
        var buttons=new HBoxContainer();buttons.AddThemeConstantOverride("separation",12);column.AddChild(buttons);
        void Button(string text,Action action){var b=new Button{Text=text};UiAccess.Name(b,text);b.Pressed+=action;buttons.AddChild(b);}
        Button("暂停 / 继续",()=>_playing=!_playing);
        Button("换装：白身 / 日常 / 工作装",()=>{_outfit=(_outfit+1)%3;Equip();});
        Button("男女互换",()=>{_swap=!_swap;for(int i=0;i<8;i++)_actors[i].Actor.Appearance=_actors[i].Actor.Appearance with{BodySex=(i<4)^_swap?"male":"female"};});
        Button("切换动作",()=>_action=(_action+2)%6-1);
        Button("旋转镜头 90°",()=>{_camera.Position=_camera.Position.Rotated(Vector3.Up,Mathf.Pi/2);_camera.LookAt(new(0,.8f,0));for(int i=0;i<8;i++)_actors[i].View.GetParent<Node3D>().Position=_camera.Basis.X*((i%4-1.5f)*1.65f)+Vector3.Up*(i<4?1f:-.85f);});
        _status=new Label();column.AddChild(_status);
        for(int c=0;c<4;c++)
        {
            var label=new Label{Text=new[]{"正面","右侧","背面","左侧"}[c],Position=new(201+c*275,116)};label.Theme=column.Theme;canvas.AddChild(label);_directionLabels.Add(label);
        }
        Equip();Sample(0);
        if(OS.GetCmdlineUserArgs().Contains("--record-sprites")){_recording=true;CallDeferred(nameof(Record));}
    }
    private IEnumerable<ItemStack> Loadout(int action)
    {
        if(_outfit==0)return Array.Empty<ItemStack>();
        var worn=new List<ItemStack>{new(){Item="shirt",Slot="body"},new(){Item="trousers",Slot="legs"},new(){Item="boots",Slot="feet"},new(){Item=action==2?"pickaxe":"sword",Slot="main"}};
        if(action!=2)worn.Add(new(){Item="shield",Slot="off"});
        if(_outfit==2){worn.Add(new(){Item="helmet",Slot="head"});worn.Add(new(){Item="gloves",Slot="hands"});}
        return worn;
    }
    private void Equip(){for(int i=0;i<8;i++)_actors[i].View.UpdateEquipment(i<4?Array.Empty<ItemStack>():Loadout(_action));}
    public override void _Process(double delta){if(_recording)return;if(_playing)_time+=delta;Sample(_time);}
    private void Sample(double time)
    {
        int action=_action<0?(int)(time/2)%5:_action;
        _status.Text=$"上排：基础身  ·  下排：{new[]{"基础身","日常装","工作装"}[_outfit]}  ·  {Actions[action]}  ·  左侧由侧身镜像";
        for(int i=0;i<8;i++)
        {
            var (view,actor)=_actors[i];view.Present(actor,time,Facings[i%4],0);
            var sprite=view.GetNode<DirectionalDwarfView>("DwarfSprite");
            sprite.Sample(time,i<4?"idle":States[action],Facings[i%4],time%1);
            if(i<4)_directionLabels[i].Text=new[]{"正面","右侧","背面","左侧"}[sprite.Direction];
            if(i>=4)view.UpdateEquipment(Loadout(action));
        }
    }
    private async void Record()
    {
        string directory=ProjectSettings.GlobalizePath("res://.local/sprite-film");System.IO.Directory.CreateDirectory(directory);
        for(int frame=0;frame<240;frame++)
        {
            if(frame==120){_outfit=2;_swap=true;for(int i=0;i<8;i++)_actors[i].Actor.Appearance=_actors[i].Actor.Appearance with{BodySex=i<4?"female":"male",Hair=i<4?"braid":"sweep",Beard=i<4?"none":"full"};Equip();}
            Sample(frame/24.0);
            RenderingServer.ForceDraw();
            GetViewport().GetTexture().GetImage().SavePng(directory+$"/{frame:0000}.png");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        _action=0;_outfit=1;Equip();Sample(0);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);RenderingServer.ForceDraw();
        GetViewport().GetTexture().GetImage().SavePng(directory+"/female-base-male-work.png");
        GD.Print("SPRITE_FILM_OK");GetTree().Quit();
    }
}

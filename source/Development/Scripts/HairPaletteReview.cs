using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class HairPaletteReview : Control
{
    private readonly List<(TextureRect Texture,int Style,int Direction)> _heads=[];
    private readonly List<TextureRect> _pawns=[];
    private CharacterAppearance _appearance=new(){Body="male",Hair="sweep",Beard="none",Outfit="tunic"};
    private static readonly string[] Styles=["crop","sweep","bob","braid","curls","ponytail","topknot"];
    private static readonly string[] Names=["短寸","侧分短发","蓬松长发","双辫","短卷发","马尾","发髻"];
    private static readonly ItemStack[] Clothes=[new(){Item="shirt",Slot="body"}];
    public override void _Ready()
    {
        GetWindow().Title="NoDwarf · 发型与独立配色";
        GetWindow().ContentScaleSize=new(1440,1000);GetWindow().ContentScaleAspect=Window.ContentScaleAspectEnum.Keep;
        Theme=new Theme{DefaultFont=GD.Load<Font>("res://Assets/Fonts/NotoSansCJK/NotoSansCJKsc-Regular.otf"),DefaultFontSize=18};
        var background=new ColorRect{Color=new("202c33"),MouseFilter=MouseFilterEnum.Ignore};AddChild(background);background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Label("七款发型 · 独立发色 / 肤色 / 胡须颜色",new(32,20),26);
        var controls=new HBoxContainer{Position=new(32,66)};controls.AddThemeConstantOverride("separation",14);AddChild(controls);
        Picker(controls,"发色",PawnColors.Hair(_appearance.HairColor),CharacterAppearance.HairColors.Select(PawnColors.Hair),c=>_appearance=_appearance with{HairColor="#"+c.ToHtml(false)});
        Picker(controls,"肤色",PawnColors.Skin(_appearance.Skin),CharacterAppearance.Skins.Select(PawnColors.Skin),c=>_appearance=_appearance with{Skin="#"+c.ToHtml(false)});
        Picker(controls,"胡须颜色",PawnColors.Hair(_appearance.BeardColor),CharacterAppearance.HairColors.Select(PawnColors.Hair),c=>_appearance=_appearance with{BeardColor="#"+c.ToHtml(false)});
        var face=new OptionButton{CustomMinimumSize=new(130,36)};foreach(var name in new[]{"圆脸","方脸","长脸"})face.AddItem(name);controls.AddChild(face);UiAccess.Name(face,"预览脸型");
        face.ItemSelected+=i=>{_appearance=_appearance with{Face=new[]{"round","square","oval"}[(int)i]};Refresh();};
        for(int col=0;col<7;col++)
        {
            Label(Names[col],new(150+col*180,120),20);
            for(int d=0;d<3;d++)
            {
                var texture=new TextureRect{Position=new(108+col*180,150+d*185),Size=new(170,168),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered};AddChild(texture);_heads.Add((texture,col,d));
            }
        }
        for(int d=0;d<3;d++)Label(new[]{"正面","右侧","背面"}[d],new(28,205+d*185),18);
        Label("配色预览 · 头发、胡须独立调整；头、身体和手使用相同肤色",new(32,720),18);
        for(int col=0;col<7;col++)
        {
            var texture=new TextureRect{Position=new(108+col*180,755),Size=new(170,220),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered};AddChild(texture);_pawns.Add(texture);
        }
        Refresh();
        if(OS.GetCmdlineUserArgs().Contains("--capture-hair"))CallDeferred(nameof(Capture));
    }
    private void Label(string text,Vector2 position,int size){var label=new Label{Text=text,Position=position};label.AddThemeFontSizeOverride("font_size",size);AddChild(label);}
    private void Picker(HBoxContainer parent,string label,Color initial,IEnumerable<Color> presets,Action<Color> change)
    {
        parent.AddChild(new Label{Text=label});var button=new ColorPickerButton{Color=initial,EditAlpha=false,CustomMinimumSize=new(140,36)};parent.AddChild(button);UiAccess.Name(button,label+"，点击调整");
        foreach(var color in presets)button.GetPicker().AddPreset(color);
        button.ColorChanged+=color=>{change(color);Refresh();};
    }
    private void Refresh()
    {
        foreach(var (texture,style,d) in _heads)
            texture.Texture=new AtlasTexture{Atlas=DirectionalDwarfView.Preview(_appearance with{Hair=Styles[style]},[],d),Region=new Rect2(24,0,152,164)};
        for(int i=0;i<_pawns.Count;i++)
            _pawns[i].Texture=DirectionalDwarfView.Preview(_appearance with{Hair=Styles[i],Beard="full",Body=i==3?"female":"male",BodySex=i==3?"female":"male"},i%2==0?[]:Clothes,i==6?3:0);
    }
    private async void Capture()
    {
        try
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);RenderingServer.ForceDraw();
            GetViewport().GetTexture().GetImage().SavePng("res://../art/dwarf/flat-pawn-five/hair-update/styles-review.png");
            foreach(var (texture,style,d) in _heads)
            {
                var a=_appearance with{Hair=Styles[style],Skin=CharacterAppearance.Skins[style%5],HairColor=CharacterAppearance.HairColors[style%6]};
                texture.Texture=new AtlasTexture{Atlas=DirectionalDwarfView.Preview(a,[],d),Region=new Rect2(24,0,152,164)};
            }
            for(int i=0;i<_pawns.Count;i++)_pawns[i].Texture=DirectionalDwarfView.Preview(_appearance with{Hair=Styles[i],Beard="full",Skin=CharacterAppearance.Skins[i%5],HairColor=CharacterAppearance.HairColors[i%6],BeardColor=CharacterAppearance.HairColors[(i+3)%6]},i%2==0?[]:Clothes,i==6?3:0);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);RenderingServer.ForceDraw();
            GetViewport().GetTexture().GetImage().SavePng("res://../art/dwarf/flat-pawn-five/hair-update/palettes-review.png");
            FlatPawnAppearanceChecks.Run();
            GD.Print("HAIR_PALETTE_REVIEW_OK");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private void CheckCharacterEditor()
    {
        var worker = _world.Workers[0]; var before = worker.Appearance;
        SelectResident(0); bool paused = _clock.Paused; OpenCharacterEditor();
        if (!_clock.Paused || _characterPreview.Texture is null) throw new InvalidOperationException("Character preview/pause");
        _appearanceOptions[0].Button.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        if (_draft.BodySex != "female") throw new InvalidOperationException("Body selector must update draft");
        if(ActorModelView.LoadDefinition().Renderer=="sprite4")
        {
            if(_appearanceColors.Count!=3)throw new InvalidOperationException("Missing independent palette controls");
            _appearanceColors[0].Button.EmitSignal(ColorPickerButton.SignalName.ColorChanged,new Color("abcdef"));
            _appearanceColors[1].Button.EmitSignal(ColorPickerButton.SignalName.ColorChanged,new Color("654321"));
            _appearanceColors[2].Button.EmitSignal(ColorPickerButton.SignalName.ColorChanged,new Color("ba9876"));
            if(_draft.HairColor!="#abcdef"||_draft.BeardColor!="#654321"||_draft.Skin!="#ba9876")throw new InvalidOperationException("Palette controls must be independent");
        }
        _draft = _draft with { Cloth = "plum", Body = "hulk" }; UpdateCharacterPreview(); CloseCharacterEditor();
        if (worker.Appearance != before || _clock.Paused != paused) throw new InvalidOperationException("Cancel must preserve appearance and pause state");
        OpenCharacterEditor(); _draft = before with { BodySex = "female",Hair="topknot",HairColor="#abcdef",BeardColor="#654321",Skin="#ba9876" }; ApplyCharacterAppearance();
        if (worker.Appearance.BodySex != "female" || _residentButtons[0].Icon != _people[0].GetChild<Sprite3D>(0).Texture) throw new InvalidOperationException("Applied portrait and world sprite must match");
        if(worker.Appearance.Hair!="topknot"||worker.Appearance.HairColor!="#abcdef"||worker.Appearance.BeardColor!="#654321"||worker.Appearance.Skin!="#ba9876")throw new InvalidOperationException("Applied palette mismatch");
        worker.Appearance = before; UpdateCharacterVisual(0);
        foreach (var sex in new[]{"male","female"})
            _ = EquipmentArt.Texture(before with { BodySex = sex }, _world.Inventory(worker));
        GD.Print("CHARACTER_UI_OK: preview, cancel, apply, synchronized portraits, male/female variants");
    }
    private async void CaptureCharacterPreview()
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            CheckCharacterEditor();
            SelectResident(0); OpenCharacterEditor();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var column = _characterMessage.GetParent<Control>();
            if (column.Size.Y > _characterEditor.Size.Y - 38) throw new InvalidOperationException("Character editor content overflows window");
            var error = GetViewport().GetTexture().GetImage().SavePng("res://.local/character-editor.png");
            if (error != Error.Ok) throw new InvalidOperationException("Preview capture failed: " + error);
            GD.Print("CHARACTER_PREVIEW_OK: editor fits window; .local/character-editor.png");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }

}

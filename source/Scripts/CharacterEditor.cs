using Godot;
using NoDwarf.Simulation;

namespace NoDwarf;

public partial class Main
{
    private Window _characterEditor = null!;
    private TextureRect _characterPreview = null!;
    private Label _characterMessage = null!;
    private Button _editCharacter = null!;
    private CharacterAppearance _draft = new();
    private int _editingWorker = -1;
    private bool _editorWasPaused, _updatingOptions;
    private readonly List<(OptionButton Button, string[] Ids, Func<CharacterAppearance, string> Get)> _appearanceOptions = new();
    private bool IsCheck => OS.GetCmdlineUserArgs().Any(a => a.EndsWith("-test"));
    private string AppearancePath => ProjectSettings.GlobalizePath("user://characters-v1.json");

    private void LoadCharacters()
    {
        if (IsCheck) return;
        try { AppearanceStorage.Load(AppearancePath, _world.Workers); }
        catch (Exception e) when (e is System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { _world.Events.Insert(0, "角色档案读取失败，已保留默认外观：" + e.Message); }
    }

    private void BuildCharacterEditor(Control root)
    {
        _characterEditor = new Window
        {
            Title = "居民外观", Size = new Vector2I(620, 590), MinSize = new Vector2I(540, 560),
            Visible = false, Transient = true, Exclusive = true, Unresizable = true, Theme = root.Theme
        };
        AddChild(_characterEditor);
        _characterEditor.CloseRequested += CloseCharacterEditor;
        var margin = new MarginContainer(); _characterEditor.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 20);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 12); margin.AddChild(column);
        column.AddChild(Text("居民外观 · 纸片角色", 22, Accent));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 28); column.AddChild(row);
        var previewColumn = new VBoxContainer(); row.AddChild(previewColumn);
        _characterPreview = new TextureRect { CustomMinimumSize = new Vector2(190, 310), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        previewColumn.AddChild(_characterPreview);
        previewColumn.AddChild(Text("预览 · 应用后同步头像与人物", 12, Muted));
        var options = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; row.AddChild(options);
        void Add(string label, string[] ids, string[] names, Func<CharacterAppearance,string> get, Func<CharacterAppearance,string,CharacterAppearance> set)
        {
            var line = new HBoxContainer(); options.AddChild(line);
            line.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(64, 36) });
            var choice = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(160, 36) };
            foreach (var name in names) choice.AddItem(name);
            line.AddChild(choice);UiAccess.Name(choice,label); _appearanceOptions.Add((choice, ids, get));
            choice.ItemSelected += index => { if (!_updatingOptions) { _draft = set(_draft, ids[(int)index]); UpdateCharacterPreview(); } };
        }
        Add("基础形象", ["male","female"], ["男矮人","女矮人"], a=>a.BodySex, (a,v)=>a with {BodySex=v});
        if(ActorModelView.LoadDefinition().Renderer!="sprite4")
        {
        Add("肤色", CharacterAppearance.Skins, ["瓷白", "暖肤", "小麦", "棕色", "深棕"], a=>a.Skin, (a,v)=>a with {Skin=v});
        Add("发型", CharacterAppearance.Hairs, ["短发", "侧分", "齐颈发", "辫子", "光头"], a=>a.Hair, (a,v)=>a with {Hair=v});
        Add("发色", CharacterAppearance.HairColors, ["栗棕", "墨黑", "麦金", "赤褐", "银灰", "霜白"], a=>a.HairColor, (a,v)=>a with {HairColor=v});
        Add("脸型", CharacterAppearance.Faces, ["圆脸", "长脸", "方脸"], a=>a.Face, (a,v)=>a with {Face=v});
        Add("服装", CharacterAppearance.Outfits, ["工匠围裙", "束腰衫", "马甲", "长外套"], a=>a.Outfit, (a,v)=>a with {Outfit=v});
        Add("衣色", CharacterAppearance.Cloths, ["鼠尾草", "赭黄", "雾蓝", "灰紫", "铁锈", "米白"], a=>a.Cloth, (a,v)=>a with {Cloth=v});
        Add("体型", CharacterAppearance.Bodies, ["男体", "女体", "瘦型", "壮型", "胖型"], a=>a.Body, (a,v)=>a with {Body=v});
        Add("胡须", CharacterAppearance.Beards, ["无", "短须", "长须"], a=>a.Beard, (a,v)=>a with {Beard=v});
        }
        else
        {
            Add("体型", CharacterAppearance.Bodies, ["男体", "女体", "瘦型", "壮型", "胖型"], a=>a.Body, (a,v)=>a with {Body=v});
            Add("脸型", CharacterAppearance.Faces, ["圆脸", "长脸", "方脸"], a=>a.Face, (a,v)=>a with {Face=v});
            Add("发型", ["sweep","bob","braid","bald"], ["侧分短发","蓬松卷发","双辫","光头"], a=>a.Hair=="crop"?"sweep":a.Hair, (a,v)=>a with {Hair=v});
            Add("胡须", CharacterAppearance.Beards, ["无胡须","短胡须","长胡须"], a=>a.Beard, (a,v)=>a with {Beard=v});
            Add("布衣款式", ["tunic","apron"], ["工作衫","工匠围裙"], a=>a.Outfit=="apron"?"apron":"tunic", (a,v)=>a with {Outfit=v});
            options.AddChild(Text("衣服与武器在装备栏穿脱", 14, Muted, true));
        }
        _characterMessage = Text("", 13, Muted, true); _characterMessage.CustomMinimumSize = new Vector2(0, 56); column.AddChild(_characterMessage);
        var actions = new HBoxContainer(); column.AddChild(actions);
        Button(actions, "随机外观", () => { _draft = CharacterAppearance.Randomize(Random.Shared); UpdateCharacterPreview(); });
        Button(actions, "还原", () => { _draft = _world.Workers[_editingWorker].Appearance; UpdateCharacterPreview(); });
        Button(actions, "取消", CloseCharacterEditor);
        UiAccess.Name(Button(actions, "保存外观", ApplyCharacterAppearance),"保存并应用此外观","只保存人物外观，不保存游戏进度；取消会放弃当前外观修改。");
    }

    private void OpenCharacterEditor()
    {
        if (_selectedWorker < 0 || _characterEditor.Visible) return;
        _editingWorker = _selectedWorker;
        _draft = _world.Workers[_editingWorker].Appearance.Normalize();
        _editorWasPaused = _clock.Paused; _clock.Paused = true; _dragging = false;
        _characterEditor.Title = "居民外观 · " + _world.Workers[_editingWorker].Name;
        _characterMessage.Text = "编辑时暂停。保存外观后，下次启动和生成新地图会沿用；营地进度仍不保存。";
        UpdateCharacterPreview(); _characterEditor.PopupCentered(); RefreshUi();
    }
    private void UpdateCharacterPreview()
    {
        _updatingOptions = true;
        foreach (var option in _appearanceOptions) option.Button.Select(Array.IndexOf(option.Ids, option.Get(_draft)));
        _updatingOptions = false;
        _characterPreview.Texture = EquipmentArt.Texture(_draft, _editingWorker >= 0 ? _world.Inventory(_world.Workers[_editingWorker]) : Array.Empty<ItemStack>());
    }
    private void ApplyCharacterAppearance()
    {
        var worker = _world.Workers[_editingWorker]; var previous = worker.Appearance;
        worker.Appearance = _draft.Normalize();
        try { if (!IsCheck) AppearanceStorage.Save(AppearancePath, _world.Workers); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        { worker.Appearance = previous; _characterMessage.Text = "保存失败，尚未应用。请重试：" + e.Message; return; }
        UpdateCharacterVisual(_editingWorker); CloseCharacterEditor();
    }
    private void UpdateCharacterVisual(int index)
    {
        var texture = EquipmentArt.Texture(_world.Workers[index].Appearance,_world.Inventory(_world.Workers[index]));
        _people[index].GetChild<Sprite3D>(0).Texture = texture;
        if (index < _residentButtons.Count) _residentButtons[index].Icon = texture;
    }
    private void CloseCharacterEditor()
    {
        _characterEditor.Hide(); _editingWorker = -1;
        _clock.Paused = _editorWasPaused; RefreshUi();
    }

    private void CheckCharacterEditor()
    {
        var worker = _world.Workers[0]; var before = worker.Appearance;
        SelectResident(0); bool paused = _clock.Paused; OpenCharacterEditor();
        if (!_clock.Paused || _characterPreview.Texture is null) throw new InvalidOperationException("Character preview/pause");
        _appearanceOptions[0].Button.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        if (_draft.BodySex != "female") throw new InvalidOperationException("Body selector must update draft");
        _draft = _draft with { Cloth = "plum", Body = "broad" }; UpdateCharacterPreview(); CloseCharacterEditor();
        if (worker.Appearance != before || _clock.Paused != paused) throw new InvalidOperationException("Cancel must preserve appearance and pause state");
        OpenCharacterEditor(); _draft = before with { BodySex = "female" }; ApplyCharacterAppearance();
        if (worker.Appearance.BodySex != "female" || _residentButtons[0].Icon != _people[0].GetChild<Sprite3D>(0).Texture) throw new InvalidOperationException("Applied portrait and world sprite must match");
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

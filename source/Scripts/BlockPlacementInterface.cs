using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private PlacementDefinition? _activePlacement;
    private int _placementSource,_selectedItemId;
    private DemoWorld? _itemSelectionWorld;
    private PopupPanel? _blockPicker;
    private LineEdit? _blockSearch;
    private OptionButton? _blockOrigin;
    private VBoxContainer? _blockChoices;
    private int _pickerSource;
    private Button _placeBlockMenuButton=null!,_placeItemButton=null!;
    private HBoxContainer _groundItemRow=null!;
    private OptionButton _groundItemChoice=null!;
    private readonly List<int> _groundItemIds=new();
    private readonly Dictionary<(string,string),Button> _blockChoiceButtons=new();
    private void ShowBlockPicker(int source=0)
    {
        _blockPicker?.QueueFree();_pickerSource=source;
        _blockPicker=new PopupPanel{Name="BlockPlacementPicker"};AddChild(_blockPicker);
        var picker=_blockPicker;
        picker.PopupHide+=()=>{if(ReferenceEquals(_blockPicker,picker)&&_tool=="placeblock"&&_activePlacement is null)SelectTool("select");};
        var box=new VBoxContainer{CustomMinimumSize=new(420,0)};_blockPicker.AddChild(box);
        var heading=new HBoxContainer();box.AddChild(heading);
        var title=Text("放置方块",16,Ink);title.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;heading.AddChild(title);
        Button(heading,"关闭",()=>_blockPicker?.Hide());
        var searchRow=new HBoxContainer();box.AddChild(searchRow);searchRow.AddChild(Text("搜索",13,Ink));
        _blockSearch=new LineEdit{PlaceholderText="方块或材料名称",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};searchRow.AddChild(_blockSearch);UiAccess.Name(_blockSearch,"搜索可放置方块");
        _blockOrigin=new OptionButton();foreach(var label in new[]{"全部来源","原版","Mod"})_blockOrigin.AddItem(label);searchRow.AddChild(_blockOrigin);UiAccess.Name(_blockOrigin,"方块来源筛选");
        var scroll=new ScrollContainer{CustomMinimumSize=new(0,290),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};box.AddChild(scroll);
        _blockChoices=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};scroll.AddChild(_blockChoices);
        _blockSearch.TextChanged+=_=>RefreshBlockChoices();_blockOrigin.ItemSelected+=_=>RefreshBlockChoices();
        RefreshBlockChoices();_blockPicker.PopupCentered();_blockSearch.GrabFocus();
    }
    private void RefreshBlockChoices()
    {
        if(_blockChoices is null)return;
        foreach(var child in _blockChoices.GetChildren()){_blockChoices.RemoveChild(child);child.QueueFree();}_blockChoiceButtons.Clear();
        var source=_pickerSource==0?null:_world.Items.FirstOrDefault(s=>s.Id==_pickerSource&&s.Owner==""&&s.Slot=="");
        string query=_blockSearch?.Text.Trim()??"";int origin=_blockOrigin?.Selected??0;
        foreach(var p in _world.Content.Placements.OrderBy(p=>_world.Content.Block(p.Block).Name,StringComparer.Ordinal).ThenBy(p=>p.Item,StringComparer.Ordinal))
        {
            if(_pickerSource!=0&&(source is null||source.Item!=p.Item))continue;
            bool core=_world.Content.Sources.TryGetValue("spatial/Blocks/"+p.Block,out var provenance)&&provenance.Pack=="base";
            if(origin==1&&!core||origin==2&&core)continue;
            var block=_world.Content.Block(p.Block);var item=_world.Content.Item(p.Item);
            if(query.Length>0&&!string.Join(" ",block.Name,item.Name,p.Block,p.Item,string.Join(" ",block.Info.Tags)).Contains(query,StringComparison.OrdinalIgnoreCase))continue;
            int count=_pickerSource==0?_world.Available(p.Item):_world.AvailableInStack(_pickerSource);
            var button=Button(_blockChoices,$"{block.Name}  ·  {item.Name} ×{p.Count}/格  ·  可用 {count}",()=>BeginBlockPlacement(p,_pickerSource));
            button.Alignment=HorizontalAlignment.Left;button.Disabled=count<p.Count;button.Icon=UiIcons.Get(item.Texture);button.ExpandIcon=true;button.AddThemeConstantOverride("icon_max_width",24);
            button.TooltipText=count<p.Count?"可用材料不足或已被预留":"选择后在地图上单击或拖框，居民搬料放置。";
            UiAccess.Name(button,$"放置{block.Name}，每格需要{p.Count}份{item.Name}，可用{count}",button.TooltipText);
            _blockChoiceButtons[(p.Item,p.Block)]=button;
        }
        if(_blockChoiceButtons.Count==0)_blockChoices.AddChild(Text("没有匹配的可放置方块",14,Muted));
    }
    private void BeginBlockPlacement(PlacementDefinition p,int source)
    {
        _activePlacement=_world.Content.Placement(p.Item,p.Block);_placementSource=source;
        SelectTool("placeblock");_buildMenu.Hide();
    }
    private bool QueuePlacement(Cell c,bool announce=true)=>_activePlacement is {} p&&_world.QueuePlaceBlock(c,p.Item,p.Block,_placementSource,announce);
    private void CommitPlacementArea(Cell from,Cell to)
    {
        if(from.Level!=to.Level||Math.Abs(from.X-to.X)>=16||Math.Abs(from.Z-to.Z)>=16){_world.Log("方块放置范围需要同层，最多 16×16 格。");return;}
        int planned=0,total=0;
        for(int x=Math.Min(from.X,to.X);x<=Math.Max(from.X,to.X);x++)for(int z=Math.Min(from.Z,to.Z);z<=Math.Max(from.Z,to.Z);z++)
        {total++;if(QueuePlacement(new(x,z,from.Level),false))planned++;}
        _world.Log($"已规划 {planned}/{total} 格；跳过材料不足、已有规划或无法放置的位置。");SyncWorld();RefreshUi();
    }
    private void DrawPlacedBlock(Node parent,Cell c,string block)=>DrawPlanBlock(parent,c,_world.Content.Block(block),BlockExposure.PlanFaces(_world,CurrentView,c,SolidPlanCells()));
    private void DrawPlacementPreview(Cell cell)
    {
        if(_activePlacement is not {} p)return;
        SetPreviewSolids(new[]{cell});
        bool valid=_world.CanPlaceBlock(cell,p.Item,p.Block)&&(_placementSource==0?_world.Available(p.Item):_world.AvailableInStack(_placementSource))>=p.Count;
        var shape=new Node3D();_hover.AddChild(shape);DrawPlacedBlock(shape,cell,p.Block);foreach(var mesh in shape.GetChildren().OfType<MeshInstance3D>()){mesh.MaterialOverride=PreviewMaterial(!valid);mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;}
        _hint.Text=$"放置{_world.Content.Block(p.Block).Name} · {_world.Content.Item(p.Item).Name} ×{p.Count}/格 · 左键或拖框规划；右键退出";
    }
    private void BuildItemPlacementCommands()
    {
        _groundItemRow=new HBoxContainer();_objectCommands.AddChild(_groundItemRow);_groundItemRow.AddChild(Text("地上物品",13,Ink));
        _groundItemChoice=new OptionButton{CustomMinimumSize=new(170,34)};_groundItemRow.AddChild(_groundItemChoice);UiAccess.Name(_groundItemChoice,"选择此处的物品堆");
        _groundItemChoice.ItemSelected+=index=>{if(index>=0&&index<_groundItemIds.Count){_selectedItemId=_groundItemIds[(int)index];RefreshUi();}};
        _placeItemButton=IconButton(_objectCommands,"Cobblestone","放置",PlaceSelectedItem,true);StyleObjectCommand(_placeItemButton,"Cobblestone");
        UiAccess.Name(_placeItemButton,"放置所选物品","选择目标位置，居民搬运材料并放成方块。");
    }
    private void PlaceSelectedItem()
    {
        var stack=_world.Items.FirstOrDefault(s=>s.Id==_selectedItemId&&s.Owner==""&&s.Slot=="");if(stack is null)return;
        var choices=_world.Content.Item(stack.Item).Placements;
        if(choices.Count==1)BeginBlockPlacement(choices[0],stack.Id);else if(choices.Count>1)ShowBlockPicker(stack.Id);
    }
    private void RefreshItemPlacementCommands()
    {
        if(_groundItemRow is null)return;
        if(_itemSelectionWorld!=_world){_selectedItemId=0;_itemSelectionWorld=_world;}
        var stacks=_selectedWorker<0&&_selectedCell is Cell c&&CurrentView.Known(_world,c)?_world.Items.Where(s=>s.Owner==""&&s.Slot==""&&(s.Cell==c||s.Cell==c+new Cell(0,0,1))).OrderBy(s=>s.Id).ToArray():Array.Empty<ItemStack>();
        _groundItemIds.Clear();_groundItemChoice.Clear();
        foreach(var s in stacks){_groundItemIds.Add(s.Id);_groundItemChoice.AddItem($"{_world.Content.Item(s.Item).Name} ×{s.Count}");}
        if(!stacks.Any(s=>s.Id==_selectedItemId))_selectedItemId=stacks.FirstOrDefault()?.Id??0;
        if(_selectedItemId!=0)_groundItemChoice.Select(_groundItemIds.IndexOf(_selectedItemId));
        _groundItemRow.Visible=stacks.Length>0;
        var selected=stacks.FirstOrDefault(s=>s.Id==_selectedItemId);
        var choices=selected is null?Array.Empty<PlacementDefinition>():_world.Content.Item(selected.Item).Placements;
        _placeItemButton.Visible=choices.Count>0;_placeItemButton.Disabled=selected is null||!choices.Any(p=>_world.AvailableInStack(selected.Id)>=p.Count);
        _placeItemButton.TooltipText=_placeItemButton.Disabled?"所选物品数量不足或已被预留":"选择目标位置，居民搬运材料并放成方块。";
        _objectCommands.Visible=_selectionPanel.Visible&&_selectedWorker<0&&(_groundItemRow.Visible||_objectCommands.GetChildren().OfType<Button>().Any(b=>b.Visible));
    }
    private bool SelectDroppedItem(Vector2 screen)
    {
        var hit=PickObject(screen,11);if(hit.Count==0||!hit["collider"].AsGodotObject().HasMeta("item_id"))return false;
        int id=hit["collider"].AsGodotObject().GetMeta("item_id").AsInt32();var stack=_world.Items.FirstOrDefault(s=>s.Id==id&&s.Owner=="");if(stack is null)return false;
        ClearResidentSelection();Act(stack.Cell);_selectedItemId=id;_itemSelectionWorld=_world;RefreshUi();return true;
    }
}

using Godot;
using NoDwarf.Simulation;
using NoDwarf.MenuLogic;
using System.Text.Json;
namespace NoDwarf;
public partial class Main
{
    private LineEdit _recipeSearch=null!;
    private GameWindowFrame _productionFrame=null!;
    private BoxContainer _productionColumns=null!;
    private Cell? _billStation;
    private string _recipeSelected="sword";
    private readonly Dictionary<string,Button> _recipeButtons=new();
    private VBoxContainer _billRecipes=null!,_billClipboard=null!;
    private PanelContainer _billInfo=null!;
    private Label _billInfoText=null!;
    private ScrollContainer _billOrderScroll=null!;
    private readonly List<ProductionOrder> _copiedBills=new();
    private readonly Dictionary<int,BillRow> _billRows=new();
    private DemoWorld? _billWorld;
    private sealed record BillRow(Control Root,Label Name,Label Count,Button Pause,Button Up,Button Down,OptionButton Mode);
    private static string RecipeCategory(ProcessingRecipe r)=>DemoWorld.NameOf(r.Station);
    private static string RecipeLabel(ProcessingRecipe r)=>r.Name.Split('：')[0];
    private void LayoutProductionPanel()
    {
        var size=_inspector.GetParent<Control>().Size;
        _inspector.Position=new(0,Math.Max(40,size.Y-Math.Min(650,size.Y-100)-40));
        float width=Math.Min(size.X,Math.Max(780,Math.Min(1100,size.X*.70f)));
        if(_productionColumns is not null)_productionColumns.Vertical=width<760;
        _inspector.Size=new(width,Math.Min(650,size.Y-100));
    }
    private Button BillButton(Node parent,string label,Action click)
    {
        return GameUi.Action(parent,label,click);
    }
    private Button BillIconButton(Node parent,string texture,string name,Action action,string fallback="")
    {
        var button=GameUi.Action(parent,fallback,action);button.CustomMinimumSize=new(30,30);button.AddThemeConstantOverride("icon_max_width",20);
        button.AddThemeStyleboxOverride("normal",GameUi.Box(GameUi.Raised,4,4));button.AddThemeStyleboxOverride("hover",GameUi.Box(GameUi.Hover,4,4));button.AddThemeStyleboxOverride("pressed",GameUi.Box(GameUi.Selected,4,4));
        if(texture!=""){button.Icon=MintTexture(texture);button.ExpandIcon=true;}
        UiAccess.Name(button,name);button.TooltipText=name;return button;
    }
    private void BuildProductionBrowser(Control root)
    {
        _productionFrame=new GameWindowFrame("生产清单",()=>_inspector.Hide());_inspector=_productionFrame;root.AddChild(_inspector);_inspector.ZIndex=20;
        _inspector.VisibilityChanged+=()=>{if(_inspector.Visible){_inspector.MoveToFront();RefreshOrders();}else _billInfo?.Hide();};
        _productionColumns=new BoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};_productionColumns.AddThemeConstantOverride("separation",16);_productionFrame.Body.AddChild(_productionColumns);
        var columns=_productionColumns;LayoutProductionPanel();root.Resized+=LayoutProductionPanel;
        var left=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill,SizeFlagsStretchRatio=1};columns.AddChild(left);
        GameUi.Label(left,"制造队列",muted:true);
        _billOrderScroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,SizeFlagsVertical=Control.SizeFlags.ExpandFill};left.AddChild(_billOrderScroll);
        _orderList=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};_orderList.AddThemeConstantOverride("separation",4);_billOrderScroll.AddChild(_orderList);
        _emptyOrders=Text("还没有清单。选择配方开始制造。",14,Muted,true);_orderList.AddChild(_emptyOrders);
        var right=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill,SizeFlagsStretchRatio=1.1f};columns.AddChild(right);
        GameUi.Label(right,"添加配方",muted:true);
        var toolbar=new HBoxContainer();toolbar.AddThemeConstantOverride("separation",0);right.AddChild(toolbar);
        var settings=new PopupPanel();_inspector.AddChild(settings);var options=new VBoxContainer();settings.AddChild(options);
        var showMaterials=new CheckBox{Text="显示配方原料",ButtonPressed=true};options.AddChild(showMaterials);
        showMaterials.Toggled+=on=>{foreach(var b in _recipeButtons.Values)b.GetChild<Control>(0).Visible=on;};
        BillIconButton(toolbar,"MenuSett","配方显示设置",()=>settings.Popup(new Rect2I((Vector2I)toolbar.GlobalPosition,new Vector2I(200,60))));
        bool az=false;BillIconButton(toolbar,"az","配方按名称排序",()=>{az=!az;int i=0;foreach(var r in az?_world.Content.Recipes.OrderBy(r=>r.Name).ToArray():_world.Content.Recipes)_billRecipes.MoveChild(_recipeButtons[r.Id],i++);});
        _recipeSearch=CatalogSearch(toolbar,"搜索配方或原料");_recipeSearch.CustomMinimumSize=new(80,28);_recipeSearch.ClearButtonEnabled=false;
        BillIconButton(toolbar,"","清除配方搜索",()=>{_recipeSearch.Text="";FilterRecipes();},"×");
        BillIconButton(toolbar,"","查看配方说明",()=>{_billInfo.Visible=!_billInfo.Visible;RefreshRecipeDetail();},"i");
        var tabs=new TabBar();tabs.AddTab("配方");tabs.AddTab("已复制清单");right.AddChild(tabs);
        var panel=new PanelContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};panel.AddThemeStyleboxOverride("panel",GameUi.Box(GameUi.Inset,4,4));right.AddChild(panel);
        var scroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};panel.AddChild(scroll);
        var content=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};scroll.AddChild(content);
        _billRecipes=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};_billRecipes.AddThemeConstantOverride("separation",0);content.AddChild(_billRecipes);
        _billClipboard=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,Visible=false};content.AddChild(_billClipboard);
        tabs.TabChanged+=i=>{_billRecipes.Visible=i==0;_billClipboard.Visible=i==1;RefreshBillClipboard();};
        foreach(var recipe in _world.Content.Recipes)
        {
            var r=recipe;
            var button=IconButton(_billRecipes,r.Output,RecipeLabel(r),()=>{_world.AddProductionOrder(r.Id,station:_billStation);RefreshOrders();});
            button.CustomMinimumSize=new(0,64);button.Alignment=HorizontalAlignment.Left;button.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
            button.AddThemeStyleboxOverride("normal",new StyleBoxFlat{BgColor=new Color(GameUi.Inset),BorderColor=new Color(GameUi.Border),BorderWidthBottom=1,ContentMarginLeft=4,ContentMarginRight=110});
            var hover=GameUi.Box(GameUi.Hover,4,0);hover.ContentMarginRight=110;button.AddThemeStyleboxOverride("hover",hover);button.AddThemeConstantOverride("icon_max_width",32);
            button.ClipText=true;UiAccess.Name(button,RecipeLabel(r),"点击添加清单；每批产出 "+r.Count+"。"+r.Name);button.TooltipText=r.Name;
            var ingredients=new HBoxContainer{MouseFilter=Control.MouseFilterEnum.Ignore,Alignment=BoxContainer.AlignmentMode.End};button.AddChild(ingredients);
            ingredients.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);ingredients.OffsetLeft=-104;ingredients.OffsetRight=-4;ingredients.OffsetTop=5;ingredients.OffsetBottom=-3;
            foreach(var (item,count) in r.Inputs)
            {
                var ingredient=new VBoxContainer{MouseFilter=Control.MouseFilterEnum.Ignore};ingredients.AddChild(ingredient);ingredient.AddThemeConstantOverride("separation",0);
                ingredient.AddChild(new TextureRect{Texture=UiIcons.Get(item),CustomMinimumSize=new(28,28),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=Control.MouseFilterEnum.Ignore});
                var amount=Text(count.ToString(),12,Ink);amount.HorizontalAlignment=HorizontalAlignment.Center;amount.MouseFilter=Control.MouseFilterEnum.Ignore;ingredient.AddChild(amount);
            }
            button.MouseEntered+=()=>{_recipeSelected=r.Id;RefreshRecipeDetail();};button.FocusEntered+=()=>{_recipeSelected=r.Id;RefreshRecipeDetail();};_recipeButtons[r.Id]=button;
        }
        _billInfo=Panel(root,Vector2.Zero,new Vector2(270,140));_billInfo.ZIndex=22;_billInfo.Hide();_billInfoText=Text("",14,Ink,true);_billInfo.AddChild(_billInfoText);
        _recipeSearch.TextChanged+=_=>FilterRecipes();
    }
    private void FilterRecipes()
    {
        foreach(var r in _world.Content.Recipes)_recipeButtons[r.Id].Visible=(_billStation is null||_world.Buildings.TryGetValue(_billStation.Value,out var bench)&&bench.Kind==r.Station)&&MintMenuLogic.Matches(RecipeEntry(r),_recipeSearch.Text.Trim());
    }
    private void SelectRecipe(string id){_recipeSelected=id;RefreshRecipeDetail();}
    private void RefreshRecipeDetail()
    {
        if(_billInfo is null||!_billInfo.Visible)return;
        var r=_world.Content.Recipe(_recipeSelected);_billInfoText.Text=r.Name+"\n设施："+DemoWorld.NameOf(r.Station)+$"\n工作量：{r.Work} · 每批 {r.Count} 个";
        var root=_inspector.GetParent<Control>();_billInfo.Position=new(Math.Min(_inspector.Position.X+_inspector.Size.X,root.Size.X-280),Math.Max(0,_inspector.Position.Y));
    }
    private void RefreshBillClipboard()
    {
        Clear(_billClipboard);
        if(_copiedBills.Count==0)_billClipboard.AddChild(Text("点击清单上的复制图标，保存制造方式与筛选设置。",14,Muted,true));
        foreach(var template in _copiedBills.Where(t=>_billStation is null||_world.Buildings.TryGetValue(_billStation.Value,out var b)&&b.Kind==_world.Content.Recipe(t.Recipe).Station))
            BillButton(_billClipboard,"粘贴："+RecipeLabel(_world.Content.Recipe(template.Recipe)),()=>
            {
                var copy=JsonSerializer.Deserialize<ProductionOrder>(JsonSerializer.Serialize(template))!;
                var order=_world.AddProductionOrder(copy.Recipe,copy.Mode,station:_billStation);copy.Id=order.Id;copy.Station=_billStation;
                _world.ProductionOrders[_world.ProductionOrders.IndexOf(order)]=copy;RefreshOrders();
            });
    }
    private void OpenProductionBills(Cell? station=null)
    {
        if(_inspector.Visible&&_billStation==station){_inspector.Hide();return;}
        _combatPanel?.Hide();if(DossierOpen)CloseResidentPanel();_storagePanel?.QueueFree();_storagePanel=null;
        _buildMenu.Hide();_selectionPanel.Hide();_worldMenu.Hide();SelectTool("select");_billStation=station;_recipeSearch.Text="";FilterRecipes();_inspector.Show();RefreshOrders();
    }
    private void RefreshOrders()
    {
        if(_orderList is null||!_inspector.Visible)return;
        if(_billWorld!=_world){foreach(var row in _billRows.Values){_orderList.RemoveChild(row.Root);row.Root.QueueFree();}_billRows.Clear();_billWorld=_world;}
        foreach(int id in _billRows.Keys.Where(id=>!_world.ProductionOrders.Any(o=>o.Id==id)).ToArray())
        {var row=_billRows[id];_orderList.RemoveChild(row.Root);row.Root.QueueFree();_billRows.Remove(id);}
        var visible=_world.ProductionOrders.Where(o=>_billStation is null||o.Station==_billStation).ToArray();
        _emptyOrders.Visible=visible.Length==0;
        _productionFrame.Heading.Text=_billStation is {} station&&_world.Buildings.TryGetValue(station,out var building)?DemoWorld.NameOf(building.Kind)+" · 生产清单":"生产清单";
        foreach(var row in _billRows.Values)row.Root.Hide();
        int index=1;
        foreach(var order in visible)
        {
            if(!_billRows.TryGetValue(order.Id,out var row))_billRows[order.Id]=row=CreateBillRow(order);
            row.Root.Show();_orderList.MoveChild(row.Root,index++);row.Name.Text=RecipeLabel(_world.Content.Recipe(order.Recipe));
            row.Count.Text=_world.Content.Mode(order.Mode).Behavior switch{"forever"=>"永远","maintain"=>$"{_world.ProductionStock(_world.Content.Recipe(order.Recipe).Output)}/{order.Target}",_=>$"{order.Target}×"};
            row.Count.TooltipText=_world.ProductionTargetStatus(order);row.Pause.AddThemeColorOverride("font_color",order.Enabled?new Color(GameUi.Ink):new Color(GameUi.Accent));
            row.Pause.Text=order.Enabled?"Ⅱ":"▶";
            row.Pause.TooltipText=order.Enabled?"暂停这条清单":"恢复这条清单";row.Pause.AccessibilityName=row.Pause.TooltipText;
            row.Up.Disabled=index==2;row.Down.Disabled=index==visible.Length+1;
            row.Mode.Select(_world.Content.Modes.ToList().FindIndex(m=>m.Id==order.Mode));
        }
    }
    private void MoveBillInView(ProductionOrder order,int delta)
    {
        var visible=_world.ProductionOrders.Where(o=>_billStation is null||o.Station==_billStation).ToList();int index=visible.IndexOf(order);
        int next=Math.Clamp(index+delta,0,visible.Count-1);
        _world.MoveProductionOrder(order.Id,_world.ProductionOrders.IndexOf(visible[next])-_world.ProductionOrders.IndexOf(order));
    }
    private BillRow CreateBillRow(ProductionOrder order)
    {
        var box=new PanelContainer();box.AddThemeStyleboxOverride("panel",GameUi.Box(_billRows.Count%2==0?GameUi.Raised:GameUi.Inset,6,6));_orderList.AddChild(box);
        var lines=new VBoxContainer();lines.AddThemeConstantOverride("separation",2);box.AddChild(lines);
        var top=new HBoxContainer();top.AddThemeConstantOverride("separation",0);lines.AddChild(top);
        var up=BillIconButton(top,"","提高制造顺序",()=>{MoveBillInView(order,-1);RefreshOrders();},"▲");up.AddThemeColorOverride("font_color",new Color(GameUi.Accent));
        var name=Text("",14,Ink);name.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;name.ClipText=true;top.AddChild(name);
        var pause=BillIconButton(top,"","暂停清单",()=>{_world.PauseProductionOrder(order.Id,order.Enabled);RefreshOrders();},"Ⅱ");pause.AddThemeFontSizeOverride("font_size",18);
        var copy=BillIconButton(top,"","复制清单",()=>{_copiedBills.Add(JsonSerializer.Deserialize<ProductionOrder>(JsonSerializer.Serialize(order))!);RefreshBillClipboard();},"");copy.Icon=UiIcons.Get("bill_copy");copy.ExpandIcon=true;
        var delete=BillIconButton(top,"","删除清单",()=>ConfirmBillDelete(order),"");delete.Icon=UiIcons.Get("bill_delete");delete.ExpandIcon=true;delete.TooltipText="删除清单，取消未完成批次并释放原料；已完成产品保留。";
        var lower=new HBoxContainer();lower.AddThemeConstantOverride("separation",2);lines.AddChild(lower);
        var down=BillIconButton(lower,"","降低制造顺序",()=>{MoveBillInView(order,1);RefreshOrders();},"▼");down.AddThemeColorOverride("font_color",new Color(GameUi.Accent));
        var count=Text("",13,Muted);count.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;lower.AddChild(count);
        foreach(int delta in new[]{-1,1}){var change=BillIconButton(lower,"",delta<0?"减少制造数量":"增加制造数量",()=>{order.Target=Math.Clamp(order.Target+delta,1,10000);_world.ProductionOrderChanged(order);RefreshOrders();},delta<0?"−":"+");change.AddThemeColorOverride("font_color",new Color(GameUi.Accent));change.AddThemeFontSizeOverride("font_size",24);}
        var mode=BillModePicker(lower,order);mode.CustomMinimumSize=new(116,26);
        var details=BillButton(lower,"详细…",()=>OpenBillDetails(order));details.CustomMinimumSize=new(54,26);UiAccess.Name(details,"清单详情："+RecipeLabel(_world.Content.Recipe(order.Recipe)));
        return new(box,name,count,pause,up,down,mode);
    }
    private OptionButton BillModePicker(Node parent,ProductionOrder order)
    {
        var mode=new OptionButton();parent.AddChild(mode);foreach(var m in _world.Content.Modes)mode.AddItem(m.Label);
        UiAccess.Name(mode,"制造方式");mode.Select(_world.Content.Modes.ToList().FindIndex(m=>m.Id==order.Mode));
        mode.AddThemeStyleboxOverride("normal",GameUi.Box(GameUi.Raised,5,2));
        mode.ItemSelected+=i=>{order.Mode=_world.Content.Modes[(int)i].Id;order.Target=Math.Max(1,order.Target);_world.ProductionOrderChanged(order);RefreshOrders();if(_billDetails?.Visible==true)OpenBillDetails(order);};return mode;
    }
    private void ConfirmBillDelete(ProductionOrder order)
    {
        var dialog=new ConfirmationDialog{Title="删除清单",DialogText="取消该清单的未完成批次并释放原料。已完成产品保留。",OkButtonText="删除",CancelButtonText="取消"};AddChild(dialog);
        dialog.Confirmed+=()=>{_world.RemoveProductionOrder(order.Id);RefreshOrders();dialog.QueueFree();};dialog.Canceled+=()=>dialog.QueueFree();dialog.PopupCentered();
    }
}

using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private PanelContainer? _billDetails;
    private Control? _billDetailBackdrop;
    private ProductionOrder? _editingBill;
    private void OpenBillDetails(ProductionOrder order)
    {
        _editingBill=order;
        _billDetailBackdrop?.QueueFree();_billDetails?.QueueFree();
        var root=_inspector.GetParent<Control>();
        _billDetailBackdrop=new Control{MouseFilter=Control.MouseFilterEnum.Stop,ZIndex=29};root.AddChild(_billDetailBackdrop);_billDetailBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _billDetails=Panel(root,Vector2.Zero,Vector2.Zero);_billDetails.ZIndex=30;_billDetails.AddThemeStyleboxOverride("panel",RStyle("15191d","777d80",20,16));
        _billDetails.Size=new(Math.Min(1080,root.Size.X-32),Math.Min(680,root.Size.Y-56));_billDetails.Position=(root.Size-_billDetails.Size)/2;
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",14);_billDetails.AddChild(column);
        var header=new HBoxContainer();column.AddChild(header);var recipe=ProductionCatalog.Get(order.Recipe);
        header.AddChild(new TextureRect{Texture=UiIcons.Get(recipe.Output),CustomMinimumSize=new(32,32),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered});
        var title=Text(RecipeLabel(recipe),22,Ink);title.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;header.AddChild(title);MintButton(header,"","关闭清单详情",CloseBillDetails,"×");
        var columns=new HBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};columns.AddThemeConstantOverride("separation",22);column.AddChild(columns);
        var left=BillDetailColumn(columns,false);var middle=BillDetailColumn(columns,false);var right=BillDetailColumn(columns,true);
        var pause=BillGold(left,order.Enabled?"未暂停":"已暂停",()=>{_world.PauseProductionOrder(order.Id,order.Enabled);OpenBillDetails(order);RefreshOrders();});UiAccess.Name(pause,"切换清单暂停状态");
        left.AddChild(Text(RecipeLabel(recipe)+"。",15,Ink,true));
        left.AddChild(Text($"工作量：{recipe.Work}\n每次产出：{recipe.Count} 个\n设施：{DemoWorld.NameOf(recipe.Station)}\n需要：\n"+string.Join("\n",recipe.Inputs.Select(p=>$"- {p.Value}× {ItemCatalog.Get(p.Key).Name}")),15,Ink,true));
        var modeBox=new PanelContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};modeBox.AddThemeStyleboxOverride("panel",RStyle("27292b","777d80",5,5));middle.AddChild(modeBox);
        var modeBody=new VBoxContainer();modeBox.AddChild(modeBody);BillModePicker(modeBody,order);
        if(ProductionDefinitions.Mode(order.Mode).Behavior!="forever")
        {
            modeBody.AddChild(Text(ProductionDefinitions.Mode(order.Mode).Behavior=="count"?"剩余制造次数":"目标库存数量",14,Ink));
            var amount=new SpinBox{MinValue=1,MaxValue=10000,Value=Math.Max(1,order.Target),Step=1,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};modeBody.AddChild(amount);UiAccess.Name(amount,"清单制造数量");UiAccess.Name(amount.GetLineEdit(),"清单制造数量");
            amount.ValueChanged+=v=>{order.Target=(int)v;_world.ProductionOrderChanged(order);RefreshOrders();};
        }
        modeBody.AddChild(Text(_world.ProductionTargetStatus(order),13,Muted,true));
        var destination=BillZonePicker(middle,order.DestinationZone,true,"成品去向");destination.ItemSelected+=i=>{order.DestinationZone=destination.GetItemId((int)i);_world.ProductionOrderChanged(order);};
        var peoplePanel=new PanelContainer();peoplePanel.AddThemeStyleboxOverride("panel",RStyle("27292b","777d80",5,5));middle.AddChild(peoplePanel);var people=new VBoxContainer();peoplePanel.AddChild(people);
        var worker=new OptionButton{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};people.AddChild(worker);worker.AddItem("任何工作者");var ids=new List<string>{""};
        foreach(var w in _world.Workers.Where(w=>!w.Person.Dead)){worker.AddItem(w.Name);ids.Add(w.Id);}
        if(!ids.Contains(order.WorkerId)){worker.AddItem("指定居民（当前不在场）");ids.Add(order.WorkerId);}
        worker.AddThemeStyleboxOverride("normal",RStyle("66502a","8b754a",5,3));worker.Select(ids.IndexOf(order.WorkerId));UiAccess.Name(worker,"制造该配方的工作者");worker.ItemSelected+=i=>{order.WorkerId=ids[(int)i];_world.ProductionOrderChanged(order);};
        people.AddChild(Text("工作者技能范围：",14,Ink));BillRange(people,0,20,order.MinSkill,order.MaxSkill,"工作者技能范围",(lo,hi)=>{order.MinSkill=lo;order.MaxSkill=hi;_world.ProductionOrderChanged(order);});
        var permissions=new HBoxContainer();right.AddChild(permissions);
        var materialRows=new Dictionary<string,CheckBox>();
        void SetMaterials(bool allowed)
        {order.AllowedIngredients=allowed?recipe.Inputs.Keys.ToHashSet():new();foreach(var (id,check) in materialRows)check.SetPressedNoSignal(allowed);_world.ProductionOrderChanged(order);}
        BillGold(permissions,"全部清除",()=>SetMaterials(false)).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        BillGold(permissions,"全部允许",()=>SetMaterials(true)).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        var search=CatalogSearch(right,"搜索材料");search.CustomMinimumSize=new(80,28);
        BillRange(right,0,100,order.MinDurability,order.MaxDurability,"材料耐久度",(lo,hi)=>{order.MinDurability=lo;order.MaxDurability=hi;_world.ProductionOrderChanged(order);},"%");
        var materialScroll=new ScrollContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};right.AddChild(materialScroll);
        var materials=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};materialScroll.AddChild(materials);
        var rows=new Dictionary<string,Control>();
        foreach(string item in recipe.Inputs.Keys)
        {
            var row=new HBoxContainer();materials.AddChild(row);rows[item]=row;
            row.AddChild(new TextureRect{Texture=UiIcons.Get(item),CustomMinimumSize=new(24,24),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered});
            var materialName=Text(ItemCatalog.Get(item).Name,15,Ink);materialName.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;row.AddChild(materialName);
            var check=new CheckBox{ButtonPressed=order.AllowedIngredients is null||order.AllowedIngredients.Contains(item)};row.AddChild(check);
            Texture2D Mark(string name){var image=UiIcons.Get(name).GetImage();image.Resize(24,24);return ImageTexture.CreateFromImage(image);}
            check.AddThemeIconOverride("checked",Mark("bill_allow"));check.AddThemeIconOverride("unchecked",Mark("bill_deny"));materialRows[item]=check;UiAccess.Name(check,"允许材料："+ItemCatalog.Get(item).Name);
            check.Toggled+=on=>{order.AllowedIngredients??=recipe.Inputs.Keys.ToHashSet();if(on)order.AllowedIngredients.Add(item);else order.AllowedIngredients.Remove(item);_world.ProductionOrderChanged(order);};
        }
        search.TextChanged+=q=>{foreach(var (id,row) in rows)row.Visible=ItemCatalog.Get(id).Name.Contains(q,StringComparison.OrdinalIgnoreCase);};
        var source=BillZonePicker(right,order.SourceZone,false,"材料来源");source.ItemSelected+=i=>{order.SourceZone=source.GetItemId((int)i);_world.ProductionOrderChanged(order);};
        var radiusText=Text(order.Radius==0?"原料搜寻半径：无限制":$"原料搜寻半径：{order.Radius} m",14,Ink);right.AddChild(radiusText);
        var radius=new HSlider{MinValue=1,MaxValue=256,Step=1,Value=order.Radius==0?256:order.Radius,CustomMinimumSize=new(0,22)};right.AddChild(radius);UiAccess.Name(radius,"原料搜寻半径");
        radius.DragEnded+=changed=>{if(changed){order.Radius=radius.Value==256?0:(int)radius.Value;_world.ProductionOrderChanged(order);}};
        radius.ValueChanged+=v=>{radiusText.Text=v==256?"原料搜寻半径：无限制":$"原料搜寻半径：{v:0} m";if(!Input.IsMouseButtonPressed(MouseButton.Left)){order.Radius=v==256?0:(int)v;_world.ProductionOrderChanged(order);}};
        var footer=new HBoxContainer{Alignment=BoxContainer.AlignmentMode.Center};column.AddChild(footer);var close=BillGold(footer,"关闭",CloseBillDetails);close.CustomMinimumSize=new(150,38);
    }
    private VBoxContainer BillDetailColumn(Node parent,bool framed)
    {
        var panel=new PanelContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};panel.AddThemeStyleboxOverride("panel",framed?RStyle("27292b","777d80",5,5):new StyleBoxEmpty());parent.AddChild(panel);
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",8);panel.AddChild(column);return column;
    }
    private OptionButton BillZonePicker(Node parent,int selected,bool destination,string label)
    {
        var picker=new OptionButton{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};parent.AddChild(picker);
        picker.AddItem(destination?"放到最合适的储存区":"材料来源：所有位置",0);if(destination)picker.AddItem("放在地上",-1);
        foreach(var zone in _world.StorageZones)picker.AddItem((destination?"放到":"取自")+$"储存区 #{zone.Id}",zone.Id);
        int index=Enumerable.Range(0,picker.ItemCount).FirstOrDefault(i=>picker.GetItemId(i)==selected,-1);
        if(index<0){picker.AddItem("储存区已移除 #"+selected,selected);index=picker.ItemCount-1;}
        picker.AddThemeStyleboxOverride("normal",RStyle("66502a","8b754a",5,3));picker.Select(index);UiAccess.Name(picker,label);return picker;
    }
    private void BillRange(Node parent,int min,int max,int low,int high,string label,Action<int,int> changed,string suffix="")
    {
        var value=Text($"{low}{suffix} - {high}{suffix}"+(suffix=="%"?" 耐久度":""),14,Muted);value.HorizontalAlignment=HorizontalAlignment.Center;parent.AddChild(value);
        var range=new BillRangeControl{Minimum=min,Maximum=max,Low=low,High=high,CustomMinimumSize=new(0,26)};parent.AddChild(range);UiAccess.Name(range,label,"左右方向键调整，空格切换上下限。");
        range.Changed+=(a,b)=>{value.Text=$"{a}{suffix} - {b}{suffix}"+(suffix=="%"?" 耐久度":"");changed(a,b);};
    }
    private void CloseBillDetails(){_billDetails?.QueueFree();_billDetails=null;_billDetailBackdrop?.QueueFree();_billDetailBackdrop=null;_editingBill=null;RefreshOrders();}
}
public partial class BillRangeControl:Control
{
    public int Minimum,Maximum,Low,High;
    public event Action<int,int>? Changed;
    private bool _upper,_drag;
    public override void _Ready(){FocusMode=FocusModeEnum.All;MouseFilter=MouseFilterEnum.Stop;}
    private float X(int v)=>10+(Size.X-20)*(v-Minimum)/Math.Max(1,Maximum-Minimum);
    public override void _Draw()
    {
        float y=Size.Y/2;DrawLine(new(10,y),new(Size.X-10,y),new Color("777777"),5);DrawLine(new(X(Low),y),new(X(High),y),Colors.White,5);
        DrawCircle(new(X(Low),y),7,Colors.White);DrawCircle(new(X(High),y),7,Colors.White);
        if(HasFocus())DrawArc(new(_upper?X(High):X(Low),y),10,0,Mathf.Tau,24,new Color("e1c67c"),2);
    }
    public override void _GuiInput(InputEvent e)
    {
        void Set(float x){int v=Math.Clamp((int)Math.Round(Minimum+(x-10)/Math.Max(1,Size.X-20)*(Maximum-Minimum)),Minimum,Maximum);if(_upper)High=Math.Max(Low,v);else Low=Math.Min(High,v);QueueRedraw();}
        if(e is InputEventMouseButton{ButtonIndex:MouseButton.Left} b){if(b.Pressed){GrabFocus();_upper=Math.Abs(b.Position.X-X(High))<Math.Abs(b.Position.X-X(Low));_drag=true;Set(b.Position.X);}else if(_drag){_drag=false;Changed?.Invoke(Low,High);}AcceptEvent();}
        if(e is InputEventMouseMotion m&&_drag){Set(m.Position.X);AcceptEvent();}
        if(e is InputEventKey{Pressed:true} k){if(k.Keycode==Key.Space)_upper=!_upper;else if(k.Keycode is Key.Left or Key.Right){int delta=k.Keycode==Key.Left?-1:1;if(_upper)High=Math.Clamp(High+delta,Low,Maximum);else Low=Math.Clamp(Low+delta,Minimum,High);Changed?.Invoke(Low,High);}else return;QueueRedraw();AcceptEvent();}
    }
}

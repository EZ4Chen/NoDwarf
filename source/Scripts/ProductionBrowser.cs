using Godot;
using NoDwarf.Simulation;
using NoDwarf.MenuLogic;
namespace NoDwarf;
public partial class Main
{
    private TabContainer _productionTabs=null!;
    private LineEdit _recipeSearch=null!;
    private string _recipeCategory="全部",_recipeSelected="sword";
    private readonly Dictionary<string,Button> _recipeButtons=new();
    private readonly Dictionary<string,Button> _recipeCategories=new();
    private Label _recipeDetailTitle=null!,_recipeDetails=null!,_recipeEmpty=null!,_recipeAvailability=null!;
    private TextureRect _recipeIcon=null!;
    private Button _recipeQueue=null!,_recipeTarget=null!;
    private static string RecipeCategory(ProcessingRecipe recipe)=>recipe.Station switch
    {BuildKind.Anvil or BuildKind.Furnace=>"冶金锻造",BuildKind.Mill or BuildKind.Stove=>"食物加工",BuildKind.Loom=>"纺织衣物",_=>"木工石工"};
    private void BuildProductionBrowser(Control root)
    {
        _inspector=Panel(root,Vector2.Zero,Vector2.Zero);LayoutCatalogPanel(_inspector);root.Resized+=()=>LayoutCatalogPanel(_inspector);
        _inspector.AddThemeStyleboxOverride("panel",RStyle("171e24f5","817457",10,8));
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",8);_inspector.AddChild(column);
        var heading=new HBoxContainer();column.AddChild(heading);var title=Text("工坊与订单",17,Accent);title.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;heading.AddChild(title);
        UiAccess.Name(Button(heading,"×",()=>_inspector.Hide()),"关闭生产菜单");
        _productionTabs=new TabContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};column.AddChild(_productionTabs);
        VBoxContainer Page(string name)
        {
            var scroll=new ScrollContainer{Name=name,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_productionTabs.AddChild(scroll);
            var box=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};box.AddThemeConstantOverride("separation",8);scroll.AddChild(box);return box;
        }
        var orders=Page("订单");_emptyOrders=Text("暂无生产订单；在「按批生产」选择配方，或设置自动补产。",14,Muted,true);orders.AddChild(_emptyOrders);
        _orderList=new VBoxContainer();orders.AddChild(_orderList);
        var batches=new VBoxContainer{Name="按批生产"};batches.AddThemeConstantOverride("separation",8);_productionTabs.AddChild(batches);
        _recipeSearch=CatalogSearch(batches,"搜索产品、原料或生产设施");
        var body=new HBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};body.AddThemeConstantOverride("separation",10);batches.AddChild(body);
        var categories=new VBoxContainer{CustomMinimumSize=new(92,0)};body.AddChild(categories);
        foreach(string category in new[]{"全部","冶金锻造","食物加工","木工石工","纺织衣物"})
        {_recipeCategories[category]=Button(categories,category,()=>{_recipeCategory=category;_recipeSearch.Text="";FilterRecipes();});}
        var scrollRecipes=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};body.AddChild(scrollRecipes);
        var recipes=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};scrollRecipes.AddChild(recipes);
        _recipeEmpty=Text("没有匹配的配方",14,Muted,true);recipes.AddChild(_recipeEmpty);
        var details=CatalogDetail(body,out _recipeIcon,out _recipeDetailTitle,out var detailActions);
        _recipeDetails=Text("",14,Ink,true);details.AddChild(_recipeDetails);
        _recipeAvailability=Text("",12,Accent,true);detailActions.AddChild(_recipeAvailability);
        _recipeQueue=Button(detailActions,"下单一批",()=>QueueProduction(()=>_world.QueueProductionRecipe(_recipeSelected)));
        UiAccess.Name(_recipeQueue,"按所选配方下单一批","预留原料后由居民加工；在订单页查看或取消。");
        _recipeTarget=Button(detailActions,"设置补产目标",()=>
        {
            int index=Array.FindIndex(ProductionCatalog.All,r=>r.Id==_recipeSelected);_targetRecipe.Select(index);
            _targetRecipe.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);_productionTabs.CurrentTab=2;
        });_recipeTarget.TooltipText="转到库存目标设置，点击应用后才开始自动补产。";
        foreach(var recipe in ProductionCatalog.All)
        {
            var r=recipe;string name=r.Id=="sword"?"打造铁剑":r.Name.Split('：')[0];
            var button=IconButton(recipes,r.Output,name,()=>SelectRecipe(r.Id));
            button.CustomMinimumSize=new(0,48);button.Alignment=HorizontalAlignment.Left;button.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
            button.TooltipText=r.Name+"\n需要"+DemoWorld.NameOf(r.Station);UiAccess.Name(button,name,button.TooltipText);
            button.FocusEntered+=()=>SelectRecipe(r.Id);_recipeButtons[r.Id]=button;
        }
        BuildProductionTargets(Page("自动补产"));
        _recipeSearch.TextChanged+=_=>FilterRecipes();FilterRecipes();SelectRecipe("sword");
    }
    private void FilterRecipes()
    {
        string query=_recipeSearch.Text.Trim();int count=0;
        foreach(var r in ProductionCatalog.All)
        {
            bool show=MintMenuLogic.Matches(RecipeEntry(r),query)&&(query.Length>0||_recipeCategory=="全部"||RecipeCategory(r)==_recipeCategory);
            _recipeButtons[r.Id].Visible=show;if(show)count++;
        }
        _recipeEmpty.Visible=count==0;
        if(count>0&&!_recipeButtons[_recipeSelected].Visible)SelectRecipe(_recipeButtons.First(p=>p.Value.Visible).Key);
        else RefreshRecipeDetail();
        foreach(var (name,button) in _recipeCategories)button.AddThemeStyleboxOverride("normal",ButtonStyle(query.Length==0&&name==_recipeCategory?"66583d":"303942"));
    }
    private void SelectRecipe(string id)
    {
        _recipeSelected=id;
        foreach(var (key,button) in _recipeButtons)button.AddThemeStyleboxOverride("normal",ButtonStyle(key==id?"66583d":"303942"));
        RefreshRecipeDetail();
    }
    private void RefreshRecipeDetail()
    {
        if(_recipeDetails is null)return;
        if(!_recipeButtons.TryGetValue(_recipeSelected,out var selected)||!selected.Visible)
        {_recipeDetailTitle.Text="没有匹配的配方";_recipeIcon.Texture=null;_recipeDetails.Text="清除搜索或更换分类。";_recipeAvailability.Text="";_recipeQueue.Disabled=true;_recipeTarget.Disabled=true;return;}
        _recipeTarget.Disabled=false;
        var r=ProductionCatalog.Get(_recipeSelected);_recipeDetailTitle.Text=ItemCatalog.Get(r.Output).Name;_recipeIcon.Texture=UiIcons.Get(r.Output);
        var missing=r.Inputs.Where(p=>_world.Available(p.Key)<p.Value).Select(p=>ItemCatalog.Get(p.Key).Name).ToArray();
        bool station=_world.Buildings.Values.Any(b=>b.Kind==r.Station);
        if(r.Id is "sword" or "smelt")station=_world.WorkshopReady;
        bool full=_world.Jobs.Count(j=>j.Production&&ProductionCatalog.RecipeOf(j)==r.Id)>=3;
        string reason=!station?"需要可用的"+DemoWorld.NameOf(r.Station):missing.Length>0?"缺少"+string.Join("、",missing):full?"此配方已有 3 批排队":"";
        _recipeDetails.Text=$"每批产出  {r.Count}\n设施  {DemoWorld.NameOf(r.Station)}\n\n原料需求 / 可用\n"+string.Join("\n",r.Inputs.Select(p=>$"{ItemCatalog.Get(p.Key).Name}  {p.Value} / {_world.Available(p.Key)}"));
        _recipeAvailability.Text=reason==""?"可下单；由居民搬料加工。":reason;
        _recipeQueue.Disabled=reason!="";_recipeQueue.TooltipText=reason!=""?reason:"下单一批，预留所需原料。";
    }
}

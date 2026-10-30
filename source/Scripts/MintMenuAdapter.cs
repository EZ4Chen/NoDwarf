using Godot;
using NoDwarf.MenuLogic;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private OptionButton _architectSort=null!;
    private GridContainer _architectSorted=null!;
    private int _architectSortMode;
    private readonly Dictionary<Button,Node> _architectHomes=new();
    private readonly HashSet<string> _pinnedCategories=new();
    private readonly Dictionary<string,HBoxContainer> _categoryRows=new();
    private readonly Dictionary<string,Button> _categoryPins=new();
    private VBoxContainer _architectCategories=null!;
    private void PinArchitectCategory(string name)
    {
        if(!_pinnedCategories.Add(name))_pinnedCategories.Remove(name);
        foreach(var key in _categoryRows.Keys)_categoryButtons[key].Text=(_pinnedCategories.Contains(key)?"• ":"")+key;
        int index=1; // Keep “all” at the top.
        foreach(var key in MintMenuLogic.Categories(_categoryRows.Keys,k=>_pinnedCategories.Contains(k)))
            _architectCategories.MoveChild(_categoryRows[key],index++);
        foreach(var (key,button) in _categoryPins)UiAccess.Name(button,(_pinnedCategories.Contains(key)?"取消置顶":"置顶")+key+"分类");
    }
    private MintEntry ArchitectEntry(string id,string label,string category,string help)
    {
        var materials=new List<string>();var tags=new List<string>{help,id};
        if(TryBuildTool(id,out var kind))
        {
            var definition=_world.Content.Block(ToolDefinition(id)!.Block);tags.Add(ToolDefinition(id)!.Id);tags.AddRange(ToolDefinition(id)!.Info.Tags);materials.AddRange(ToolDefinition(id)!.Cost.Keys.Select(k=>_world.Content.Item(k).Name));
            if(definition.Cost.Stone>0)materials.Add("石料 石材 stone");
            if(definition.Cost.Wood>0)materials.Add("木料 木材 wood");
            if(definition.Cost.Iron>0)materials.Add("铁锭 金属 iron metal");
        }
        return new(id,label,category,"NoDwarf",materials,tags);
    }
    private double? ArchitectStat(string id,int mode)
    {
        if(!TryBuildTool(id,out var kind))return null;
        var definition=_world.Content.Block(ToolDefinition(id)!.Block);
        return mode is 2 or 3?definition.Durability:ToolDefinition(id)!.Cost.Values.Sum();
    }
    private void SortArchitectResults()
    {
        bool sorting=_architectSortMode!=0&&_buildCategory!="规划";
        _architectSorted.Visible=sorting;
        foreach(var e in _architectEntries)
        {
            if(!_architectHomes.ContainsKey(e.Button))_architectHomes[e.Button]=e.Button.GetParent();
            Node home=sorting?_architectSorted:_architectHomes[e.Button];
            if(e.Button.GetParent()!=home)e.Button.Reparent(home);
        }
        if(!sorting){foreach(var e in _architectEntries)e.Button.Text=e.Name;return;}
        foreach(var page in _buildPages.Values)page.Hide();
        var entries=_architectEntries.Where(e=>e.Button.Visible);
        if(_architectSortMode>=2&&(_buildCategory=="全部"||_architectSearch.Text.Trim().Length>0))
            foreach(var e in entries.Where(e=>ArchitectStat(e.Icon,_architectSortMode) is null))e.Button.Hide();
        var ordered=_architectSortMode==1?entries.OrderBy(e=>e.Name,StringComparer.CurrentCulture):
            MintMenuLogic.Ordered(entries,e=>ArchitectStat(e.Icon,_architectSortMode),_architectSortMode is 3 or 4);
        int index=0;
        foreach(var e in ordered.ToArray())_architectSorted.MoveChild(e.Button,index++);
        foreach(var e in _architectEntries)
        {
            e.Button.Text=e.Name;
            if(_architectSortMode>=2&&ArchitectStat(e.Icon,_architectSortMode) is {} value)
                e.Button.Text+=$"\n{(_architectSortMode is 2 or 3?"耐久":"耗料")} {value:0}";
        }
        _architectEmpty.Visible=!_architectEntries.Any(e=>e.Button.Visible);
    }
    private MintEntry RecipeEntry(ProcessingRecipe recipe)
    {
        var tags=new List<string>{recipe.Id,_world.Content.Item(recipe.Output).Name,DemoWorld.NameOf(recipe.Station)};
        string slot=ResidentGear.Get(recipe.Output)?.Slot??"";
        if(slot!="")tags.Add(slot);
        if(slot=="main")tags.Add("近战 melee 武器");
        if(slot is "body" or "legs" or "feet" or "head")tags.Add("衣物 apparel");
        return new(recipe.Id,recipe.Name,RecipeCategory(recipe),"NoDwarf",recipe.Inputs.Keys.Select(k=>_world.Content.Item(k).Name).ToArray(),tags);
    }
}

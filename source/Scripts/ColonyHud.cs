using Godot;
namespace NoDwarf;

public partial class Main
{
    private PanelContainer _viewControls=null!;
    private HBoxContainer _selectionTabs=null!;
    private Button _medicalBedAction=null!;
    private Button _storageAction=null!,_demolishAction=null!;
    private readonly Dictionary<int,Button> _colonyTabs=new();
    private bool ColonyPage=>_dossierPage>=6;

    private static StyleBoxFlat HudStyle(string color,int padding)=>new()
    {
        BgColor=new Color(color),ContentMarginLeft=padding,ContentMarginRight=padding,
        ContentMarginTop=padding,ContentMarginBottom=padding
    };
    private static Button HudTab(Node parent,string title,Action action,string hint)
    {
        var button=Button(parent,title,action);button.CustomMinimumSize=new(80,40);
        button.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;button.TooltipText=hint;
        button.AddThemeStyleboxOverride("normal",ButtonStyle("222d37"));return button;
    }
    private void OpenPersonPage(int page)
    {
        if(_selectedWorker<0||_selectedWorker>=_world.Workers.Count)return;
        _dossierPage=page;
        if(DossierOpen){_dossierId=_world.Workers[_selectedWorker].Id;RenderResidentPanel();LayoutResidentPanel();RefreshUi();}
        else OpenResidentPanel();
    }
    private void OpenColonyPage(int page)
    {
        if(DossierOpen&&_dossierPage==page){CloseResidentPanel();return;}
        if(_selectedWorker<0&&_world.Workers.Count>0)_selectedWorker=0;
        _dossierPage=page;
        if(DossierOpen){RenderResidentPanel();LayoutResidentPanel();RefreshUi();}
        else OpenResidentPanel();
    }
    private void RefreshHudTabs()
    {
        foreach(var (page,button) in _colonyTabs)
        {
            bool active=DossierOpen&&_dossierPage==page;
            button.AddThemeStyleboxOverride("normal",ButtonStyle(active?"596b78":"222d37"));
            button.AddThemeColorOverride("font_color",active?Accent:Ink);
        }
    }
}

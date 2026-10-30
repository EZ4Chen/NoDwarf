using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private HFlowContainer _objectCommands=null!;
    private Label _selectionTitle=null!;
    private Button _productionBillAction=null!,_harvestPlantAction=null!,_cancelHarvestPlantAction=null!;
    private Button _mineSelectionAction=null!,_chopSelectionAction=null!,_cancelChopAction=null!,_locateSelectionAction=null!;
    private void BuildObjectCommands(Control root)
    {
        _objectCommands=new HFlowContainer{Name="ObjectCommands",AnchorTop=1,AnchorBottom=1,AnchorRight=1,
            OffsetLeft=332,OffsetRight=-316,OffsetTop=-148,OffsetBottom=-48,GrowVertical=Control.GrowDirection.Begin,MouseFilter=Control.MouseFilterEnum.Ignore};
        _objectCommands.AddThemeConstantOverride("h_separation",6);_objectCommands.AddThemeConstantOverride("v_separation",6);root.AddChild(_objectCommands);
        Button Command(string name,string icon,Action action,string help)
        {
            var button=IconButton(_objectCommands,icon,name,action,true);
            StyleObjectCommand(button,icon);
            button.TooltipText=help;UiAccess.Name(button,name,help);return button;
        }
        _harvestPlantAction=Command("收获","wheat",()=>{if(_selectedCell is Cell c)_world.QueuePlantHarvest(c);SyncWorld();RefreshUi();},"采收成熟植物，产出作物和种子；收获后移除植株。");
        _cancelHarvestPlantAction=Command("取消收获","cancel",()=>{if(_selectedCell is Cell c)foreach(var j in _world.Jobs.At(c).Where(j=>j.Harvest).ToArray())_world.CancelJob(j);RefreshUi();},"取消尚未完成的收获任务，保留植物。");
        _productionBillAction=Command("清单","workbench",()=>OpenProductionBills(_selectedCell),"查看该设施的制作清单、原料和工作者设置。");
        _mineSelectionAction=Command("开采","dig",()=>OrderSelectedObject("dig"),"安排居民开采这个方块；完成后移除岩土并掉落材料，开始前可取消。");
        _demolishAction=Command("拆除","erase",()=>OrderSelectedObject("erase"),"安排居民拆除选中的建筑并回收材料；开始前可取消。");
        _chopSelectionAction=Command("伐木","chop",()=>OrderSelectedObject("chop"),"安排居民砍倒这棵树，产出木料和树苗；开始前可取消。");
        _cancelConstructionAction=Command("取消施工","cancel",CancelSelectedConstruction,"撤销选中的未完成任务，释放预留材料；已拆除或挖掉的部分不会恢复。");
        _cancelDigRegionAction=Command("取消开采","cancel",CancelSelectedDigRegion,"撤销这片开采区域的全部剩余任务；已挖部分不会填回，其他区域不受影响。");
        _cancelChopAction=Command("取消伐木","cancel",()=>
        {
            if(_selectedCell is Cell c)foreach(var job in _world.Jobs.Where(j=>j.Chop&&j.Cell==c).ToArray())_world.CancelJob(job);
            SyncWorld();RefreshUi();
        },"撤销这棵树的伐木任务；已经砍倒的树不会恢复。");
        foreach(var (button,icon) in new[]{(_medicalBedAction,"bed"),(_waterPointAction,"water_bucket"),(_storageAction,"stockpile")})
        {button.Reparent(_objectCommands);StyleObjectCommand(button,icon);}
        _selectionPanel.VisibilityChanged+=()=>{if(!_selectionPanel.Visible)_objectCommands.Hide();};
        BuildItemPlacementCommands();
        _objectCommands.Hide();
    }
    private static void StyleObjectCommand(Button button,string icon)
    {
        button.Icon=UiIcons.Get(icon);button.ExpandIcon=true;
        button.IconAlignment=HorizontalAlignment.Center;button.VerticalIconAlignment=VerticalAlignment.Top;button.Alignment=HorizontalAlignment.Center;
        button.CustomMinimumSize=new(96,100);button.AddThemeConstantOverride("icon_max_width",64);
        button.AddThemeStyleboxOverride("normal",ButtonStyle("24333d"));
        button.AddThemeStyleboxOverride("hover",ButtonStyle("354b59"));
    }
    private void OrderSelectedObject(string command)
    {
        if(_selectedWorker>=0||_selectedCell is not Cell cell||!CurrentView.Known(_world,cell))return;
        // Selection commands always queue work, including in the paused editor.
        // Recheck eligibility because the simulation may have advanced since the UI refresh.
        if(command=="dig"&&!_world.Trees.ContainsKey(cell)&&_world.CanDig(cell))_world.QueueDig(cell);
        else if(command=="chop"&&_world.Trees.ContainsKey(cell))_world.QueueTree(cell,true);
        else if(command=="erase"&&_world.Buildings.ContainsKey(cell)&&!_world.Jobs.Any(j=>j.Construction&&j.Cell==cell))_world.QueueDemolish(cell);
        SyncWorld();RefreshUi();
    }
    private void RefreshObjectCommands()
    {
        foreach(var button in new[]{_medicalBedAction,_waterPointAction,_gateAction})UiAccess.Name(button,button.Text,button.TooltipText);
        _locateSelectionAction.Visible=_selectedWorker>=0;
        _productionBillAction.Visible=_selectedCell is Cell station&&_world.Buildings.TryGetValue(station,out var bench)&&_world.Content.Recipes.Any(r=>r.Station==bench.Kind);
        _harvestPlantAction.Hide();_cancelHarvestPlantAction.Hide();
        if(_selectedWorker<0&&_selectedCell is Cell pc&&CurrentView.Known(_world,pc)&&_world.Plants.TryGetValue(pc,out var plant))
        {bool pending=_world.Jobs.At(pc).Any(j=>j.Harvest);_harvestPlantAction.Visible=!pending&&plant.Growth>=DemoWorld.GrowTime(plant.Crop);_cancelHarvestPlantAction.Visible=pending;}
        _mineSelectionAction.Hide();_demolishAction.Hide();_chopSelectionAction.Hide();_cancelChopAction.Hide();
        if(_selectedWorker<0&&_selectedCell is Cell cell&&CurrentView.Known(_world,cell)&&SelectedGrowingZone is null&&SelectedDigRegion is null)
        {
            bool queued=_world.Jobs.Any(j=>j.Construction&&j.Cell==cell);
            bool tree=_world.Trees.ContainsKey(cell);
            bool chop=_world.Jobs.Any(j=>j.Chop&&j.Cell==cell);
            _chopSelectionAction.Visible=tree&&!chop&&!queued;
            _cancelChopAction.Visible=tree&&chop;
            _demolishAction.Visible=!queued&&_world.Buildings.ContainsKey(cell);
            _mineSelectionAction.Visible=!tree&&!queued&&_world.CanDig(cell);
        }
        _objectCommands.Visible=_selectionPanel.Visible&&_selectedWorker<0&&_objectCommands.GetChildren().OfType<Button>().Any(b=>b.Visible);
        bool objectSelected=_selectedWorker<0&&_selectedCell is not null;
        _selectionTitle.Visible=objectSelected;
        if(objectSelected)
        {
            var lines=_selection.Text.Split('\n',2);_selectionTitle.Text=lines[0];
            _selection.Text=lines.Length>1?lines[1]:"";
        }
        RefreshItemPlacementCommands();
        _selection.Visible=_selection.Text.Length>0;
        float width=objectSelected?320:400;
        _selectionPanel.CustomMinimumSize=new(width,objectSelected?180:0);
        _selectionPanel.OffsetLeft=objectSelected?0:12;_selectionPanel.OffsetRight=_selectionPanel.OffsetLeft+width;
        float height=_selectionPanel.GetCombinedMinimumSize().Y;
        _selectionPanel.OffsetTop=-48-height;_selectionPanel.OffsetBottom=-48;
    }
}

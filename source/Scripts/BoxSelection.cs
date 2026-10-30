using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private readonly HashSet<string> _selectedResidents=new();
    private DemoWorld? _residentSelectionWorld,_leftWorld;
    private Vector2? _leftStart;
    private Cell? _leftCell,_leftEnd;
    private string _leftTool="",_leftHint="";
    private bool _leftMoved,_leftAdditive;
    private Panel? _marquee;
    private static bool AreaTool(string tool)=>tool is "growingzone" or "unfarm" or "stockpile" or "unstockpile" or "foundation" or "chop" or "placeblock";
    private bool GestureTest=>OS.GetCmdlineUserArgs().Any(a=>a is "--camera-cutaway-test" or "--object-commands-test" or "--dig-volume-test" or "--interaction-test" or "--box-selection-test" or "--construction-line-test" or "--construction-preview-test" or "--designation-test");
    private bool IsResidentSelected(int i)=>_selectedWorker>=0&&_selectedWorker<_world.Workers.Count&&
        (i==_selectedWorker||_residentSelectionWorld==_world&&_selectedResidents.Contains(_world.Workers[_selectedWorker].Id)&&_selectedResidents.Contains(_world.Workers[i].Id));
    private int[] SelectedResidents()=>Enumerable.Range(0,_world.Workers.Count).Where(IsResidentSelected).ToArray();
    private void ClearResidentSelection()
    {_selectedResidents.Clear();_residentSelectionWorld=_world;_selectedWorker=-1;_selectedCell=null;_selectionPanel.Hide();}
    private void SetResidentSelection(IEnumerable<int> indices)
    {
        var selected=indices.Distinct().Where(i=>i>=0&&i<_world.Workers.Count).Order().ToArray();
        if(selected.Length==0){ClearResidentSelection();RefreshUi();return;}
        SelectResident(selected[0]);
        foreach(int i in selected)_selectedResidents.Add(_world.Workers[i].Id);
        RefreshUi();
    }
    private void BeginLeftSelection(Vector2 position,bool additive)
    {
        if(_orbitHeld||_dragging)return;
        _leftStart=position;_leftMoved=false;_leftAdditive=additive;_leftTool=_tool;_leftWorld=_world;_leftHint=_hint.Text;
        _leftCell=AreaTool(_tool)||LineBuildTool(_tool)?PickForTool(position):null;_leftEnd=_leftCell;
    }
    private void CancelLeftSelection()
    {
        if(_leftStart is not null)_hint.Text=_leftHint;
        _leftStart=null;_leftCell=null;_leftEnd=null;_leftMoved=false;
        _marquee?.Hide();_hoverCell=null;ClearHover();
    }
    private void DrawMarquee(Vector2 end)
    {
        if(_marquee is null)
        {
            var layer=new CanvasLayer{Layer=5};AddChild(layer);
            _marquee=new Panel{MouseFilter=Control.MouseFilterEnum.Ignore,FocusMode=Control.FocusModeEnum.None};layer.AddChild(_marquee);
            _marquee.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new Color(.4f,.7f,.85f,.13f),BorderColor=new("c2dbdf"),BorderWidthLeft=1,BorderWidthRight=1,BorderWidthTop=1,BorderWidthBottom=1});
        }
        var rect=new Rect2(_leftStart!.Value,end-_leftStart.Value).Abs();
        _marquee.Position=rect.Position;_marquee.Size=rect.Size;_marquee.Show();
    }
    private bool HandleLeftSelection(InputEvent input)
    {
        if(_leftStart is not Vector2 start)return false;
        if(_leftWorld!=_world||_tool!=_leftTool){CancelLeftSelection();return false;}
        if(input is InputEventKey{Pressed:true,Keycode:Key.Escape}||input is InputEventMouseButton{Pressed:true,ButtonIndex:MouseButton.Right})
        {CancelLeftSelection();GetViewport().SetInputAsHandled();return true;}
        if(input is InputEventMouseMotion move)
        {
            _leftMoved|=start.DistanceTo(move.Position)>6;
            if(_leftMoved)
            {
                if(_leftTool=="select")DrawMarquee(move.Position);
                else if(_leftCell is Cell a)_leftEnd=LineBuildTool(_leftTool)?LineEndPick(move.Position,a):PickForTool(move.Position)??PlanePick(move.Position,a.Level);
                _hoverCell=null;
            }
            GetViewport().SetInputAsHandled();return true;
        }
        if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false} release)
        {
            bool moved=_leftMoved||start.DistanceTo(release.Position)>6,additive=_leftAdditive;
            string tool=_leftTool,hint=_leftHint;var a=_leftCell;var b=_leftEnd;
            if(moved&&a is Cell anchor)b=LineBuildTool(tool)?LineEndPick(release.Position,anchor):PickForTool(release.Position)??PlanePick(release.Position,anchor.Level);
            CancelLeftSelection();
            if(GetViewport().GuiGetHoveredControl() is null&&GetViewport().GetVisibleRect().HasPoint(release.Position))
            {
                if(tool=="select")
                {
                    if(moved)
                    {
                        var rect=new Rect2(start,release.Position-start).Abs();var hits=new List<int>();
                        for(int i=0;i<_people.Count;i++)
                        {
                            if(!_people[i].Visible)continue;
                            var point=_people[i].Position+Vector3.Up*.7f;
                            if(_camera.IsPositionBehind(point))continue;
                            var screen=_camera.UnprojectPosition(point);if(!rect.HasPoint(screen))continue;
                            var hit=PickObject(screen,11);
                            if(hit.Count>0&&_camera.ProjectRayOrigin(screen).DistanceTo(hit["position"].AsVector3())+.08f<_camera.ProjectRayOrigin(screen).DistanceTo(point))continue;
                            hits.Add(i);
                        }
                        SetResidentSelection(additive?SelectedResidents().Concat(hits):hits);
                    }
                    else if(PickResident(release.Position) is int person)
                        SetResidentSelection(additive?(IsResidentSelected(person)?SelectedResidents().Where(i=>i!=person):SelectedResidents().Append(person)):new[]{person});
                    else if(!additive&&SelectDroppedItem(release.Position)){}
                    else if(!additive)
                    {ClearResidentSelection();if(PickForTool(release.Position) is Cell cell)Act(cell);else RefreshUi();}
                }
                else if(moved&&a is Cell from&&b is Cell to)
                {
                    if(LineBuildTool(tool))CommitBuildLine(from,to);
                    else if(tool=="placeblock")CommitPlacementArea(from,to);
                    else if(tool=="chop")CommitChopArea(from,to);
                    else
                    {
                        _farmStart=null;_stockpileStart=null;_foundationStart=null;
                        Act(from);Act(to);if(_tool==tool)_hint.Text=hint;
                    }
                }
                else if(!moved&&PickForTool(release.Position) is Cell cell)Act(cell);
            }
            GetViewport().SetInputAsHandled();return true;
        }
        if(input is InputEventKey{Pressed:true} key&&key.Keycode!=Key.Shift||input is InputEventMouseButton{Pressed:true})CancelLeftSelection();
        return false;
    }
    private bool UpdateLeftSelectionPreview()
    {
        if(_leftStart is null)return false;
        if(_leftWorld!=_world||(!GetWindow().HasFocus()&&!GestureTest)||DossierOpen||_consolePanel.Visible||_characterEditor.Visible||_titleDialog?.Visible==true||_removeGrowingDialog?.Visible==true||_resetDialog.Visible||_loadDialog?.Visible==true)
        {CancelLeftSelection();return false;}
        if(!_leftMoved)return false;
        if(_leftTool=="select"){ClearHover();return true;}
        if(_leftCell is Cell a&&_leftEnd is Cell b)
        {
            if(_hoverCell==b)return true;
            _hoverCell=b;ClearHover();
            if(LineBuildTool(_leftTool)){DrawBuildLine(a,b);return true;}
            float width=Math.Abs(a.X-b.X)+1,depth=Math.Abs(a.Z-b.Z)+1;
            var center=WorldPosition(a)+new Vector3((b.X-a.X)*.5f,.045f,(b.Z-a.Z)*.5f);
            int limit=_leftTool is "stockpile" or "unstockpile"?31:16;
            var material=PreviewMaterial(_leftTool!="chop"&&(a.Level!=b.Level||(_leftTool!="foundation"&&(width>limit||depth>limit))));
            foreach(float side in new[]{-1f,1f})
            {Box(_hover,center+new Vector3(side*width/2,0,0),new Vector3(.04f,.04f,depth),material);Box(_hover,center+new Vector3(0,0,side*depth/2),new Vector3(width,.04f,.04f),material);}
            _hint.Text=_leftTool=="chop"?$"伐木范围 {width} × {depth} · {ChopAreaTrees(a,b).Count()} 棵树 · 松开确认，右键或 Esc 取消":$"范围 {width} × {depth} · 松开左键确认 · 右键或 Esc 取消";
        }
        return true;
    }
    private static readonly StandardMaterial3D SelectionRingMaterial=new(){AlbedoColor=new("e4ce88"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
    private static readonly TorusMesh SelectionRingMesh=new(){InnerRadius=.32f,OuterRadius=.36f,Rings=16,RingSegments=4,Material=SelectionRingMaterial};
    private static void AddSelectionRing(Node3D person)=>person.AddChild(new MeshInstance3D{Name="SelectionRing",Mesh=SelectionRingMesh,Position=new(0,.04f,0),Visible=false,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    private void RefreshGroupSelection()
    {
        var selected=SelectedResidents();if(selected.Length<2)return;
        _editCharacter.Hide();_residentDetails.Hide();_selectionTabs.Hide();
        _selection.Text=$"已选中 {selected.Length} 位居民 · 征召 {selected.Count(i=>_world.Workers[i].Combat.Drafted)} 人\n"+
            string.Join("、",selected.Take(6).Select(i=>_world.Workers[i].Name))+(selected.Length>6?"等":"")+
            "\nR 征召 / 解除；右键地面移动、敌人攻击。\nShift＋点击切换成员，Shift＋拖框追加。";
    }
    private void DraftSelectedResidents()
    {
        var selected=SelectedResidents();bool draft=selected.Any(i=>!_world.Workers[i].Combat.Drafted);
        foreach(int i in selected)_world.Log(_world.DraftResident(_world.Workers[i].Id,draft).Message);
        RefreshUi();
    }
    private void StopSelectedResidents()
    {foreach(int i in SelectedResidents().Where(i=>_world.Workers[i].Combat.Drafted))_world.Log(_world.MilitaryStop(_world.Workers[i].Id).Message);RefreshUi();}
    private void OrderSelectedResidents(Vector2 position)
    {
        var selected=SelectedResidents();var enemyHit=PickObject(position,16);
        if(enemyHit.Count>0&&enemyHit["collider"].AsGodotObject() is {} enemy&&enemy.HasMeta("enemy"))
        {
            int ordered=0;foreach(int i in selected.Where(i=>_world.Workers[i].Combat.Drafted))
                if(_world.MilitaryAttack(_world.Workers[i].Id,enemy.GetMeta("enemy").AsString()).Ok)ordered++;
            _world.Log($"已向 {ordered} 位征召居民下达攻击命令。");RefreshUi();return;
        }
        if(Pick(position) is not Cell destination)return;
        int radius=Math.Min(32,(int)Math.Ceiling(Math.Sqrt(selected.Length))+1);
        var places=(from x in Enumerable.Range(-radius,radius*2+1) from z in Enumerable.Range(-radius,radius*2+1)
            let cell=destination+new Cell(x,z) where DemoWorld.Inside(cell)&&_world.Walkable(cell)
            orderby x*x+z*z,x,z select cell).ToArray();
        int moved=0,skipped=0;
        foreach(int i in selected)
        {
            var worker=_world.Workers[i];var goal=places.Length>moved?places[moved]:destination;
            if(worker.Combat.Drafted)
            {if(_world.MilitaryMove(worker.Id,goal).Ok)moved++;else skipped++;continue;}
            if(_world.CivilianAlert||worker.Person.Incapacitated||worker.Person.Mobility<.05||worker.Job is not null||worker.Person.Activity is not null)
            {skipped++;continue;}
            var path=_world.FindPath(worker.Tile,goal);if(path is null){skipped++;continue;}
            worker.Path.Clear();foreach(var step in path)worker.Path.Enqueue(step);moved++;
        }
        _world.Log($"移动命令：{moved} 人；跳过 {skipped} 人（工作中、无法行动、集合警报或路径不可达）。");RefreshUi();
    }
}

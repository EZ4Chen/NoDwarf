using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private async void CaptureResidentPreview()
    {
        try
        {
            async System.Threading.Tasks.Task Frame()
            {await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            void Click(Control control)
            {
                var point=control.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion{Position=point,GlobalPosition=point},true);
                GetViewport().PushInput(new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true},true);
                GetViewport().PushInput(new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false},true);
            }
            async System.Threading.Tasks.Task Page(int page)
            {
                if(page>=6)Click(_colonyTabs[page]);
                else
                {
                    if(ColonyPage){Click(_residentClose);await Frame();Click(_residentDetails);await Frame();}
                    Click(_residentTabs[page]);
                }
                await Frame();
            }
            async System.Threading.Tasks.Task Capture(string file)
            {
                await Frame();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var viewport=GetViewport().GetVisibleRect();var panel=_residentPanel.GetGlobalRect();
                if(!viewport.Encloses(panel))throw new InvalidOperationException("Dossier exceeds screen: "+panel+" / "+viewport);
                foreach(var scroll in _residentScrolls)
                    if(scroll.GetChild<Control>(0).Size.X>scroll.Size.X+1)throw new InvalidOperationException("Horizontal page overflow: "+_dossierPage);
                if(_dossierPage is 7 or 8 && _world.Workers.Count<=3 && _residentScrolls.Any(scroll=>scroll.GetVScrollBar().MaxValue>scroll.GetVScrollBar().Page+1))
                    throw new InvalidOperationException("Three resident planning rows must fit without scrolling: page="+_dossierPage+" sizes="+string.Join(",",_residentScrolls.Select(s=>$"{s.Size}/{s.GetVScrollBar().MaxValue}/{s.GetVScrollBar().Page}")));
                if(GetViewport().GetTexture().GetImage().SavePng($"res://.local/{file}.png")!=Error.Ok)throw new IOException("Dossier screenshot failed");
            }
            await Frame();
            var resident=_world.Workers[0];resident.Person.Earn("mining",1500);
            _world.InjureResident(resident.Id,"left_hand","cut",24);
            resident.Person.Feeling("preview","与同伴愉快交流",5,_world.SimulationTime+120);
            SelectResident(0);await Frame();Click(_residentDetails);await Frame();
            if(!_clock.Paused||!DossierOpen)throw new InvalidOperationException("Dossier must pause simulation");
            // Purchase through a real skill row and talent button, not a direct command call.
            Click(_residentSkillRows["mining"]);await Frame();
            if(_dossierPage!=5)throw new InvalidOperationException("Skill row must open its growth page");
            int before=resident.Person.Points;Click(_residentTalentButtons["mining.1"]);await Frame();
            if(resident.Person.Points!=before-1)throw new InvalidOperationException("Talent click failed");
            for(int i=0;i<_residentTabs.Count;i++)
            {
                await Page(i);
                if(_dossierPage!=i)throw new InvalidOperationException("Tab click failed: "+i);
                if(i==0)
                {
                    var bounds=_residentBody.GetParent<Control>().GetGlobalRect();
                    if(_residentSkillRows.Count!=11||_residentSkillRows.Values.Any(b=>!bounds.Encloses(b.GetGlobalRect())))throw new InvalidOperationException("All eleven skills must fit without scrolling");
                    foreach(var control in _residentBody.FindChildren("*","Control",true,false).OfType<Control>())
                        if(control is Label or Godot.Button or ProgressBar && !bounds.Encloses(control.GetGlobalRect()))
                            throw new InvalidOperationException("Bio content is clipped: "+control.Name+" "+control.GetGlobalRect());
                }
                await Capture("resident-"+i);
                if(i==2&&resident.Person.Injuries.Count==0&&_residentScrolls[0].GetVScrollBar().MaxValue>_residentScrolls[0].GetVScrollBar().Page+1)
                    throw new InvalidOperationException("All ten body parts should fit without scrolling");
            }
            await Page(2);
            _world.InfectResident(resident.Id,"fever");resident.Person.Diseases[0].Progress=145;resident.Person.Diseases[0].Immunity=72;
            resident.Person.Diseases[0].Treatment=new(){Quality=.8,Remaining=400};
            resident.Person.Malnutrition=120;resident.Person.Blood=65;
            _world.InjureResident(resident.Id,"left_leg","fracture",85);
            RenderResidentPanel();await Frame();
            if(_healthExpandedKey!=""||_healthAllParts||_healthShownParts!=0||_residentBody.FindChildren("*","ProgressBar",true,false).Count!=0)
                throw new InvalidOperationException("Health starts collapsed without progress bars or healthy body rows");
            if(_healthRows.Count!=HealthConditions.States(resident.Person).Count()||_healthRows.Values.Any(b=>string.IsNullOrEmpty(b.AccessibilityName)))
                throw new InvalidOperationException("Health abnormal rows must all have accessible names");
            await Capture("resident-health-compact");
            Click(_healthRows[":fever"]);await Frame();
            if(_healthExpandedKey!=":fever"||_residentBody.FindChildren("*","ProgressBar",true,false).Count!=2)
                throw new InvalidOperationException("Disease expands both progress and immunity");
            await Capture("resident-health-progress");
            Click(_healthRows[":fever"]);await Frame();
            if(_healthExpandedKey!=""||_residentBody.FindChildren("*","ProgressBar",true,false).Count!=0)throw new InvalidOperationException("Disease folds again");
            Click(_healthPartsToggle);await Frame();
            if(!_healthAllParts||_healthShownParts!=10||_residentScrolls.Last().ScrollVertical==0)throw new InvalidOperationException("All ten body parts expand");
            await Capture("resident-health-body-parts");
            Click(_healthPartsToggle);await Frame();if(_healthShownParts!=0)throw new InvalidOperationException("Body list folds again");
            Click(_residentNext);await Frame();
            if(_healthExpandedKey!=""||_healthAllParts||_healthRows.Count!=0)throw new InvalidOperationException("Healthy next resident has fresh collapsed state");
            SelectDossierResident(0);await Frame();
            resident.Person.Diseases.Clear();resident.Person.Malnutrition=0;resident.Person.Blood=100;
            await Page(7);
            Click(_scheduleBrushes["sleep"]);await Frame();
            Click(_scheduleCells[(resident.Id,22)]);await Frame();
            if(resident.Person.Schedule[22]!="sleep")throw new InvalidOperationException("Schedule paint click failed");
            var startPaint=_scheduleCells[(resident.Id,21)].GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseButton{Position=startPaint,GlobalPosition=startPaint,ButtonIndex=MouseButton.Left,Pressed=true},true);
            var paintPoint=_scheduleCells[(resident.Id,23)].GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion{Position=paintPoint,GlobalPosition=paintPoint,ButtonMask=MouseButtonMask.Left},true);await Frame();
            GetViewport().PushInput(new InputEventMouseButton{Position=paintPoint,GlobalPosition=paintPoint,ButtonIndex=MouseButton.Left,Pressed=false},true);
            if(resident.Person.Schedule[21]!="sleep"||resident.Person.Schedule[23]!="sleep")throw new InvalidOperationException("Held schedule drag paint failed");
            ApplyDay(ResidentWork.DayShift());await Frame();await Capture("resident-schedule");
            await Page(8);
            int prior=resident.Person.WorkPriorities["research"];
            Click(_priorityCells[(resident.Id,"research")]);await Frame();
            if(resident.Person.WorkPriorities["research"]!=(prior+4)%5)throw new InvalidOperationException("Work priority click failed");
            var priorityPoint=_priorityCells[(resident.Id,"research")].GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseButton{Position=priorityPoint,GlobalPosition=priorityPoint,ButtonIndex=MouseButton.Right,Pressed=true},true);
            GetViewport().PushInput(new InputEventMouseButton{Position=priorityPoint,GlobalPosition=priorityPoint,ButtonIndex=MouseButton.Right,Pressed=false},true);await Frame();
            if(resident.Person.WorkPriorities["research"]!=prior)throw new InvalidOperationException("Reverse work priority click failed");
            await Capture("resident-work-priorities");
            await Page(6);
            Click(_residentResearchButtons["precision"]);await Frame();
            if(_dossierResearch!="precision")throw new InvalidOperationException("Research node selection failed");
            await Capture("resident-research-locked");
            await Page(2);Click(_residentNext);await Frame();
            if(_dossierId!=_world.Workers[1].Id||_dossierPage!=2)throw new InvalidOperationException("Switching residents must preserve current tab");
            await Capture("resident-health-healthy");
            Click(_residentClose);await Frame();if(DossierOpen||_clock.Paused)throw new InvalidOperationException("Close must restore running state");
            _clock.Paused=true;OpenResidentPanel();await Frame();
            GetViewport().PushInput(new InputEventKey{Keycode=Key.Escape,Pressed=true},true);await Frame();
            if(DossierOpen||!_clock.Paused)throw new InvalidOperationException("Escape must preserve prior pause");
            _world.DeveloperResidents(false);CreatePeople();_selectedWorker=-1;RefreshUi();await Frame();
            Click(_colonyTabs[8]);await Frame();
            if(!DossierOpen||_dossierPage!=8)throw new InvalidOperationException("Empty colony work panel must open");
            Click(_residentNext);await Frame();
            Click(_colonyTabs[7]);await Frame();
            if(!DossierOpen||_dossierPage!=7)throw new InvalidOperationException("Bottom navigation must remain reachable");
            Click(_colonyTabs[7]);await Frame();
            if(DossierOpen||!_clock.Paused)throw new InvalidOperationException("Active colony tab must close without unpausing");
            var medicalCell=new Cell(60,60,0);_world.Buildings[medicalCell]=new(medicalCell,BuildKind.Bed);
            _selectedCell=medicalCell;_selectionPanel.Show();RefreshUi();await Frame();
            if(!_medicalBedAction.IsVisibleInTree())throw new InvalidOperationException("Medical bed action must be visible for a built bed");
            Click(_medicalBedAction);await Frame();if(!_world.IsMedicalBed(medicalCell))throw new InvalidOperationException("Medical bed designation button");
            Click(_medicalBedAction);await Frame();if(_world.IsMedicalBed(medicalCell))throw new InvalidOperationException("Medical bed designation released");
            GD.Print("RESIDENT_UI_OK: six person tabs + three colony tabs, skill purchase, schedule click/drag, work priority left/right clicks, resident switching, bounds and pause");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

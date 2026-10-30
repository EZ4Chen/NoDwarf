using Godot;
using NoDwarf.Simulation;
using NoDwarf.Services.Llm;
namespace NoDwarf;
public partial class Main
{
    private DialogueDirector? _dialogueDirector;
    private Window? _dialogueWindow;
    private Label? _dialogueStatus,_dialogueText;
    private LineEdit? _dialogueSearch;
    private OptionButton? _dialoguePerson;
    private string _lastDialogue="";
    private double _speechUntil,_journalRefreshAt;
    private readonly Queue<DialogueLine> _speechQueue=new();
    private string _speechSpeaker="",_speechText="";
    private DemoWorld? _dialogueWorld;
    private void PumpDialogues(bool advance)
    {
        if(_dialogueWorld!=_world)
        {
            _dialogueDirector?.Dispose();_dialogueDirector=null;_dialogueWorld=_world;
            _autosaveElapsed=0;_observedRaid=_world.Enemies.Count;
            _lastDialogue=_world.Dialogues.Records.LastOrDefault()?.Id??"";_speechQueue.Clear();_speechText="";
            _world.DialogueSettings=PreferencesRuntime.Current.Dialogue;
            _dialogueWindow?.QueueFree();_dialogueWindow=null;
        }
        if(_world.DialogueSettings.Enabled||_dialogueDirector is not null)
        {
            _dialogueDirector??=new(LlmRuntime.Client);
            _dialogueDirector.Pump(_world,LlmRuntime.Configuration,advance,_storyDirector?.InFlight!=true&&_residentMindDirector?.InFlight!=true);
        }
        var last=_world.Dialogues.Records.LastOrDefault();
        if(last is not null&&last.Id!=_lastDialogue)
        {foreach(var line in last.Lines)_speechQueue.Enqueue(line);_lastDialogue=last.Id;RefreshDialogueJournal();}
        double now=Time.GetTicksMsec()/1000d;
        if(now>=_speechUntil)
        {
            if(_speechQueue.TryDequeue(out var line)){_speechSpeaker=line.Speaker;_speechText=line.Text;_speechUntil=now+Math.Clamp(line.Text.Length*.15,4,12);}
            else _speechText="";
        }
        for(int i=0;i<_people.Count;i++)
        {
            var bubble=_people[i].GetNodeOrNull<Label3D>("Speech");
            bool visible=_speechText!=""&&_world.Workers[i].Id==_speechSpeaker;
            if(bubble is null&&visible)
            {bubble=new Label3D{Name="Speech",Position=new(0,2.5f,0),Font=GameFont.Regular,FontSize=30,PixelSize=.006f,OutlineSize=10,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,AutowrapMode=TextServer.AutowrapMode.WordSmart,Width=420};_people[i].AddChild(bubble);}
            if(bubble is not null){bubble.Visible=visible;bubble.Text=visible?_speechText:"";}
        }
        if(_dialogueWindow?.Visible==true&&_dialogueStatus is not null){_dialogueStatus.Text=DialogueStatus();if(now>=_journalRefreshAt){_journalRefreshAt=now+.5;RefreshDialogueJournal();}}
    }
    private string DialogueStatus()=>!_world.DialogueSettings.Enabled?"居民对话未启用 · 可在设置 → 对话与记忆中开启":_world.Dialogues.Error!=""?_world.Dialogues.Error:
        _world.Dialogues.Turn?.Status switch{"requesting"=>_world.Dialogues.Turn.Kind=="summary"?"正在提炼旧对话":"居民正在交谈","received"=>"已收到回复，等待游戏继续","waiting"=>"等待手动重试",_=>"等待下一次交谈"};
    private void ShowDialogueJournal()
    {
        if(_dialogueWindow is not null){_dialogueWindow.PopupCentered();RefreshDialogueJournal();return;}
        _dialogueWindow=new Window{Title="对话与记忆账本",Size=new(760,560),MinSize=new(600,380),Transient=true,Exclusive=true,Theme=GameUi.CreateTheme()};AddChild(_dialogueWindow);
        _dialogueWindow.CloseRequested+=()=>_dialogueWindow.Hide();
        _dialogueWindow.WindowInput+=input=>{if(input is InputEventKey{Pressed:true,Echo:false,Keycode:Key.Escape}){_dialogueWindow.SetInputAsHandled();_dialogueWindow.Hide();}};
        var margin=new MarginContainer();_dialogueWindow.AddChild(margin);margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach(string side in new[]{"left","right","top","bottom"})margin.AddThemeConstantOverride("margin_"+side,16);
        var body=new VBoxContainer();body.AddThemeConstantOverride("separation",12);margin.AddChild(body);
        _dialogueStatus=Text(DialogueStatus(),14,Accent,true);_dialogueStatus.AccessibilityLive=AccessibilityServer.AccessibilityLiveMode.Polite;body.AddChild(_dialogueStatus);
        var filters=new HBoxContainer();body.AddChild(filters);
        _dialoguePerson=new OptionButton();_dialoguePerson.AddItem("所有居民");foreach(var w in _world.Workers)_dialoguePerson.AddItem(w.Name);filters.AddChild(_dialoguePerson);UiAccess.Name(_dialoguePerson,"筛选居民的对话记忆");
        _dialogueSearch=new LineEdit{PlaceholderText="搜索对话或摘要",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};filters.AddChild(_dialogueSearch);UiAccess.Name(_dialogueSearch,"搜索对话或摘要");
        _dialogueSearch.TextChanged+=_=>RefreshDialogueJournal();_dialoguePerson.ItemSelected+=_=>RefreshDialogueJournal();
        var scroll=new ScrollContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};body.AddChild(scroll);
        _dialogueText=Text("",16,Ink,true);_dialogueText.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;scroll.AddChild(_dialogueText);
        var footer=new HBoxContainer();body.AddChild(footer);
        UiAccess.Name(Button(footer,"重试中断请求",()=>{if(_world.Dialogues.Turn?.Status=="waiting"){_world.Dialogues.Turn=null;_world.Dialogues.Error="";_world.Dialogues.NextAt=_world.SimulationTime;}RefreshDialogueJournal();}),"重试中断的对话或记忆提炼","会使用共享 AI 请求额度，可能计费；不会重放已保存的完整对话。");
        Button(footer,"设置",()=>{_dialogueWindow.Hide();ShowGameSettings();});Button(footer,"关闭",()=>_dialogueWindow.Hide());
        _worldMenu.Hide();RefreshDialogueJournal();_dialogueWindow.PopupCentered();
    }
    private void RefreshDialogueJournal()
    {
        if(_dialogueWindow is null||_dialogueText is null)return;
        string Name(string id)=>_world.Workers.FirstOrDefault(w=>w.Id==id)?.Name??id;
        string selected=_dialoguePerson!.Selected>0&&_dialoguePerson.Selected<=_world.Workers.Count?_world.Workers[_dialoguePerson.Selected-1].Id:"";
        var records=_world.Dialogues.Records.Where(r=>selected==""||r.Participants.Contains(selected)).Reverse().Select(r=>
            $"{(r.IsLongTerm?"长期记忆":"短期记忆")} · 第 {r.Time/GameCalendar.SecondsPerDay+1:0.00} 日 · {r.Location}\n{string.Join("、",r.Participants.Select(Name))}\n"+
            (r.Summary!=""?"对话摘要（发言不等于事实）："+r.Summary:string.Join("\n",r.Lines.Select(l=>Name(l.Speaker)+"："+l.Text))));
        string search=_dialogueSearch!.Text.Trim();var filtered=records.Where(t=>search==""||t.Contains(search,StringComparison.OrdinalIgnoreCase)).ToArray();
        _dialogueText.Text=filtered.Length==0?"暂无匹配的对话。启用对话后，附近空闲居民会交谈；已完成的社交活动也会提供交谈机会。":string.Join("\n\n──────────\n\n",filtered);
    }
}

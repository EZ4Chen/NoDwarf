using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private void RenderResidentBio(DemoWorker w)
    {
        var p=w.Person;var(left,right)=RColumns(246);
        RSection(left,"背景");
        RStat(left,"童年",p.Childhood);RStat(left,"成年",p.Adulthood);
        RSection(left,"特性");
        foreach(var trait in p.Traits)
        {
            var parts=ResidentCatalog.Traits[trait].Split('：',2);
            RT(left,parts[0],16,RGreen);
            if(parts.Length>1)RT(left,parts[1],13,RMuted,true);
        }
        var spacer=new Control{SizeFlagsVertical=Control.SizeFlags.ExpandFill};left.AddChild(spacer);
        RSection(left,"身体与状态");
        RStat(left,"身体",p.Dead?"已死亡":p.Injuries.Count==0?"健康":$"{p.Injuries.Count} 处伤势",p.Injuries.Count==0?RGreen:RRed);
        RStat(left,"移动 / 操作",$"{p.Mobility:P0} / {p.Manipulation:P0}");
        RStat(left,"负重",$"{p.CarriedWeight:F1} / {p.Capacity:F0} kg");
        RSection(right,"技能","等级 0—20 · 点击查看成长");
        var list=RV(right,2);
        foreach(var skill in ResidentCatalog.Skills)ResidentSkillRow(list,w,skill);
        var tail=RH(right);tail.SizeFlagsVertical=Control.SizeFlags.ExpandFill;tail.Alignment=BoxContainer.AlignmentMode.End;
        RB(tail,$"可用技能点  {p.Points}  ›",()=>ShowResidentPage(5),"每 1000 成长经验获得 1 个通用技能点",148,true).SizeFlagsVertical=Control.SizeFlags.ShrinkEnd;
    }
    private void ResidentSkillRow(Node parent,DemoWorker w,ResidentSkill skill,bool selected=false)
    {
        var p=w.Person;int level=p.Level(skill.Id);long xp=p.SkillXp[skill.Id]-ResidentCatalog.LevelStart(level);
        var b=RB(parent,"",()=>{_dossierSkill=skill.Id;ShowResidentPage(5);},$"{skill.Name} {level} 级\n{(level==20?"已满级":$"升级进度 {xp} / {ResidentCatalog.LevelCost(level)}")}\n已学专长 {p.Talents.GetValueOrDefault(skill.Id)}/3"+(skill.HasWork?"":"\n对应玩法待接入"));
        b.CustomMinimumSize=new(0,24);b.AddThemeStyleboxOverride("normal",RStyle(selected?"3c4545":"262d2f",selected?"7e8b81":"262d2f",6,0));
        b.AddThemeStyleboxOverride("hover",RStyle("3d4748","79847b",6,0));
        var row=new HBoxContainer{MouseFilter=Control.MouseFilterEnum.Ignore};b.AddChild(row);row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);row.OffsetLeft=8;row.OffsetRight=-8;row.AddThemeConstantOverride("separation",10);
        var name=RT(row,skill.Name,14,skill.HasWork?RInk:RMuted);name.CustomMinimumSize=new(46,0);
        var meter=RMeter(row,level,20,new Color(skill.HasWork?"637b80":"444e50"),18);meter.SizeFlagsVertical=Control.SizeFlags.ShrinkCenter;
        var number=RT(row,level.ToString(),15,level>0?RInk:RMuted);number.CustomMinimumSize=new(26,0);number.HorizontalAlignment=HorizontalAlignment.Right;
        var rank=RT(row,!skill.HasWork?"待接入":p.Talents.GetValueOrDefault(skill.Id)>0?$"专长 {p.Talents[skill.Id]}":"",12,RGold);rank.CustomMinimumSize=new(48,0);rank.HorizontalAlignment=HorizontalAlignment.Right;
        _residentSkillRows[skill.Id]=b;
    }
    private void RenderResidentNeeds(DemoWorker w)
    {
        var p=w.Person;var(left,right)=RColumns(320);RSection(left,"需求");
        void Need(string name,double value,string hint,Color color)
        {
            var area=RV(left,5);area.TooltipText=hint;
            RStat(area,name,$"{value:F0}%",value<25?RRed:color);RMeter(area,value,100,value<25?RRed:color,14);
            var gap=new Control{CustomMinimumSize=new(0,8)};area.AddChild(gap);
        }
        Need("饱食",p.Hunger,"随身口粮不足时会寻找可达的地面口粮。饱食低于 20 会影响效率。",RGreen);
        Need("休息",p.Rest,"休息低于 15 时会主动休息。",RBlue);
        Need("娱乐",p.Recreation,"放松或与同伴交流可恢复娱乐。",RGold);
        Need("心情",p.Mood,"由当前需求、疼痛和短期感受共同决定。",RGreen);
        var commands=RH(left,6);RB(commands,"吃口粮",()=>ResidentDo("eat"));RB(commands,"休息",()=>ResidentDo("rest"));RB(commands,"放松",()=>ResidentDo("relax"));
        RB(left,"恢复普通工作",()=>ResidentDo("cancel"),"取消个人活动，回到聚落工作");
        RSection(right,"当前感受",p.Mood>=60?"心情平稳":p.Mood>=30?"有些低落":"情绪低落");
        var thoughts=RScroll(right);
        if(p.Pain>0)RStat(thoughts,"伤口疼痛",$"{p.Pain:F0} / 100",RRed);
        if(p.Hunger<20)RStat(thoughts,"饥饿","需要食物",RRed);
        if(p.Rest<20)RStat(thoughts,"疲惫","需要休息",RRed);
        foreach(var e in p.MoodEffects)
        {
            RStat(thoughts,e.Text,e.Amount.ToString("+0;-0;0"),e.Amount>=0?RGreen:RRed);
            RT(thoughts,$"剩余 {Math.Max(0,e.Until-_world.SimulationTime):F0} 秒",12,RMuted);
            RRule(thoughts);
        }
        if(p.MoodEffects.Count==0)RT(thoughts,"暂无短期心情变化。",14,RMuted,true);
    }
    private void RenderResidentHealth(DemoWorker w)
    {
        var p=w.Person;var(left,right)=RColumns(240);RSection(left,"身体能力",p.Dead?"已死亡":"");
        RStat(left,"意识",$"{p.Consciousness:P0}",p.Consciousness<1?RRed:RGreen);
        RStat(left,"移动",$"{p.Mobility:P0}",p.Mobility<1?RRed:RGreen);
        RStat(left,"操作",$"{p.Manipulation:P0}",p.Manipulation<1?RRed:RGreen);
        RStat(left,"血量",$"{p.Blood:F0}%",p.Blood<90?RRed:RGreen);
        RStat(left,"疼痛",p.Pain<1?"无":$"{p.Pain:F0}%",p.Pain<1?RGreen:RRed);
        RSection(left,"护理");
        var medicChoice=new OptionButton{CustomMinimumSize=new(0,30)};StyleResidentChoice(medicChoice);UiAccess.Name(medicChoice,"治疗人员");left.AddChild(medicChoice);
        var medics=_world.Workers.Where(m=>!m.Person.Dead).ToArray();
        foreach(var m in medics)medicChoice.AddItem(m==w?"自己护理":$"{m.Name} · 医疗 {m.Person.Level("medicine")} 级");
        var eligible=medics.Where(m=>!m.Person.Incapacitated&&!_world.MilitaryControlled(m)&&m.Person.Manipulation>=.05).OrderByDescending(m=>m.Person.Level("medicine")).FirstOrDefault();
        if(eligible is not null)medicChoice.Select(Array.IndexOf(medics,eligible));
        var care=RB(left,"安排护理",()=>{if(medicChoice.Selected>=0&&medicChoice.Selected<medics.Length)ResidentDo("treat",w.Id,actor:medics[medicChoice.Selected].Id);},"每处伤口消耗 1 份绷带；没有随身绷带时会先去公共库存取用。",0,true);
        void UpdateCare()
        {
            var medic=medicChoice.Selected>=0&&medicChoice.Selected<medics.Length?medics[medicChoice.Selected]:null;
            care.Disabled=p.Dead||!p.Injuries.Any(i=>!i.Treated&&i.Kind!="missing")||medic is null||medic.Person.Incapacitated||medic.Person.Manipulation<.05;
        }
        medicChoice.ItemSelected+=_=>UpdateCare();UpdateCare();
        RT(left,$"公共可用绷带：{_world.Available("bandage")} 份",13,RMuted,true);
        RT(left,"护理割伤可止血。骨折需护理后恢复；缺失部位无法恢复。",13,RMuted,true);
        RSection(right,"部位与伤势",$"{p.Injuries.Count} 处伤势");
        var list=RScroll(right);
        list.AddThemeConstantOverride("separation",2);
        foreach(var part in ResidentCatalog.Parts)
        {
            var injuries=p.Injuries.Where(i=>i.Part==part.Id).ToArray();
            var row=RH(list,12);row.CustomMinimumSize=new(0,24);
            var label=RT(row,part.Name,14,injuries.Length>0?RInk:RMuted);label.CustomMinimumSize=new(44,0);
            var condition=RT(row,injuries.Length==0?"健康":string.Join("、",injuries.Select(i=>ResidentCatalog.InjuryName(i.Kind))),14,injuries.Length>0?RRed:RMuted);condition.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
            RT(row,$"{p.Integrity(part.Id):P0}",14,injuries.Length>0?RRed:RMuted).CustomMinimumSize=new(44,0);
            var status=RT(row,injuries.Length==0?"—":injuries.All(i=>i.Treated)?"已护理":"待处理",13,injuries.Length==0?RMuted:injuries.All(i=>i.Treated)?RGreen:RRed);status.CustomMinimumSize=new(48,0);
            row.TooltipText=injuries.Length==0?"部位完好":string.Join("\n",injuries.Select(i=>$"{ResidentCatalog.InjuryName(i.Kind)} · 严重度 {i.Severity:F0} · {(i.Treated?"已护理":"未处理")}"));
            RRule(list);
        }
    }
    private void RenderResidentGear(DemoWorker w)
    {
        var p=w.Person;var(left,right)=RColumns(302);
        RSection(left,"穿戴装备");
        foreach(var slot in ResidentCatalog.Slots)
        {
            var item=_world.Inventory(w).FirstOrDefault(i=>i.Slot==slot.Id);var row=RH(left,8);row.CustomMinimumSize=new(0,34);
            var name=RT(row,slot.Name,13,RMuted);name.CustomMinimumSize=new(30,0);
            if(item is not null)
            {
                ResidentItemIcon(row,item.Item);
                var label=RT(row,ItemCatalog.Get(item.Item).Name,14);label.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
                row.TooltipText=GearDescription(item.Item);RB(row,"卸下",()=>ResidentDo("unequip",item:item.Id),"卸下后收入随身物品",45);
            }
            else RT(row,"—",14,RMuted).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        }
        RRule(left);RStat(left,"携带重量",$"{p.CarriedWeight:F1} / {p.Capacity:F0} kg",p.CarriedWeight>p.Capacity?RRed:RInk);
        RMeter(left,p.CarriedWeight,p.Capacity,RBlue,8);
        RT(left,"含装备、随身物品与任务货物。",12,RMuted,true);
        var switcher=RH(right,6);
        RB(switcher,"随身物品",()=>{_dossierNearby=false;RenderResidentPanel();},"",0,!_dossierNearby).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        RB(switcher,"附近物品",()=>{_dossierNearby=true;RenderResidentPanel();},"",0,_dossierNearby).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        var bag=RScroll(right);
        var items=(_dossierNearby?_world.NearbyItems(w).Take(24):_world.Inventory(w).Where(i=>i.Slot=="")).ToArray();
        foreach(var item in items)
        {
            var row=RH(bag,8);row.CustomMinimumSize=new(0,36);ResidentItemIcon(row,item.Item);
            var name=RT(row,$"{ItemCatalog.Get(item.Item).Name} ×{item.Count}",14);name.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
            row.TooltipText=GearDescription(item.Item)+(_dossierNearby?$"\n位置 {item.Cell.X}, {item.Cell.Z}, {item.Cell.Level}":"");
            RT(row,$"{item.Count*ResidentGear.Weight(item.Item):F1} kg",12,RMuted);
            if(_dossierNearby)RB(row,"拾取",()=>ResidentDo("pickup",item:item.Id));
            else
            {
                if(ResidentGear.Get(item.Item) is not null)RB(row,"装备",()=>ResidentDo("equip",item:item.Id));
                RB(row,"放下",()=>ResidentDo("drop",item:item.Id));
            }
            RRule(bag);
        }
        if(items.Length==0)RT(bag,_dossierNearby?"附近没有可拾取的物品。":"没有未装备的随身物品。",14,RMuted,true);
        if(!_dossierNearby&&w.Job is {} job&&job.Materials.Any(r=>r.Stage==1))
        {
            RSection(bag,"任务货物");foreach(var cargo in job.Materials.Where(r=>r.Stage==1))RStat(bag,ItemCatalog.Get(cargo.Item).Name,$"×{cargo.Count}");
        }
        RT(right,_dossierNearby?"实际前往拾取 · 最多 5 件，装备 1 件":"换装会将旧装备收入物品栏。",12,RMuted,true);
    }
    private static void ResidentItemIcon(Node parent,string item)
    {
        parent.AddChild(new TextureRect{Texture=EquipmentArt.Icon(item)??UiIcons.Get(item),CustomMinimumSize=new(26,26),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,MouseFilter=Control.MouseFilterEnum.Ignore});
    }
    private void RenderResidentSocial(DemoWorker w)
    {
        var p=w.Person;var(left,right)=RColumns(300);RSection(left,"关系","此人对同伴的好感");
        var relations=RScroll(left);
        foreach(var other in _world.Workers.Where(other=>other!=w))
        {
            var relation=p.Relations.FirstOrDefault(r=>r.Target==other.Id);var row=RH(relations,8);
            var portrait=new TextureRect{Texture=_people[_world.Workers.IndexOf(other)].GetChild<Sprite3D>(0).Texture,CustomMinimumSize=new(32,42),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered};row.AddChild(portrait);
            var text=RV(row,2);text.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;RT(text,other.Name);RT(text,relation?.Kind??"同伴",12,RMuted);
            int opinion=relation?.Opinion??0;RT(row,opinion.ToString("+0;-0;0"),15,opinion>0?RGreen:opinion<0?RRed:RMuted);
            RB(row,"交流",()=>ResidentDo("social",other.Id),"前往交流，增加娱乐并留下经历");RRule(relations);
        }
        RSection(right,"重要经历");var history=RScroll(right);
        foreach(var memory in p.Memories)
        {
            var when=GameCalendar.At(Math.Min(memory.Time,_world.SimulationTime));
            RT(history,when.DateText+" · "+when.TimeText,12,RMuted);
            RT(history,memory.Text,14,RInk,true);RRule(history);
        }
        if(p.Memories.Count==0)RT(history,"尚无记录。",14,RMuted);
    }
    private void RenderResidentGrowth(DemoWorker w)
    {
        var p=w.Person;var(left,right)=RColumns(284);
        RSection(left,"技能",$"可用点数 {p.Points}");
        var list=RScroll(left);foreach(var s in ResidentCatalog.Skills)ResidentSkillRow(list,w,s,s.Id==_dossierSkill);
        var skill=ResidentCatalog.Skill(_dossierSkill);int level=p.Level(skill.Id);long xp=p.SkillXp[skill.Id]-ResidentCatalog.LevelStart(level);
        RSection(right,skill.Name+"专长",$"等级 {level}");
        var progress=RH(right);RT(progress,level==20?"已满级":$"{xp} / {ResidentCatalog.LevelCost(level)} 经验",13,RMuted).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        RT(progress,$"成长经验 {p.GrowthXp:N0}",13,RMuted);
        RMeter(right,level==20?1:xp,level==20?1:ResidentCatalog.LevelCost(level),RBlue,5);
        var nodes=RScroll(right);
        foreach(var node in ResidentCatalog.Talents.Where(n=>n.Skill==skill.Id))
        {
            bool learned=p.Talents.GetValueOrDefault(skill.Id)>=node.Rank;string issue=_world.TalentIssue(w,node);
            var frame=new PanelContainer();frame.AddThemeStyleboxOverride("panel",RStyle(learned?"2e3833":"282f31",learned?"61745d":"455052",10,8));nodes.AddChild(frame);
            var box=RV(frame,4);var top=RH(box);RT(top,$"{node.Rank:00}   {skill.Name}专长",15,learned?RGreen:RInk).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
            var learn=RB(top,learned?"已学习":$"学习 · {node.Cost} 点",()=>ResidentDo("talent",node.Id),issue==""?node.Effect:issue,92,issue=="");learn.Disabled=issue!="";_residentTalentButtons[node.Id]=learn;
            RT(box,node.Effect,13,RMuted);
            string gate=$"技能 {node.Level} 级"+(node.Research==""?"":$" · {ResidentCatalog.Research.First(r=>r.Id==node.Research).Name}");
            RT(box,learned?"已掌握":gate,12,learned?RGreen:issue==""?RGold:RMuted);
        }
        if(!skill.HasWork)RT(right,"对应玩法待接入，当前无法通过工作提升。",12,RGold,true);
        var route=RH(right);RT(route,"培养路线",13,RMuted);
        var choice=new OptionButton{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(0,29)};StyleResidentChoice(choice);UiAccess.Name(choice,"培养路线");route.AddChild(choice);choice.AddItem("手动选择");foreach(var s in ResidentCatalog.Skills)choice.AddItem(s.Name+"优先");
        choice.Select(Array.FindIndex(ResidentCatalog.Skills,s=>s.Id==p.AutoSkill)+1);choice.ItemSelected+=i=>ResidentDo("route",i==0?"":ResidentCatalog.Skills[(int)i-1].Id);choice.TooltipText="选择路线后，满足等级、点数与研究条件时自动加点。";
        RT(right,$"每 1000 成长经验获得 1 点 · 距下一点 {1000-p.GrowthXp%1000}",12,RMuted);
    }
    private void RenderResidentResearch(DemoWorker w)
    {
        var(left,right)=RColumns(346);RSection(left,"聚落研究树","所有居民共享");
        var tree=RV(left,10);tree.SizeFlagsVertical=Control.SizeFlags.ExpandFill;
        void Node(Node parent,string id)
        {
            var topic=ResidentCatalog.Research.First(r=>r.Id==id);bool done=_world.Research.Completed.Contains(id);bool locked=topic.Prerequisites.Any(p=>!_world.Research.Completed.Contains(p));
            var b=RB(parent,topic.Name+"\n"+(done?"已完成":locked?"未解锁":_world.Research.Active==id?"研究中":"可研究"),()=>{_dossierResearch=id;RenderResidentPanel();},$"所需研究进度 {topic.Cost}",152,id==_dossierResearch);
            b.CustomMinimumSize=new(152,62);b.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
            b.AddThemeColorOverride("font_color",done?RGreen:locked?RMuted:RGold);_residentResearchButtons[id]=b;
        }
        var first=RH(tree);first.Alignment=BoxContainer.AlignmentMode.Center;Node(first,"foundations");
        RT(tree,"↓",18,RMuted).HorizontalAlignment=HorizontalAlignment.Center;
        var middle=RH(tree,12);Node(middle,"industry");Node(middle,"living");
        RT(tree,"↓",18,RMuted).HorizontalAlignment=HorizontalAlignment.Center;
        var final=RH(tree);final.Alignment=BoxContainer.AlignmentMode.Center;Node(final,"precision");
        RT(left,"完成前置研究后，解锁下一层。",12,RMuted);
        var selected=ResidentCatalog.Research.First(r=>r.Id==_dossierResearch);bool complete=_world.Research.Completed.Contains(selected.Id);bool unavailable=selected.Prerequisites.Any(r=>!_world.Research.Completed.Contains(r));
        RSection(right,selected.Name,complete?"已完成":unavailable?"未解锁":_world.Research.Active==selected.Id?"当前项目":"可研究");
        string effect=selected.Id switch{"foundations"=>"解锁工艺改良与生活协作研究。","industry"=>"解锁采矿、建造、冶炼、锻造与木工的二级专长。","living"=>"解锁种植、烹饪、医疗、战斗、交涉与研究的二级专长。",_=>"解锁全部技能的三级专长。"};
        RT(right,effect,15,RInk,true);
        RStat(right,"研究进度",$"{_world.Research.Progress.GetValueOrDefault(selected.Id):F0} / {selected.Cost}");
        RMeter(right,_world.Research.Progress.GetValueOrDefault(selected.Id),selected.Cost,RGreen,12);
        RT(right,selected.Prerequisites.Length==0?"无前置要求":"前置："+string.Join("、",selected.Prerequisites.Select(id=>ResidentCatalog.Research.First(r=>r.Id==id).Name)),13,RMuted,true);
        var spacer=new Control{SizeFlagsVertical=Control.SizeFlags.ExpandFill};right.AddChild(spacer);
        RStat(right,"研究员",w.Name+" · 研究 "+w.Person.Level("research")+" 级");
        var choose=RB(right,_world.Research.Active==selected.Id?"当前研究项目":"设为当前研究",()=>ResidentDo("research_topic",selected.Id),"切换聚落的当前研究项目",0,true);choose.Disabled=complete||unavailable||_world.Research.Active==selected.Id;
        var start=RB(right,"安排此人研究",()=>ResidentDo("research"),"研究员推进当前项目，并获得研究经验。",0,true);start.Disabled=complete||unavailable||_world.Research.Active!=selected.Id||w.Person.Incapacitated;
        RT(right,"安排后点击「继续模拟」开始工作。",12,RMuted);
    }
}

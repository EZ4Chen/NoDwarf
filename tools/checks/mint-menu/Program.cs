using NoDwarf.MenuLogic;
static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
var entry=new MintEntry("wood_stairs","木楼梯","楼梯","NoDwarf",["wood","木料"],["连接上下层"]);
Check(MintMenuLogic.Matches(entry,"楼梯"),"Label/category search");
Check(MintMenuLogic.Matches(entry,"WOOD"),"Case-insensitive material search");
Check(MintMenuLogic.Matches(entry,"nodwarf"),"Source search");
Check(!MintMenuLogic.Matches(entry,"楼梯 NoDwarf"),"Do not match across separate definition fields");
Check(!MintMenuLogic.Matches(entry with{Visible=false},""),"Hidden entries must stay hidden for an empty query");
var rows=new[]{(Id:"none-a",Value:(double?)null),(Id:"low-a",Value:(double?)60),(Id:"high",Value:(double?)200),(Id:"low-b",Value:(double?)60),(Id:"none-b",Value:(double?)null)};
Check(string.Join(",",MintMenuLogic.Ordered(rows,x=>x.Value,true).Select(x=>x.Id))=="low-a,low-b,high,none-a,none-b","Ascending ties and missing stats must retain original order");
Check(string.Join(",",MintMenuLogic.Ordered(rows,x=>x.Value,false).Select(x=>x.Id))=="high,low-a,low-b,none-a,none-b","Missing stats must remain last even descending");
Check(string.Join(",",MintMenuLogic.Categories(new[]{"命令","家具","楼梯","生产"},c=>c is "楼梯" or "生产"))=="楼梯,生产,命令,家具","Pinned categories must preserve group order");
Console.WriteLine("MINT_LOGIC_OK: field matching, visibility, nullable-stat sorting, stable ties and category pinning; no Unity, Verse or Godot references");

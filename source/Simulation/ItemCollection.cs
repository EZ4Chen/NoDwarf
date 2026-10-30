using System.Collections.ObjectModel;
namespace NoDwarf.Simulation;

// All membership and visual-field mutations invalidate the equipment index, including
// direct editor/test changes and same-count replacement. Loose inventory never does.
public sealed class ItemCollection:Collection<ItemStack>
{
    private readonly List<ItemStack> list;
    private readonly Dictionary<ItemStack,int> subscriptions=new();
    private readonly HashSet<ItemStack> equipped=new();
    public long EquipmentVersion {get;private set;}
    public IEnumerable<ItemStack> EquippedItems {get{foreach(var item in equipped)yield return item;}}
    public ItemCollection():this(new List<ItemStack>()){}
    private ItemCollection(List<ItemStack> items):base(items)=>list=items;
    private void Changed(ItemStack item,bool wasEquipped)
    {
        if(item.Equipped)equipped.Add(item);else equipped.Remove(item);
        if(wasEquipped||item.Equipped)EquipmentVersion++;
    }
    private void Attach(ItemStack item)
    {
        if(subscriptions.TryGetValue(item,out int n))subscriptions[item]=n+1;
        else{subscriptions.Add(item,1);item.EquipmentChanged+=Changed;}
        if(item.Equipped){equipped.Add(item);EquipmentVersion++;}
    }
    private void Detach(ItemStack item)
    {
        if(subscriptions[item]>1)subscriptions[item]--;
        else{subscriptions.Remove(item);equipped.Remove(item);item.EquipmentChanged-=Changed;}
        if(item.Equipped)EquipmentVersion++;
    }
    protected override void InsertItem(int index,ItemStack item){ArgumentNullException.ThrowIfNull(item);base.InsertItem(index,item);Attach(item);}
    protected override void SetItem(int index,ItemStack item)
    {ArgumentNullException.ThrowIfNull(item);var old=this[index];if(ReferenceEquals(old,item))return;base.SetItem(index,item);Detach(old);Attach(item);}
    protected override void RemoveItem(int index){var old=this[index];base.RemoveItem(index);Detach(old);}
    protected override void ClearItems(){foreach(var item in list)Detach(item);base.ClearItems();}
    public int RemoveAll(Predicate<ItemStack> match)
    {return list.RemoveAll(item=>{if(!match(item))return false;Detach(item);return true;});}
    public new List<ItemStack>.Enumerator GetEnumerator()=>list.GetEnumerator();
}

"""Copy unmodified PureBDcraft item art. Then run bake-equipment.gd in Godot."""
from pathlib import Path
import zipfile, json
root = Path(__file__).resolve().parents[1]
base = 'assets/minecraft/textures/'
items = {
    'axe': ('铁斧', 'item/iron_axe.png', [20,44], .70, .055, True),
    'battleaxe': ('双刃战斧', 'item_alt/iron_axe/battleaxe/iron_axe.png', [20,44], .86, .065, True),
    'hammer': ('锻锤', 'item_alt/iron_sword/warhammer/iron_sword.png', [20,44], .56, .07, True),
    'shovel': ('铁铲', 'item/iron_shovel.png', [20,44], .84, .045, True),
    'hoe': ('铁锄', 'item/iron_hoe.png', [20,44], .82, .055, True),
    'bow': ('弓', 'item/bow.png', [32,32], .78, .045, False),
    'crossbow': ('弩', 'item/crossbow_standby.png', [32,40], .70, .07, False),
    'shears': ('剪刀', 'item/shears.png', [22,42], .34, .025, False),
    'fishing_rod': ('钓鱼竿', 'item/fishing_rod.png', [17,47], 1.05, .025, False),
}
manifest = {}
with zipfile.ZipFile(root.parent / 'PureBDcraft  64x MC117.zip') as archive:
    for item, (name, path, grip, size, depth, playable) in items.items():
        filename = 'warhammer.png' if item == 'hammer' else 'iron_battleaxe.png' if item == 'battleaxe' else Path(path).name
        target = root / 'Assets/Textures/Items' / filename
        target.write_bytes(archive.read(base + path))
        manifest[item] = dict(Name=name, Texture='res://Assets/Textures/Items/'+filename,
            Source=base+path, Grip=grip, PlaneSize=size, Thickness=depth, Playable=playable,
            Scene=f'res://Assets/Equipment/{item}.tscn')
    # Retain all original draw/loaded/cast frames for later ranged/fishing systems.
    for name in ['bow_pulling_0','bow_pulling_1','bow_pulling_2','crossbow_pulling_0',
                 'crossbow_pulling_1','crossbow_pulling_2','crossbow_arrow','crossbow_firework','fishing_rod_cast']:
        (root/'Assets/Textures/Items'/f'{name}.png').write_bytes(archive.read(base+'item/'+name+'.png'))
(root/'Assets/Equipment/catalog.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')

# 工具与武器资源

来源为用户提供的 `PureBDcraft  64x MC117.zip`。`catalog.json` 保留每张贴图在压缩包中的路径，PNG 原样复制。铁斧与双刃战斧使用不同文件，锻锤沿用包内铁剑的 warhammer 替代款。

这个版本未包含斧、锤、弓等独立的立体 JSON 几何。铲子模型继承 `item/generated`；盾牌继承 `builtin/entity`。这里按物品贴图的透明轮廓烘焙带侧面的网格，保留原图风格，不把这些生成结果称为包内原生 GLB。已接入的铁剑、矿镐、木盾继续使用现有角色模型。

## 已接入游戏

`axe` 铁斧、`battleaxe` 双刃战斧、`hammer` 锻锤、`shovel` 铁铲、`hoe` 铁锄：图标、背包/装备、地面物品显示、男女角色右手挂点、铁砧制造、库存补产、近战与存档。

- 铁斧：单手，伐木效率 +15%；不加速木工台生产。
- 战斧：双手武器，不提供伐木效率加成。
- 锻锤：单手，沿用锻造效率 +15%。
- 铁铲：双手，挖土、砂、腐殖土和砾石 +15%；不加速采岩矿。
- 铁锄：双手，播种/种树 +15%；不加速收获。

主手通过现有 `ActResident(equip/unequip)` 操作，玩家与 AI 共用；没有加入额外 LLM 调用。两手工具沿用伤残限制与盾牌互斥。无需强制装备工具才能工作。

## 素材预留

`bow`、`crossbow`、`shears`、`fishing_rod` 有图标与可加载的 3D 场景。弓/弩拉弦阶段、装填箭矢/烟花及钓竿抛线原图也已导入。尚无射击、弹药、剪毛或钓鱼玩法，因此没有把它们放进生产和装备列表，以免出现看似能用但无实际用途的按钮。

长矛/三叉戟本轮未接入；木、石、金、钻石等材质等级也未展开成额外物品。

## 构建与验证

从项目 `source` 目录运行：

```sh
python3 tools/import-equipment.py
source tools/env.sh
"$GODOT_BIN" --headless --path "$PROJECT_DIR" --editor --import --quit
"$GODOT_BIN" --headless --path "$PROJECT_DIR" --script res://tools/bake-equipment.gd
dotnet build NoDwarf.csproj --no-restore --nologo
"$GODOT_BIN" --path "$PROJECT_DIR" -- --tool-equipment-test
```

烘焙生成 `.res` 共享网格和 `.tscn` 场景，并将启用物品的真实包围盒写入 `Assets/Combat/equipment-geometry.json`。`Grip` 为原图像素握点，原点对齐手骨挂点，长柄沿局部 +Z。只在重新导入时生成网格；运行时实例共享资源，不逐帧创建材质/网格。每件约 740–1780 个三角形、一个材质面。

工具挥击复用 `Mine` 动画及 `grip-poses.json` 采样，命中窗口为 0.72–1.08 秒，1.5 秒完成；开始挥击时统一校正侧偏握持方向。战斗范围由实际接触段轨迹推导，不另外编造独立射程。

专项测试覆盖制造材料消耗、装备与卸下、双手限制、工作加成范围、真实近战命中、存读档、男女骨骼挂点、换装残留、模型共享和动画/碰撞接触点一致性。窗口模式额外输出 `.local/equipment-catalog.png` 和 `.local/equipment-held.png`。

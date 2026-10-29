# 五体型纸片矮人

当前游戏的 `sprite4` 渲染器使用本套素材。原图由内置 image_gen 生成；提示词见 `generation.json`、`generation-additions.json`。风格参考是用户认可的 `../flat-pawn-study/front-study-v1.png`。

## 资源约定

- 体型稳定 ID：`male`、`female`、`thin`、`hulk`、`fat`。性别是独立字段，衣服按体型选择。
- 三个绘制朝向：正面、右侧、背面。左侧镜像右侧；武器和双手位置由动作代码另外处理。
- 身体、工作衫、围裙、护甲、内衣覆盖层、裤装：每款每体型三个朝向。工作衫、围裙、护甲是覆盖整个躯干的表面，穿戴时替换白身表面，防止裸露边缘。
- 独立三款头、三款头发、两款胡子、头盔、帽子，跨体型共用。后脑不画胡子。戴帽/头盔隐藏头发，避免穿插。
- 女性白身保留上下内衣；Female 体型的内衣已画入白身，其余体型的女性使用独立上身内衣覆盖层。
- 日常只显示头与身体，无脚；工作、搬运、战斗显示小手及持物。鞋子仍是正常装备，参与游戏逻辑，但不产生脚部图形。
- 武器暂沿用已有持物图，未在此次重绘。

## 运行时贴图

`source/Assets/Characters/FlatPawn/`：40 张 600×280 三方向图集，加 6 张手部单图。每帧 200×280。打包只裁切透明留白、统一部件锚点和缩放；原始绘画保留在本目录。接缝检查须看最终拼装，不能只检查单独零件。

旧体型映射：regular/standard 按性别映射 male/female；slim → thin；broad → hulk；round → fat。已有档案可正常读取。

## 预览与复现

从仓库根目录启动交互预览：

```sh
bash source/tools/flat-pawn-demo.sh
```

切换三视图/动作、衣物、方向、动作和暂停。预览调用游戏实际使用的合成器和角色渲染器。

重新打包（Godot，原图不变）：

```sh
source source/tools/env.sh
"$GODOT_BIN" --headless --path "$PROJECT_DIR" --script ../art/dwarf/flat-pawn-five/pack.gd
```

之后重新导入 Godot 贴图。`--capture-flat-pawns` 参数会输出工作衫/围裙/护甲三视图对比和采矿截图，并检查五体型/四方向/穿衣/日常隐藏手/动作显示手。

游戏接入回归：`--sprite-integration-test`，检查保存、旧体型迁移、换装、朝向、暂停、采矿双手握点与实际行走。

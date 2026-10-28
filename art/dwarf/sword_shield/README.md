# 剑盾近战展示资源

按仓库《3D人物与剑盾近战.md》制作。装备、攻击动作、防御动作分别由三个 agent 制作，统一装配为男女展示场景。此交付不改变命中率结算，也不实现接触式伤害、格挡状态机或存档格式。

## 交付与约定

- 男女统一源文件：`male.blend`、`female.blend`。
- `attack/`：站立、行走、剑盾戒备、挥剑模块与源文件、采样数据。
- `defense/`：举盾、持盾、盾击受震、受击、倒地模块与验证材料。
- `weapons/`：独立剑盾源文件、几何和坐标验证。
- 引擎场景位于 `source/Assets/Characters/SwordShield/Models/`，包含 `male_base.tscn`、`male_clothed.tscn`、`female_base.tscn`、`female_clothed.tscn`（路径均相对仓库根）。
- 展示场景不附带移动或伤害脚本，不自动播放，只有一个 AnimationPlayer。根节点为地面中心；包装场景在制作时统一基础身体高度到 1.4 米、正面朝 -Z。配置 Scale=1、YawDegrees=0。头盔等附加装备可以高于基础身。
- `MainHandSocket`、`OffHandSocket` 分别位于右手、左手的 BoneAttachment3D 下，武器通过独立场景根变换适配。盾挂左手。
- clothed 是预先装配的日常衣着，未扩展游戏的三维换装、肤色、性别选择或头像系统。男女资源均可独立选择。

## 使用

示例配置为 `source/Assets/Characters/SwordShield/dwarf.example.json`。默认 `source/Assets/Characters/dwarf.json` 保留原有配置；要启用某套模型，可将 Scene 和 EquipmentScenes 等字段按示例填写，女版替换 Scene 文件名。配置更改后重新启动。

预览男女剑盾和九个必需动作及收盾、破防：

```bash
bash source/tools/run.sh res://Scenes/SwordShield/Preview.tscn
```

实际 ActorModelView 接口验证：

```bash
bash source/tools/run.sh --headless res://Scenes/SwordShield/IntegrationReview.tscn
```

动作采样与截图（会自动退出）：

```bash
bash source/tools/run.sh res://Scenes/SwordShield/Preview.tscn -- --sword-shield-test
```

统一重建模型：Blender 执行 `build_models.py`，传入 `-- --female` 生成女版。导入 GLB 后，用 Godot `--headless --script res://Scripts/SwordShield/BuildScenes.gd` 装配四套展示场景。保留 GLB 的 `.import` 设置以启用顶点色和完整动作轨道。此操作会覆盖生成资源，手工修改请先另存副本。

## 动作与边界

额外已制作 block_lower、block_break。

必需动作：idle、walk、sword_shield_idle、sword_slash、block_raise、block_hold、block_hit、hit、down。挥剑起手 0–0.36 秒、显示挥击 0.36–0.62 秒、收招到 1 秒。这些时间只是资产阶段定义，不会直接扣血。

游戏当前仅接入 idle/walk/combat_idle/work/attack/down；防御、受击动作在预览中可检查，但仍需后续模拟状态驱动。原地动画不负责角色导航位移。

验证覆盖实际展示模型加载、左右手穿脱与去重、模拟时间暂停、动作映射、根节点原地和骨骼有限变换。完整多人地图 FPS、墙体/盾牌/身体接触顺序、读档伤害去重属于后续验收，不能用本预览推断已完成。

## 本轮验证结果

构建 0 警告、0 错误。实际男女 ActorModelView 接入检查通过，最终两套 11 动作共 556 个姿态采样通过，所有非静止动作确实改变姿态。最终场景只有一套骨架与一个 AnimationPlayer，打包时已去除导入场景实例标记以避免骨架重复。演示见 `preview.mp4`，画面左女右男。

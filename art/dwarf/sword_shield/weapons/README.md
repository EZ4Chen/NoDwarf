# 单手剑与盾：展示资源

交付场景：

- `res://Assets/Characters/SwordShield/Weapons/sword.tscn`
- `res://Assets/Characters/SwordShield/Weapons/shield.tscn`

两者根变换均为 identity，不含动画、自动播放、角色移动、碰撞体或伤害脚本。共用男女同规格骨架。接入的 `MainHandSocket` 应位于 `slot_hand_r` BoneAttachment3D 下，`OffHandSocket` 应位于 `slot_hand_l` 下，Node3D 自身保持 identity。角色整体朝向的 Yaw 补偿在外层角色处理，不能再对武器单独施加世界坐标转向。

## 骨骼局部轴（不是 Godot 世界轴）

- +Y：当前手部静止姿势下的身体前方，盾面正法向。
- +Z：剑柄和剑尖轴，上端为剑尖。
- ±Y：剑的两侧切刃与护手跨度，匹配前后挥砍，避免用剑身宽面拍击。

剑全长约 0.745m，刀刃基点 `(0,0,.12)`、剑尖 `(0,0,.58)`。盾板约宽 0.399m、高 0.462m、厚 0.05775m，板中心在 `(0,.107625,-.0315)`。边框和盾徽会稍超出板尺寸，完整网格包围盒见 `weapon_manifest.json`。

`BladeBase`、`BladeTip`、`PanelCenter`、`FrontNormal` 是纯展示标记，供下一阶段轨迹采样引用；目前不据此判定伤害或格挡。盾板尺寸是未来简化判定形状的参考，不是已启用的碰撞盒。

源文件 `weapons.blend` 包含独立静态剑盾网格，原点都在握点，无骨架依赖。`build_weapons.py` 从当前 v2 源文件生成，剑保持修正后的刃向，盾撤销身体分段缩放并改为相对左手握点刚性缩放，以修复上缘偏斜。生成后两个物件会重叠在同一握点，可在 Blender 隐藏其中一个编辑。

保持 `.glb.import` 中的 `DwarfImport.gd` 引用，以正确使用顶点颜色。每件武器 2 个材质表面；剑 156 三角形，盾 288 三角形。

`validate_weapons.gd` 验证根变换、展示资源纯净性、实际网格方向、居中程度、标记位置与材质开关。使用项目 Godot 运行此脚本；结果保存在 `validation.json`。

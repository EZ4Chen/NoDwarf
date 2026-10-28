# 基础与剑盾攻击动作

入口 `attack_actions.build_actions(rig, fps=24)`，在已加载的 V2 男女 Blender 源文件上调用。模块自包含，依赖 Blender 的 bpy/mathutils。保留所有骨骼 rest、23 根骨骼和装备挂点，不改人物网格。

生成 idle（2 秒循环）、walk（1 秒循环）、sword_shield_idle（2 秒循环）、sword_slash（1 秒单次）。每个动作完整关键帧所有骨骼的旋转、位置、缩放，根节点始终没有位移。NLA 轨道与动作同名，默认 muted，统一导出时仅开启需要的轨道，使用 glTF NLA_TRACKS 和 export_anim_slide_to_zero=True。旧动作保留供 work/Mine 映射。

剑盾待机使用 `RigPose(rig).combat_idle(0)`，先调用 reset()。防御模块沿用此姿态作为举盾起点和放盾终点。左手盾正面朝 Blender -Y；右手剑刃工作面由已修正的 slot_hand_r 装备局部坐标定义，动画不再额外旋转装备。

sword_slash 分为起手 0–0.36 秒、有效显示挥击 0.36–0.62 秒、收招 0.62–1 秒。这些是视觉制作阶段标记，不是权威碰撞轨迹；没有方法回调或伤害代码。攻击时盾保持左侧低位，不宣称拥有防御覆盖。

`build_attack.py` 独立生成男女源文件、基础身体 GLB、逐帧挂点参考与四张关键姿势渲染。默认男版，`-- --female` 女版。独立GLB仅用于动作资源验证，最终合并文件由主agent的 build_models.py 制作。

验证：检查骨骼 rest 矩阵不变、root 不移动、三个循环动作首尾挂点吻合、男女均可生成和导出。人工检查剑盾待机、起手、挥落姿势。Root负责游戏内导入与状态映射验证。

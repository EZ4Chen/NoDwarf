# 防御、受击和倒地动作

`defense_actions.py` 在 Blender 中调用 `build_actions(rig, fps=24)`，只增加自己的七个动作和默认静音的同名 NLA 轨道，不修改骨架 rest、网格或其他团队的动作。全骨骼使用 XYZ Euler、位置和缩放关键帧；角色动画统一导出时启用目标 NLA 轨道，并将起始时间滑到零。

| 动作 | 秒 | 结束姿态 |
| --- | --- | --- |
| block_raise | 0.5 | 接 block_hold |
| block_hold | 1.0 | 首尾相同，可循环 |
| block_hit | 0.5 | 接回 block_hold |
| hit | 0.625 | 接回剑盾待机 |
| down | 1.25 | 最后 15% 保持平躺 |
| block_lower | 0.5 | 接回剑盾待机 |
| block_break | 0.75 | 放低盾，踉跄后回待机 |

raise 起点和 lower 终点与 attack_actions.RigPose.combat_idle(0) 使用相同姿势。盾面使用 `slot_hand_l` 局部 +Y 朝角色前方（Blender -Y）；手部保持相对 rest 的世界朝向，避免盾面随抬臂翻向天空。root 从不平移；down 通过骨盆局部位移与旋转倒地，并以基础身、靴子、护甲及头盔的最低顶点调整落地高度。

`validate_defense.py` 检查 rest 不变、175 个采样的 root、举盾时盾面朝向、五处接缝、倒地终态保持及与攻击团队待机的逐骨骼一致性。男版输出在本目录，女版报告在 `female/`。PNG 是源文件姿态检查；正式游戏包装场景由 root 统一导出与验证，使用装备团队的新剑盾。

这些文件仅提供显示动作；没有碰撞、伤害回调或已实现的防御判定。何时格挡生效、何时破防由后续权威战斗状态机决定。

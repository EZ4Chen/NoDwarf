# 矮人第二版：短腿、宽肩、方形头部

`male/dwarf.blend` 与 `female/dwarf.blend` 是两套独立可编辑源文件。男基础身为裸上身和内裤；女基础身为上身内衣、内裤和短发。胡须为独立外观件。第一版保留在 `../v1/`，不覆盖主游戏居民。

小腿和大腿所在的高度区间缩短 32%，脚部保留厚度；肩胸横向加宽 18%、厚度增加 10%，头和肩改为带倒角的块面。两种身体使用相同的 23 骨骼规范、5 个挂点和五种原地动画。装备随新比例变形，头盔另加宽以容纳方头与短发。身体遮挡网格针对各自基础身重新生成。

资源：`source/Assets/Characters/DwarfV2/male/` 与 `female/`（仓库相对路径）。

在仓库根目录运行：

```bash
# 男版交互预览
bash source/tools/run.sh res://Scenes/CharacterLabV2/DwarfLab.tscn
# 女版交互预览
bash source/tools/run.sh res://Scenes/CharacterLabV2/DwarfLab.tscn -- --female
```

专项检查在上述命令的用户参数中加入 `--dwarf-lab-test`；加入 `--dwarf-lab-film` 同时输出动作帧。

重建使用 `../build_v2.py`（Blender Python）；默认男版，传入 `-- --female` 为女版。`--fast` 跳过静态渲染，仍生成包含相机和动作的源文件。重建会覆盖对应版本的生成资源；手工修改请另存。

`body-comparison.png` 从左到右为第一版、新男版、新女版；相同相机尺度用于展示比例变化。`equipment-comparison.png` 上男下女，分别展示基础身、日常、矿工、守卫。`animation-comparison.mp4` 左男右女，展示站立、行走、采矿、挥剑和搬运。

两套各检查 120 个动画采样、9 件装备/外观穿脱和 600 次挂点位置，另对照 Blender 制作帧的骨骼及工具方向。有限采样和截图检查不是任意新增装备的无穿模保证。尚未替换主游戏居民或测试完整地图百人性能。

## 握持方向修正

镐头和剑刃绕握柄轴旋转 90 度，使工作面与前后挥动方向一致。手持装备改为整体移动到新挂点，不再应用身体的分段缩放，避免柄轴被拉歪。男女源文件、GLB 和引擎预览均已更新；实际网格的前后横向跨度检查及两套动作回归通过。

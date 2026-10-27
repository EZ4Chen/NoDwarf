# NoDwarf

开发中的 Godot / C# 方块世界殖民地模拟游戏。当前仓库用于私人备份和协作，尚未公开发布，也尚未选择项目开源许可证。

## 项目入口

- [`source/`](source/)：Godot 工程、C# 源码、场景和运行所需资源。
- [`source/README.md`](source/README.md)：功能说明、操作方式、验证入口及开发记录。
- [`benchmarks/`](benchmarks/)：寻路、模拟与性能检查。
- [开发版本计划](开发版本计划.md)：版本目标与待完成工作。
- [开发任务清单](开发任务清单与多Agent分工.md)、[方块世界与多层寻路](方块世界与多层寻路.md)、[流体系统设计与实施](流体系统设计与实施.md)：设计文档。

## 开发环境

项目当前配置为 **Godot 4.7.2 .NET** 和 **.NET SDK 8.0.426**。请使用支持 C# 的 Godot .NET 版本；具体配置见 `source/NoDwarf.csproj` 和 `source/global.json`。

工具本体、缓存、存档和构建产物不进入 Git。克隆仓库后需要自行准备开发环境。

### macOS

已有脚本默认从仓库根目录的以下路径读取工具：

- `.tools/dotnet/`：.NET SDK。
- `.tools/godot/Godot_mono.app`：Godot .NET 编辑器。

也可以通过 `NODWARF_DOTNET_ROOT` 和 `NODWARF_GODOT_BIN` 指向现有安装。脚本会从 Godot 安装目录复制构建需要的 NuGet 包到忽略的 `source/.local/` 中。

从仓库根目录运行：

```sh
bash source/tools/editor.sh  # 打开编辑器
bash source/tools/run.sh     # 启动游戏
bash source/tools/build.sh   # 编译
bash source/tools/check.sh   # 现有回归检查
```

### Windows

安装匹配的 Godot .NET 与 .NET SDK，保留 Godot 目录中的 `GodotSharp` 文件夹，然后在仓库根目录运行：

```powershell
./source/tools/windows.ps1 -Godot "C:\Tools\Godot\Godot_v4.7.2-stable_mono_win64.exe" -Mode editor
```

## 资源与许可

代码和第三方素材的授权分别处理。当前原型使用部分 PureBDcraft 资源，原始条款保存在 [`terms of use.txt`](source/Assets/Textures/PureBDcraft/terms%20of%20use.txt)。

Textures/3D Models/Sounds from PureBDcraft ResourcePack by https://bdcraft.net

私人仓库不代表这些素材已获得开源授权。未来公开仓库或发布游戏前，应确认各项素材的适用许可及署名要求。

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

## 自动打包与发布

[Build and release](https://github.com/EZ4Chen/NoDwarf/actions/workflows/release.yml) 使用 GitHub Actions 分别在 Windows 和 macOS 上编译、导出、运行检查并生成 ZIP：

- **Windows x64**：`NoDwarf-<版本>-windows-x64.zip`。
- **macOS Apple Silicon（M 系列）**：`NoDwarf-<版本>-macos-arm64.zip`。
- 安装包自带 Godot 与 .NET 运行时，玩家不需要安装开发环境。

推送 `main`、向 `main` 提交 PR，或在 Actions 页面选择分支点击 **Run workflow**，都会生成测试包；在该次运行的 **Artifacts** 中下载，保留 7 天。这些操作不会创建 Release。手动选择版本标签运行则会执行该版本的发布流程。

准备发布时，先提交并推送要发布的代码，再对该提交创建版本标签。例如首次测试发布：

```sh
git tag v0.1.0-alpha.1
git push origin v0.1.0-alpha.1
```

标签只包含已经提交的代码。工作目录中的未提交修改不会进入安装包。使用 `v主版本.次版本.修订号` 格式；带 `-alpha.1`、`-beta.1` 等后缀会自动标为预发布。

两个平台都导出成功且安装包运行检查通过后，工作流才会创建 Release、上传两个 ZIP 和 `SHA256SUMS.txt`，最后发布。若上传中断，Release 保持草稿，重新运行失败任务可重试；已发布的同名版本不会被覆盖，应创建新版本标签。

工作流使用仓库自带的 `GITHUB_TOKEN`，无需个人访问令牌。仓库为私有时，Release 和测试包也需要仓库访问权限。组织或仓库策略若禁止 Actions 写入，需要允许该工作流的 `contents: write` 发布任务。私人仓库的构建和存储计入账户的 Actions 用量。

Mac 包仅作 ad-hoc 签名，未做 Apple 公证；Windows 包未做发行证书签名，系统可能显示来源验证提示。当前没有 Intel Mac 构建。自动检查不替代真实窗口、音频和长时间游玩验收。

导出设置见 [`source/export_presets.cfg`](source/export_presets.cfg)。升级 Godot 时，应同时更新工程 SDK、[`tools/ci/godot-downloads.json`](tools/ci/godot-downloads.json) 中的版本和官方文件 SHA-256 校验值；.NET 版本由 `source/global.json` 决定。

## 资源与许可

代码和第三方素材的授权分别处理。当前原型使用部分 PureBDcraft 资源，原始条款保存在 [`terms of use.txt`](source/Assets/Textures/PureBDcraft/terms%20of%20use.txt)。

Textures/3D Models/Sounds from PureBDcraft ResourcePack by https://bdcraft.net

中文字体随游戏附带 Noto Sans CJK SC Regular（SIL Open Font License 1.1），来源与许可证见 [`source/Assets/Fonts/NotoSansCJK/`](source/Assets/Fonts/NotoSansCJK/)。

私人仓库不代表这些素材已获得开源授权。未来公开仓库或发布游戏前，应确认各项素材的适用许可及署名要求。

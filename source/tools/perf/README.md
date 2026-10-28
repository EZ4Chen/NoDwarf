# 性能诊断工作流

目标是用相同工作量验证长帧、模拟耗时和后台延迟是否降低，同时保留足够数据解释原因。CPU 占用高不等于效率高。普通游戏默认不记录时间线或每秒诊断样本。

所有命令从仓库根目录运行；`source/tools/profile.sh` 自动使用仓库的 .NET 和 Godot。Python 3 使用标准库，无需 pip。工具和结果留在本机，不自动上传。

## 1. 安装与自检

```bash
bash source/tools/profile.sh setup
bash source/tools/profile.sh doctor
bash source/tools/profile.sh check
```

`setup` 将微软 `dotnet-trace`、`dotnet-counters` 的 **8.0.547301** 版安装到 `.tools/diagnostics/`，不修改全局工具；版本声明在 `tools.json`。`doctor` 检查 SDK、采集器和原生工具。`check` 验证并发任务 ID、事件容量限制、关闭后晚到事件，以及错误对比条件的拒绝行为。

macOS 的 `sample` 可以采集原生线程堆栈。`xctrace` 需要完整 Xcode；仅有 Command Line Tools 不够。若未显式设置 `DEVELOPER_DIR`，工作流会为本次进程选用已安装的 `/Applications/Xcode.app/Contents/Developer`，不改变系统 `xcode-select`。缺少 Xcode 不影响 .NET 采集。2026-10-28 已用 Xcode 27.1 实际验证 Time Profiler 录制与样本导出；部分 Godot/JIT 地址缺少符号，具体 C# 函数归因仍需结合托管采样和分段计时。

## 2. 固定场景并记录基线

```bash
bash source/tools/profile.sh record --mode baseline --scenario game --label before-1
```

默认是真实窗口、100 人、1×、Debug 构建、5 秒预热及 20 秒测量；渲染状态使用现有帧诊断入口，初始模拟先推进相同的 150 tick。`--quick` 缩短为 10 秒测量，用于工具冒烟检查，不作为最终性能结论的首选。游戏场景仍随真实时间执行任务，记录起始状态哈希、任务完成量和真实 TPS，而不是保证每次恰好运行相同 tick 数。

| 场景 | 内容 | 推荐用途 |
| --- | --- | --- |
| `game` | 100 人、三层建筑、挖掘、搬运、需求、固定镜头 | 综合主线程和 TPS |
| `interior` | 相同模拟，地下室内视角持续旋转 | 遮挡、地形构网、提交和取消 |
| `environment` | 将工作场景放入有树木等环境的地图 | 更接近实际画面的渲染压力 |
| `simulation` | Release 构建，150 tick 预热后固定执行 1800 tick | 排除引擎和绘制，比较模拟算法 |

`--speed 3` 用于游戏场景 3×。`--headless` 可跑自动化，但无窗口 FPS 不能代表画面帧率。`--gpu-timing` 显式开启 Godot 的 renderer 计时；后端可能不提供有效数据，0 不能解释成 GPU 没工作。`simulation` 不绘图，不受镜头或倍率参数影响。

每次运行新建唯一目录 `source/.local/performance/<UTC>-<标签>-<随机后缀>/`，不会覆盖旧结果。控制台输出 `CAPTURE_DIR` 和 `PROFILE_OK`。默认重新构建；`--no-build` 复用已有二进制用于同构建重复测试，修改代码后不要使用它。源码哈希是运行时工作区状态，二进制 SHA256 才标识实际执行文件；并行编辑或 `--no-build` 时二者不保证对应。

## 3. 单独录制诊断

```bash
bash source/tools/profile.sh record --mode trace --scenario interior --label diagnose
bash source/tools/profile.sh record --mode trace --scenario game --label native --native sample
```

`trace` 同时启用：

- EventPipe 托管栈采样，以及 GC、线程、锁竞争事件和 `NoDwarf-Performance` 任务标记。
- `dotnet-counters` 的 `System.Runtime` 与 `NoDwarf-Runtime` 提供者。
- 主线程帧、模拟 tick、快照、后台计算和发布的时间线。
- 原有 `PerformanceProbe` 分段计时和逐帧诊断。

Godot 嵌入的运行时在本机未输出 `System.Runtime`，所以显式增加 `NoDwarf-Runtime`：通过公开 .NET API提供累计分配、堆大小、GC 次数/暂停累计时间、线程池线程/排队数、Monitor 锁竞争累计次数。报告会说明实际提供者，不把缺失数据解释成零。`runtime.json` 另以每秒公共 API 采样记录 CPU 时间、工作集和同类计数，统一到任务时间线的时钟。

采集采用启动与退出握手：先确认工具附加，再开始场景；测量结束后先停 EventPipe，再让游戏退出，避免 Godot 关闭内嵌 CLR 时截断 trace。固定版本的 `dotnet-counters` 在本机收到 SIGINT 时可能不封闭 JSON，因此使用正常时长结束：simulation 10 秒、quick 游戏 25 秒、普通游戏 35 秒。等待落盘发生在测量结束之后。启动特别慢时，外部 counters 可能不覆盖整个测量窗口，以原始时间戳为准；进程内样本覆盖到退出。任务采样包含预热与收尾，报告不会把全部采样直接当作测量段 CPU 用量。

有完整 Xcode 时可使用 `--native instruments`，录制 10 秒 Time Profiler `.trace`。原生采样从场景启动阶段开始，包含预热，并非只覆盖测量段。内核调度、精确线程阻塞原因需进一步在 Instruments 中用 Thread State Trace / System Trace；当前脚本没有声称能从 EventPipe 推断这些信息。GPU 精确完成时刻和硬件计数器也不在托管采样范围内。

## 4. 读报告、追溯原因

| 文件 | 用途 |
| --- | --- |
| `report.md` / `summary.json` | 帧和 tick 指标、任务阶段延迟、运行时样本、热点与完整性提示 |
| `managed.nettrace` | 原始托管采样、GC、锁竞争和任务事件；保留它以便深入分析 |
| `managed.speedscope.json` | 导入 Speedscope 看调用树、火焰图和时间顺序 |
| `hotspots-self.txt` / `hotspots-inclusive.txt` | 全捕获期间的自身/含子调用采样热点 |
| `timeline.json` | 导入 Perfetto 或 Chrome tracing 看各线程完整跨度；按任务 ID 检索 |
| `runtime.json` / `counters.json` | 每秒公共 API 样本 / 外部采集器原始数据 |
| `frame-tps-*.json` / `frame-diagnostic-*.json` | 真实 TPS、逐帧耗时、GC 次数、渲染状态及管线统计 |
| `native-sample.txt` / `native.trace` | 可选 macOS 原生线程样本 / Instruments 录制 |
| `manifest.json` / `sources.json` / `working.diff` | 参数、平台、工具版本、二进制/源码哈希、Git 状态及已跟踪文件改动 |
| `capture.log` / `trace.log` / `counters.log` | 故障排查，不应只看退出码 |

Speedscope / Perfetto 可以手动打开本地文件；脚本不会自动向在线服务发送记录。内存中的时间线最多 20 万条事件，运行时最多 3600 个样本。溢出会计数并在报告提示，不能把截断数据当完整捕获。后台线程只写有界记录，保存时关闭入口，晚到事件不会改写已完成文件。

任务 ID 跨地形、遮挡唯一，关联 `queued → started → computed → collected → applied`；也会记录取消、被替换、过期丢弃。地形 revision 字段是网格请求版本，遮挡是缓存 generation，不能把两个数值当相同的地形版本。

- queueMs 高：待办或线程池排队，先看并发预算和是否重复请求。
- computeMs 高：计算本身慢，再查对应调用栈和数据量。
- collectWaitMs 高：结果已算好，但主线程尚未收取。
- apply 跨度高：网格上传、碰撞注册或场景提交贵。
- 分配/GC 暂停累计量同步升高：再检查分配热点；时间重合只提供线索，不能直接证明因果。

未到达某阶段的任务不计入该延迟，不按 0 ms 处理。时间线 tid 为托管 ID，原生和 EventPipe 使用的 OS tid 可能不同，不按数字直接拼接。时间线跨度是经过时间，包含被抢占/等待；栈采样也可能包含等待线程，不能把等待函数排名高当成 CPU 消耗高。

## 5. 修改、验证和比较

只针对有证据的瓶颈修改，先跑相关正确性检查，再运行同一场景的 baseline。推荐每版至少 3 次，条件允许时前后交替，保持窗口尺寸、倍率、电源状态、前台可见性一致，避免同时编译、跑其他性能测试或大量后台任务。

```bash
bash source/tools/check.sh
bash source/tools/profile.sh record --mode baseline --scenario game --label after-1
bash source/tools/profile.sh compare \
  --before /绝对路径/before-1 /绝对路径/before-2 /绝对路径/before-3 \
  --after /绝对路径/after-1 /绝对路径/after-2 /绝对路径/after-3 \
  --output /绝对路径/新的对比目录
```

`compare` 拒绝失败记录、trace 模式、不同场景/倍率/窗口/平台、不同状态哈希，以及同一组内混用不同构建。simulation 比较最终状态哈希；游戏场景比较固定的初始状态，实际完成工作量另在原始结果里检查。输出跨运行的中位数和变化百分比，不自动把负数解释为改善。单次数据可用于流程验证，不足以证明性能回归或收益。

报告可以重建：`bash source/tools/profile.sh analyze /绝对路径/采集目录`。失败、超时或缺失关键数据会留下日志和失败 manifest，不会给出成功报告；进程清理仅针对本次脚本启动的进程组。默认超时 180 秒，可用 `--timeout` 调整。没有采集工具或未安装 Xcode 时会说明缺项，不会静默跳过用户要求的采集器。

## 验收一项优化的最低证据

1. 相同场景与正确性条件成立：状态/路径/任务预留/视觉拾取符合预期。
2. baseline 的平均值、P99、最大值及工作完成量，而不只是一张火焰图。
3. trace 能解释变化：减少了什么计算、分配或等待，是否增加取消/重复计算。
4. 明确未覆盖的范围：无窗口、短场景、GPU 未测、数据丢失或版本差异。

工具参考：[dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)、[dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)、[EventPipe 能力边界](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventpipe)。

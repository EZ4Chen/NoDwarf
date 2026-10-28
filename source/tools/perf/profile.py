#!/usr/bin/env python3
"""Reproducible, local-only performance capture. No shell-evaluated subprocesses."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import statistics
import subprocess
import sys
import time
from datetime import datetime, timezone
from uuid import uuid4

PROJECT = Path(__file__).resolve().parents[2]
ROOT = PROJECT.parent
TOOLS = ROOT / '.tools/diagnostics'
VERSIONS = json.loads((Path(__file__).parent / 'tools.json').read_text())
RUNS = PROJECT / '.local/performance'


def read(path):
    return json.loads(Path(path).read_text())


def write(path, data):
    Path(path).write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n')


def output(cmd, **kwargs):
    return subprocess.check_output(cmd, text=True, stderr=subprocess.STDOUT, timeout=30, **kwargs).strip()


def tool(name):
    return str(TOOLS / name)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def stats(values):
    values = sorted(values)
    if not values:
        return {'count': 0, 'median': None, 'p95': None, 'p99': None, 'max': None}
    return {'count': len(values), 'median': statistics.median(values),
            'p95': values[math.ceil(len(values) * .95) - 1],
            'p99': values[math.ceil(len(values) * .99) - 1], 'max': values[-1]}


def native_environment():
    env = os.environ.copy()
    installed = Path('/Applications/Xcode.app/Contents/Developer')
    if sys.platform == 'darwin' and 'DEVELOPER_DIR' not in env and installed.exists():
        # Process-local selection: installing Xcode need not change xcode-select.
        env['DEVELOPER_DIR'] = str(installed)
    return env


def doctor():
    result = {'dotnet': output(['dotnet', '--version'], cwd=PROJECT), 'tools': {},
              'sample': shutil.which('sample'), 'platform': platform.platform()}
    for name, version in VERSIONS.items():
        try:
            installed = output([tool(name), '--version'])
            result['tools'][name] = {'expected': version, 'installed': installed,
                                    'ok': installed.startswith(version)}
        except (OSError, subprocess.SubprocessError) as e:
            result['tools'][name] = {'expected': version, 'ok': False, 'error': str(e)}
    try:
        result['instruments'] = output(['xcrun', 'xctrace', 'version'], env=native_environment())
        result['developerDirectory'] = native_environment().get('DEVELOPER_DIR', 'system selection')
    except (OSError, subprocess.SubprocessError):
        result['instruments'] = 'Unavailable: full Xcode and a selected developer directory are required. .NET capture remains usable.'
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return result


def setup():
    TOOLS.mkdir(parents=True, exist_ok=True)
    for name, version in VERSIONS.items():
        if (TOOLS / name).exists():
            installed = output([tool(name), '--version'])
            if not installed.startswith(version):
                raise RuntimeError(f'{name}: expected {version}, found {installed}; resolve the project-local tool version explicitly')
            continue
        subprocess.run(['dotnet', 'tool', 'install', name, '--tool-path', str(TOOLS),
                        '--version', version, '--configfile', str(PROJECT / 'NuGet.Config')],
                       cwd=PROJECT, check=True, timeout=300)
    doctor()


def terminate(proc):
    if proc is None or proc.poll() is not None:
        return
    # Only processes launched by this capture, each in its own process group.
    try:
        os.killpg(proc.pid, signal.SIGTERM)
    except ProcessLookupError:
        return
    try:
        proc.wait(timeout=5)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.wait(timeout=5)


def checked_run(cmd, logfile, **kwargs):
    with Path(logfile).open('w') as log:
        subprocess.run(cmd, stdout=log, stderr=subprocess.STDOUT, check=True, **kwargs)


def record(args):
    if not re.fullmatch(r'[A-Za-z0-9_-]{1,60}', args.label):
        raise ValueError('label must contain 1–60 letters, digits, underscores or hyphens')
    if args.mode == 'baseline' and args.native != 'none':
        raise ValueError('native sampling belongs to trace mode, not baseline')
    if args.mode == 'trace':
        for name, version in VERSIONS.items():
            if not (TOOLS / name).exists() or not output([tool(name), '--version']).startswith(version):
                raise RuntimeError('Run tools/profile.sh setup first')
    if args.native == 'instruments':
        output(['xcrun', 'xctrace', 'version'], env=native_environment())  # Fail before launching any workload.
    if args.native == 'sample' and not shutil.which('sample'):
        raise RuntimeError('macOS sample is unavailable')
    folder = RUNS / (datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + args.label + '-' + uuid4().hex[:6])
    folder.mkdir(parents=True)
    print(f'CAPTURE_DIR: {folder}', flush=True)
    simulation = args.scenario == 'simulation'
    config = 'Release' if simulation else 'Debug'
    dll = ROOT / 'benchmarks/bin/Release/net8.0/NavigationBench.dll' if simulation else PROJECT / '.godot/mono/temp/bin/Debug/NoDwarf.dll'
    build = ['dotnet', 'build', str(ROOT / 'benchmarks/NavigationBench.csproj' if simulation else PROJECT / 'NoDwarf.csproj'), '-c', config, '--nologo']
    if not args.no_build:
        checked_run(build, folder / 'build.log', cwd=PROJECT, timeout=180)
    if simulation:
        command = ['dotnet', str(dll), '--realtime-simulation']
        if args.mode == 'trace':
            command.append('--profile')
    else:
        command = [os.environ['NODWARF_GODOT_BIN'], '--path', str(PROJECT)]
        if args.headless:
            command.append('--headless')
        command += ['--', '--tps-test', '--frame-diagnostic', '--diag-fixed-workload', '--diag-quiet']
        if not args.gpu_timing:
            command.append('--diag-no-gpu-timing')
        if args.quick:
            command.append('--tps-quick')
        if args.speed == 3:
            command.append('--tps-3x')
        if args.scenario == 'interior':
            command += ['--diag-interior', '--diag-basement', '--diag-orbit']
        if args.scenario == 'environment':
            command += ['--tps-environment']
        if args.mode == 'trace':
            command.append('--tps-profile')
    # Snapshot relevant sources including untracked C# files. Preserve an exact
    # working diff as context; git revision alone cannot describe a dirty build.
    sources = {}
    for base in (PROJECT / 'Simulation', PROJECT / 'Scripts', ROOT / 'benchmarks'):
        for path in sorted(base.glob('*.cs')):
            sources[str(path.relative_to(ROOT))] = sha(path)
    source_hash = hashlib.sha256(json.dumps(sources, sort_keys=True).encode()).hexdigest()
    diff = subprocess.check_output(['git', 'diff', '--', 'source', 'benchmarks'], cwd=ROOT)
    (folder / 'working.diff').write_bytes(diff)
    write(folder / 'sources.json', sources)
    manifest = {'schema': 1, 'label': args.label, 'mode': args.mode, 'scenario': args.scenario,
                'configuration': config, 'headless': args.headless if not simulation else True,
                'quick': args.quick if not simulation else False, 'speed': args.speed if not simulation else 1,
                'native': args.native, 'gpuTiming': args.gpu_timing, 'command': command, 'buildCommand': build,
                'sourceHash': source_hash, 'binarySha256': sha(dll),
                'gitRevision': output(['git', 'rev-parse', 'HEAD'], cwd=ROOT),
                'gitStatus': output(['git', 'status', '--short'], cwd=ROOT),
                'platform': platform.platform(), 'machine': platform.machine(),
                'dotnetVersion': output(['dotnet', '--version'], cwd=PROJECT),
                'hardware': output(['sysctl', '-n', 'machdep.cpu.brand_string']) if sys.platform == 'darwin' else platform.processor(),
                'workflowSha256': sha(__file__), 'loadAverageAtStart': os.getloadavg(),
                'logicalProcessors': os.cpu_count(), 'tools': VERSIONS if args.mode == 'trace' else {},
                'startedUtc': datetime.now(timezone.utc).isoformat(), 'status': 'running',
                'note': 'Trace mode perturbs timing. Timeline spans are wall time. Headless FPS is not rendered FPS. Managed and OS thread IDs differ.'}
    write(folder / 'manifest.json', manifest)
    env = native_environment() if args.native == 'instruments' else os.environ.copy()
    env['NODWARF_PERF_OUTPUT_DIR'] = str(folder)
    env['NODWARF_PROFILE_TRACE'] = '1' if args.mode == 'trace' else '0'
    # Attach independently; stop collectors before Godot tears down its embedded CLR.
    env['NODWARF_PROFILE_HANDSHAKE'] = '1' if args.mode == 'trace' else '0'
    # All collectors inherit the target's TMPDIR for diagnostic socket discovery.
    procs, handles = [], []
    try:
        game_log = (folder / 'capture.log').open('w'); handles.append(game_log)
        main = subprocess.Popen(command, cwd=PROJECT, env=env, stdout=game_log, stderr=subprocess.STDOUT, start_new_session=True)
        procs.append(main)
        deadline = time.monotonic() + args.timeout
        ready = folder / 'ready.json'
        while not ready.exists() and main.poll() is None and time.monotonic() < deadline:
            time.sleep(.1)
        if not ready.exists():
            raise RuntimeError('Target did not initialize; see capture.log')
        # A write can be visible before the JSON flush; wait briefly for a complete document.
        for attempt in range(30):
            try:
                pid = read(ready)['pid']; break
            except json.JSONDecodeError:
                time.sleep(.1)
        else:
            raise RuntimeError('Incomplete ready.json')
        manifest['targetPid'] = pid
        if args.mode == 'trace' and main.poll() is None:
            trace_log = (folder / 'trace.log').open('w'); handles.append(trace_log)
            trace_proc = subprocess.Popen([tool('dotnet-trace'), 'collect', '-p', str(pid), '--profile', 'cpu-sampling',
                '--providers', 'Microsoft-Windows-DotNETRuntime:0x14001:4,NoDwarf-Performance:0xFFFFFFFFFFFFFFFF:4',
                '--buffersize', '128', '--output', str(folder / 'managed.nettrace')],
                env=env, stdin=subprocess.PIPE, stdout=trace_log, stderr=subprocess.STDOUT, start_new_session=True)
            procs.append(trace_proc)
            log = (folder / 'counters.log').open('w'); handles.append(log)
            counter = subprocess.Popen([tool('dotnet-counters'), 'collect', '-p', str(pid), '--counters', 'System.Runtime,NoDwarf-Runtime',
                                        '--format', 'json', '--duration', '00:00:' + str(10 if simulation else 25 if args.quick else 35), '--output', str(folder / 'counters.json')],
                                       env=env, stdin=subprocess.PIPE, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            procs.append(counter)
            attach_deadline = time.monotonic() + 20
            while 'Output File' not in (folder / 'trace.log').read_text() or 'Starting a counter session' not in (folder / 'counters.log').read_text():
                if trace_proc.poll() is not None or counter.poll() is not None or time.monotonic() > attach_deadline:
                    raise RuntimeError('Collectors failed to attach; see trace.log/counters.log')
                time.sleep(.1)
            (folder / 'collectors-ready').touch()
        if args.native != 'none' and main.poll() is None:
            native_cmd = ['sample', str(pid), '10', '1', '-file', str(folder / 'native-sample.txt')] if args.native == 'sample' else [
                'xcrun', 'xctrace', 'record', '--template', 'Time Profiler', '--attach', str(pid),
                '--time-limit', '10s', '--output', str(folder / 'native.trace')]
            log = (folder / 'native.log').open('w'); handles.append(log)
            procs.append(subprocess.Popen(native_cmd, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True))
        collectors_stopped = False
        while main.poll() is None:
            if args.mode == 'trace' and (folder / 'finished.json').exists() and not collectors_stopped:
                for proc in (trace_proc,):
                    if proc.poll() is None:
                        proc.send_signal(signal.SIGINT)
                for proc in (trace_proc, counter):
                    proc.wait(timeout=20)
                    if proc.returncode:
                        raise RuntimeError(f'Collector exited {proc.returncode}; see trace.log/counters.log')
                (folder / 'collectors-stopped').touch()
                collectors_stopped = True
            if time.monotonic() > deadline:
                raise TimeoutError(f'Capture exceeded {args.timeout}s; see capture.log')
            time.sleep(.2)
        if main.returncode:
            raise RuntimeError(f'Target/trace exited {main.returncode}; see capture.log')
        for proc in procs[1:]:
            try:
                proc.wait(timeout=15)
            except subprocess.TimeoutExpired:
                terminate(proc)
        manifest['collectorExitCodes'] = [p.returncode for p in procs[1:]]
        if any(manifest['collectorExitCodes']):
            raise RuntimeError('A requested collector failed; see trace.log, counters.log or native.log')
        if not simulation and 'TPS_REVIEW_OK:' not in (folder / 'capture.log').read_text():
            raise RuntimeError('Workload exited without TPS_REVIEW_OK')
        if args.mode == 'trace':
            trace = folder / 'managed.nettrace'
            if not trace.exists() or trace.stat().st_size == 0:
                raise RuntimeError('No EventPipe trace collected')
            checked_run([tool('dotnet-trace'), 'convert', str(trace), '--format', 'Speedscope', '-o', str(folder / 'managed')],
                        folder / 'convert.log', env=env, timeout=90)
            for kind in ('self', 'inclusive'):
                cmd = [tool('dotnet-trace'), 'report', str(trace), 'topN', '--number', '30']
                if kind == 'inclusive':
                    cmd.append('--inclusive')
                checked_run(cmd, folder / f'hotspots-{kind}.txt', env=env, timeout=90)
            for name in ('convert.log', 'hotspots-self.txt', 'hotspots-inclusive.txt'):
                if '[ERROR]' in (folder / name).read_text():
                    raise RuntimeError(f'Trace conversion/report error: {name}')
            counters_data = read(folder / 'counters.json')
            if not counters_data.get('Events') or not all(e.get('name') for e in counters_data['Events']):
                raise RuntimeError('Runtime counters contain no named samples; see counters.log')
            if not (folder / 'timeline.json').exists():
                raise RuntimeError('No task timeline was flushed')
        manifest['status'] = 'complete'
    except BaseException as e:
        manifest['status'] = 'failed'; manifest['error'] = str(e)
        raise
    finally:
        for proc in reversed(procs):
            terminate(proc)
        for handle in handles:
            handle.close()
        manifest['finishedUtc'] = datetime.now(timezone.utc).isoformat()
        write(folder / 'manifest.json', manifest)
    try:
        analyze(folder)
    except Exception as error:
        manifest['status'] = 'failed'; manifest['error'] = f'Analysis failed: {error}'
        write(folder / 'manifest.json', manifest)
        raise
    print(f'PROFILE_OK: {folder / "report.md"}', flush=True)
    return folder


def timeline_summary(data):
    events = data['traceEvents']
    window = {e['args']['phase']: e['ts'] for e in events if e['name'] == 'measurement' and e['ph'] == 'i'}
    measured = [e for e in events if window.get('start', float('inf')) <= e.get('ts', -1) <= window.get('end', -1)]
    phases, tasks, spans = {}, {}, {}
    for event in events:
        if event['ph'] == 'X':
            spans.setdefault(event['name'], []).append(event['dur'] / 1000)
        args = event.get('args', {})
        if event['ph'] == 'i' and args.get('task', 0):
            key = (event['name'], args['task'])
            tasks.setdefault(key, {})[args['phase']] = event['ts']
            phases[args['phase']] = phases.get(args['phase'], 0) + 1
    timings = {}
    for (category, _), task in tasks.items():
        for label, begin, end in [('queueMs', 'queued', 'started'), ('computeMs', 'started', 'computed'),
                                  ('collectWaitMs', 'computed', 'collected'), ('readyToApplyMs', 'computed', 'applied')]:
            if begin in task and end in task:
                timings.setdefault(category + '.' + label, []).append((task[end] - task[begin]) / 1000)
    long_frames = sorted((e for e in events if e['ph'] == 'X' and e['name'] == 'frame.main'), key=lambda e: e['dur'], reverse=True)[:10]
    return {'measurementWindowMs': {k: v / 1000 for k, v in window.items()},
            'measuredMainMs': stats([e['dur'] / 1000 for e in measured if e['name'] == 'frame.main' and e['ph'] == 'X']),
            'droppedEvents': data['metadata']['droppedEvents'], 'events': len(events), 'tasks': len(tasks),
            'managedThreads': sorted({e['tid'] for e in events}), 'phases': phases,
            'spansMs': {k: stats(v) for k, v in spans.items()}, 'taskTimingMs': {k: stats(v) for k, v in timings.items()},
            'longestMainFrames': [{'atMs': e['ts'] / 1000, 'durationMs': e['dur'] / 1000,
                                  'frame': e['args']['frame']} for e in long_frames],
            'note': 'Whole capture including warmup. Tasks not reaching a phase are excluded from that latency, not counted as zero. Thread concurrency does not establish CPU-core utilization.'}


def analyze(folder):
    folder = Path(folder).resolve(); manifest = read(folder / 'manifest.json')
    if manifest['status'] != 'complete':
        raise ValueError('Cannot summarize an incomplete capture as successful')
    result = {'manifest': manifest, 'warnings': [], 'metrics': {}}
    lines = [f'# 性能采集：{manifest["label"]}', '', f'模式：{manifest["mode"]}；场景：{manifest["scenario"]}；构建：{manifest["configuration"]}。', '']
    if manifest['mode'] == 'trace':
        lines += ['此运行用于定位原因，含采样、时间线和埋点开销；性能结论应使用独立 baseline。', '']
    if manifest['scenario'] == 'simulation':
        raw = read(folder / 'simulation.json')
        result['metrics'] = {'tickMeanMs': raw['meanTickMs'], 'tickP99Ms': raw['p99'], 'tickMaxMs': raw['max'],
                             'allocationMB': raw['allocatedBytes'] / 1048576}
        result['stateHash'] = raw['stateHash']
        result['stateHashKind'] = 'final'
        result['profiling'] = raw.get('profiling')
    else:
        raw = read(next(folder.glob('frame-tps-*.json')))
        frames = read(next(folder.glob('frame-diagnostic-*.json')))
        result['metrics'] = {'tickMeanMs': raw['tickMilliseconds']['mean'], 'tickP99Ms': raw['tickMilliseconds']['p99'],
                             'frameP99Ms': raw['frameMilliseconds']['p99'], 'mainP99Ms': raw['processMilliseconds']['p99'],
                             'syncP99Ms': raw['syncMilliseconds']['p99'], 'tps': raw['achievedTps'],
                             'allocationMB': raw['allocationMB'], 'cpuCoreEquivalent': raw['processCpuCoreEquivalent']}
        result['stateHash'] = frames['SceneStateHash']; result['stateHashKind'] = 'initial'
        result['profiling'] = raw.get('profilingWholeRunIncludingWarmup')
        result['view'] = {k: frames[k] for k in ('maxFps', 'vsync', 'viewportSize', 'windowSize', 'cameraSize', 'shadows')}
        lines += ['帧指标包含模拟与场景更新；无窗口运行不代表真实渲染 FPS。GPU 计时默认关闭，未测到的 GPU 成本不是零。', '']
    lines += ['| 指标 | 数值 |', '| --- | ---: |']
    for key, value in result['metrics'].items():
        lines.append(f'| {key} | {value:.4f} |')
    if (folder / 'timeline.json').exists():
        result['timeline'] = timeline_summary(read(folder / 'timeline.json'))
        lines += ['', '## 后台任务时间线', '', '完整捕获包含启动与预热；下表仅统计到达对应阶段的任务。单位 ms。', '',
                  '| 阶段 | 样本数 | 中位数 | P99 |', '| --- | ---: | ---: | ---: |']
        for name, s in result['timeline']['taskTimingMs'].items():
            lines.append(f'| {name} | {s["count"]} | {s["median"]:.3f} | {s["p99"]:.3f} |')
        if result['timeline']['droppedEvents']:
            result['warnings'].append('Timeline event limit reached; data is truncated.')
        lines += ['', '[线程时间线](timeline.json)：导入 Perfetto 或 Chrome tracing，搜索任务 ID；tid 为托管线程 ID，不能直接与 OS tid 对齐。', '']
    counters = folder / 'counters.json'
    if counters.exists():
        try:
            data = read(counters)
            result['runtimeCounters'] = {'samples': len(data.get('Events', [])), 'providers': sorted({e['provider'] for e in data.get('Events', [])})}
            if 'System.Runtime' not in result['runtimeCounters']['providers']:
                result['warnings'].append('System.Runtime is unavailable in this host; NoDwarf-Runtime counters use public .NET APIs. Missing provider is not zero activity.')
            lines += ['[运行时计数器](counters.json)：检查 GC、分配速率、线程池队列与锁竞争；完整原始事件保存在 nettrace。', '']
        except json.JSONDecodeError:
            result['warnings'].append('Counters JSON incomplete; inspect counters.log.')
    elif manifest['mode'] == 'trace':
        result['warnings'].append('No counter output; short captures may finish before attachment. See counters.log.')
    if (folder / 'runtime.json').exists():
        samples = read(folder / 'runtime.json')['samples']
        window = result.get('timeline', {}).get('measurementWindowMs', {})
        selected = [x for x in samples if window.get('start', 0) <= x['atMs'] <= window.get('end', float('inf'))]
        result['runtimeSampleCount'] = len(samples)
        if len(selected) >= 2:
            first, last = selected[0], selected[-1]
            runtime = {k + 'Delta': last[k] - first[k] for k in ('allocatedBytes', 'gc0', 'gc1', 'gc2', 'gcPauseTotalMs', 'lockContentions', 'threadPoolCompleted')}
            runtime.update(sampledSeconds=(last['atMs'] - first['atMs']) / 1000, peakPoolPending=max(x['threadPoolPending'] for x in selected), peakPoolThreads=max(x['threadPoolThreads'] for x in selected))
            result['runtimeMeasurementSamples'] = runtime
            lines += ['## 运行时采样', '', '只取测量窗口内每秒采样点；首尾不足一秒的部分不计，累计暂停不是每次暂停的精确时刻。', '', '```json', json.dumps(runtime, indent=2), '```', '', '[完整运行时样本](runtime.json)', '']
    if manifest.get('collectorExitCodes') and any(manifest['collectorExitCodes']):
        result['warnings'].append('One or more optional collectors failed or were terminated; see counters.log/native.log.')
    if (folder / 'hotspots-self.txt').exists():
        lines += ['## 托管调用栈采样', '', '以下为整个捕获期的托管栈采样，包含预热和诊断收尾；可能包含等待线程，不能解释为纯 CPU 占比。不是精确调用次数或 OS 调度状态分析。', '', '```text',
                  (folder / 'hotspots-self.txt').read_text().strip(), '```', '',
                  '[完整原始采样](managed.nettrace)；[包含子调用的热点](hotspots-inclusive.txt)。Speedscope 文件可查看调用树与时间顺序。', '']
    if (folder / 'native-sample.txt').exists():
        lines += ['[macOS 原生线程堆栈](native-sample.txt)：覆盖采集起始 10 秒，包含预热；等待线程的样本不等于 CPU 使用时间。', '']
    if (folder / 'native.trace').exists():
        lines += ['原生 Instruments 文件：`native.trace`，用 Instruments 打开。', '']
    lines += ['## 数据完整性', '', f'状态哈希类型：{result["stateHashKind"]}；哈希：`{result["stateHash"]}`。', '',
              f'构建 SHA256：`{manifest["binarySha256"]}`。', '', '[运行参数和环境](manifest.json) · [源码哈希](sources.json) · [捕获日志](capture.log)', '']
    lines += [f'- 注意：{warning}' for warning in result['warnings']]
    write(folder / 'summary.json', result)
    (folder / 'report.md').write_text('\n'.join(lines) + '\n')
    return result


def compare(before_paths, after_paths, target):
    before = [read(Path(p) / 'summary.json') for p in before_paths]
    after = [read(Path(p) / 'summary.json') for p in after_paths]
    first = before[0]
    for group in (before, after):
        if len({r['manifest']['binarySha256'] for r in group}) != 1:
            raise ValueError('Each comparison group must use one binary build')
    for run in before + after:
        if run['manifest']['mode'] != 'baseline' or run['manifest']['status'] != 'complete':
            raise ValueError('Compare only successful baseline runs; traced samples are diagnostic, not a speed comparison')
        for key in ('scenario', 'configuration', 'headless', 'quick', 'speed', 'platform', 'machine', 'logicalProcessors', 'gpuTiming', 'hardware', 'dotnetVersion'):
            if run['manifest'][key] != first['manifest'][key]:
                raise ValueError(f'Incompatible baseline: {key}')
        if run['stateHash'] != first['stateHash'] or run.get('view') != first.get('view'):
            raise ValueError('State hash or view differs; establish equivalent workload/correctness before timing comparison')
    rows = []
    for metric in first['metrics']:
        b = statistics.median(r['metrics'][metric] for r in before)
        a = statistics.median(r['metrics'][metric] for r in after)
        rows.append({'metric': metric, 'beforeMedian': b, 'afterMedian': a, 'changePercent': (a / b - 1) * 100 if b else None})
    report = {'before': before_paths, 'after': after_paths, 'rows': rows,
              'note': 'Medians across runs. Negative is lower, not automatically better (TPS differs). Prefer at least 3 alternating runs per build. Same hardware/settings do not control thermal/background load.'}
    target = Path(target).resolve()
    if target.exists():
        raise ValueError('Comparison output already exists; choose a new path')
    target.mkdir(parents=True)
    write(target / 'comparison.json', report)
    lines = ['# 性能对比', '', report['note'], '', '| 指标 | Before | After | 变化 |', '| --- | ---: | ---: | ---: |']
    for row in rows:
        change = 'n/a' if row['changePercent'] is None else f'{row["changePercent"]:+.2f}%'
        lines.append(f'| {row["metric"]} | {row["beforeMedian"]:.4f} | {row["afterMedian"]:.4f} | {change} |')
    (target / 'comparison.md').write_text('\n'.join(lines) + '\n')
    print(target / 'comparison.md')


def check():
    import tempfile
    subprocess.run([sys.executable, str(Path(__file__).parent / 'test_profile.py')], check=True)
    subprocess.run(['dotnet', 'build', str(ROOT / 'benchmarks/NavigationBench.csproj'), '-c', 'Release', '--nologo'], cwd=PROJECT, check=True, timeout=180)
    with tempfile.TemporaryDirectory(prefix='nodwarf-trace-check-') as temporary:
        for enabled in ('0', '1'):
            folder = Path(temporary) / enabled; folder.mkdir()
            env = os.environ.copy()
            env.update(NODWARF_PERF_OUTPUT_DIR=str(folder), NODWARF_PROFILE_TRACE=enabled, NODWARF_PROFILE_HANDSHAKE='0')
            subprocess.run(['dotnet', str(ROOT / 'benchmarks/bin/Release/net8.0/NavigationBench.dll'), '--diagnostic-checks'], env=env, check=True, timeout=30)
            if enabled == '0' and (folder / 'timeline.json').exists():
                raise RuntimeError('Disabled capture wrote a timeline')
    print('PROFILE_CHECK_OK')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    sub.add_parser('setup'); sub.add_parser('doctor'); sub.add_parser('check')
    rec = sub.add_parser('record')
    rec.add_argument('--label', required=True)
    rec.add_argument('--mode', choices=['baseline', 'trace'], default='trace')
    rec.add_argument('--scenario', choices=['game', 'interior', 'environment', 'simulation'], default='game')
    rec.add_argument('--gpu-timing', action='store_true', help='Enable Godot renderer timings; zero may be unavailable')
    rec.add_argument('--headless', action='store_true'); rec.add_argument('--quick', action='store_true')
    rec.add_argument('--speed', type=int, choices=[1, 3], default=1)
    rec.add_argument('--native', choices=['none', 'sample', 'instruments'], default='none')
    rec.add_argument('--timeout', type=int, default=180)
    rec.add_argument('--no-build', action='store_true', help='Reuse the existing binary; its hash is still recorded')
    ana = sub.add_parser('analyze'); ana.add_argument('folder')
    comp = sub.add_parser('compare')
    comp.add_argument('--before', nargs='+', required=True); comp.add_argument('--after', nargs='+', required=True)
    comp.add_argument('--output', required=True)
    args = parser.parse_args()
    if args.action == 'setup': setup()
    elif args.action == 'doctor': doctor()
    elif args.action == 'check': check()
    elif args.action == 'record': record(args)
    elif args.action == 'analyze': analyze(args.folder)
    elif args.action == 'compare': compare(args.before, args.after, args.output)


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, ValueError, OSError, subprocess.SubprocessError) as error:
        print(f'PROFILE_FAILED: {error}', file=sys.stderr)
        sys.exit(1)

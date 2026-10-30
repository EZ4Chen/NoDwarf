import copy
import json
from pathlib import Path
import tempfile
import unittest
import profile as workflow


class WorkflowTests(unittest.TestCase):
    def test_shared_core_change_invalidates_build_identity(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp); entry = folder/'NoDwarf.dll'; core = folder/'NoDwarf.Core.dll'
            entry.write_bytes(b'unchanged renderer'); core.write_bytes(b'old simulation')
            before, files = workflow.build_fingerprint(entry)
            core.write_bytes(b'new simulation')
            after, changed = workflow.build_fingerprint(entry)
            self.assertEqual(files['NoDwarf.dll'], changed['NoDwarf.dll'])
            self.assertNotEqual(before, after)
            self.assertEqual(after, workflow.build_fingerprint(entry)[0])

    def test_task_latency_does_not_turn_missing_completion_into_zero(self):
        def event(phase, ts, task=1):
            return dict(name='terrain', ph='i', ts=ts, tid=7, args=dict(phase=phase, task=task))
        result = workflow.timeline_summary(dict(metadata=dict(droppedEvents=0), traceEvents=[
            event('queued', 1000), event('started', 4000), event('computed', 9000), event('collected', 15000),
            event('queued', 16000, 2), event('superseded', 17000, 2)]))
        self.assertEqual(result['taskTimingMs']['terrain.queueMs']['median'], 3)
        self.assertEqual(result['taskTimingMs']['terrain.collectWaitMs']['median'], 6)
        self.assertEqual(result['taskTimingMs']['terrain.computeMs']['count'], 1)
        self.assertNotIn('terrain.readyToApplyMs', result['taskTimingMs'])

    def test_comparison_rejects_mismatched_or_instrumented_workloads(self):
        manifest = dict(mode='baseline', status='complete', binarySha256='build-a', scenario='game',
                        configuration='Debug', headless=False, quick=True, speed=1, platform='test',
                        machine='arm64', logicalProcessors=8, gpuTiming=False, hardware='test', dotnetVersion='8.0.426')
        row = dict(manifest=manifest, metrics=dict(tickMeanMs=2), stateHash='same', view=dict(cameraSize=66))
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp); a=base/'a'; b=base/'b'; a.mkdir(); b.mkdir()
            workflow.write(a/'summary.json', row)
            for key, value in [('mode', 'trace'), ('headless', True), ('speed', 3)]:
                changed=copy.deepcopy(row);changed['manifest'][key]=value
                workflow.write(b/'summary.json',changed)
                with self.assertRaises(ValueError):workflow.compare([str(a)],[str(b)],base/'reject')
            changed=copy.deepcopy(row);changed['stateHash']='different'
            workflow.write(b/'summary.json',changed)
            with self.assertRaises(ValueError):workflow.compare([str(a)],[str(b)],base/'reject')
            changed=copy.deepcopy(row);changed['metrics']['tickMeanMs']=1;changed['manifest']['binarySha256']='build-b'
            workflow.write(b/'summary.json',changed)
            workflow.compare([str(a)],[str(b)],base/'valid')
            self.assertEqual(workflow.read(base/'valid/comparison.json')['rows'][0]['changePercent'],-50)
            with self.assertRaises(ValueError):workflow.compare([str(a)],[str(b)],base/'valid')


if __name__ == '__main__':unittest.main()

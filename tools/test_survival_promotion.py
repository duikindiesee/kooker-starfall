"""Synthetic verifier tests only: never launches a player or invokes retention."""
import copy
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class SurvivalPromotionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='starfall-verifier-fixture-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'tools').mkdir()
        for name in ('check-survival-promotion.ps1', 'get-integrated-content-hash.ps1'):
            shutil.copyfile(ROOT / 'tools' / name, self.root / 'tools' / name)
        self.build = 'KookerStarfallIntegrated-0.0.11-survival.1-20990101-000000'
        folder = self.root / 'Builds' / self.build
        folder.mkdir(parents=True)
        payload = b'SYNTHETIC NONEXECUTABLE TEST FIXTURE'
        (folder / 'KookerStarfallIntegrated.exe').write_bytes(payload)
        entry = f'KookerStarfallIntegrated.exe\t{len(payload)}\t{hashlib.sha256(payload).hexdigest()}\n'
        self.digest = hashlib.sha256(entry.encode()).hexdigest()
        self.manifest = dict(buildId=self.build, sourceCommit='a' * 40)
        self.process = dict(schema='starfall.normal-process-observation.v2', build=self.build,
                            buildContentSha256=self.digest, normalFlagsVerified=True,
                            flags=dict(npcSurvivalRuntime=True, npcSurvivalDeathAcceptance=False,
                                       integratedSmoke=False, npcSmoke=False, npcRealProbe=False))
        self.death_process = copy.deepcopy(self.process)
        self.death_process['flags']['npcSurvivalDeathAcceptance'] = True
        self.death_process['normalFlagsVerified'] = False
        self.events = []
        for index, choice in enumerate(('explore north', 'eat fruit', 'drink spring')):
            row = dict(world='test-world', actor='test-actor', kind='decision', code='live-admitted',
                       choice=choice, model='test-only', tick=index * 100, finishReason='stop',
                       requestHash=str(index + 1) * 64, responseHash=str(index + 4) * 64)
            self.events.append(row)
            if index:
                self.events.append(dict(row, kind='food', foodDelta=1200 if index == 1 else 0,
                                        waterDelta=400 if index == 1 else 2000))
            self.events.append(dict(row, kind='route', code='reached'))
        self.death = dict(status='PASS_COMPILED_ACCELERATED_CAUSE_AND_SAFE_RETURN_NOT_NATURAL_PACING',
                          world='test-world', actor='test-actor', geometryPreserved=True, cases=[])
        for index, cause in enumerate(('prolonged-dehydration', 'prolonged-starvation')):
            self.death['cases'].append(dict(cause=cause, realPhysiologyDeath=True,
                safeRefugeReturn=True, scopedReload=True, worldAndActorPreserved=True,
                noInventedKnowledge=True, deathHash=str(index + 6) * 64,
                incarnationBefore=index + 1, incarnationAfter=index + 2))
        self.receipt = dict(schema='starfall.survival-today-scoped-receipt.v1', build=self.build,
            sourceCommit='a' * 40, buildContentSha256=self.digest,
            ordinaryNormalPlay=dict(process='evidence/process.json', evidence='evidence/events.jsonl',
                durationSecondsAtFinalLiveSample=300, buildContentSha256AfterExit=self.digest),
            acceleratedDeathDiagnostic=dict(process='evidence/death-process.json',
                evidence='evidence/death.json', buildContentSha256AfterExit=self.digest))

    def run_gate(self):
        evidence = self.root / 'evidence'
        evidence.mkdir(exist_ok=True)
        for name, value in (('manifest.json', self.manifest), ('receipt.json', self.receipt),
                            ('process.json', self.process), ('death-process.json', self.death_process),
                            ('death.json', self.death)):
            (evidence / name).write_text(json.dumps(value), encoding='utf-8')
        (evidence / 'events.jsonl').write_text('\n'.join(map(json.dumps, self.events)), encoding='utf-8')
        return subprocess.run([shutil.which('pwsh') or 'pwsh', '-NoProfile', '-File',
            str(self.root / 'tools/check-survival-promotion.ps1'), '-BuildManifest',
            str(evidence / 'manifest.json'), '-ScopedReceipt', str(evidence / 'receipt.json')],
            capture_output=True, text=True, timeout=20)

    def reject(self):
        result = self.run_gate()
        self.assertNotEqual(result.returncode, 0, result.stdout)

    def test_complete_synthetic_contract(self):
        result = self.run_gate()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('NOT_RELEASE', result.stdout)

    def test_missing_drink(self):
        self.events = [r for r in self.events if not (r['kind'] == 'food' and r['choice'] == 'drink spring')]
        self.reject()

    def test_old_process_schema(self):
        self.process['schema'] = 'starfall.normal-process-observation.v1'
        self.reject()

    def test_mismatched_content(self):
        self.process['buildContentSha256'] = '0' * 64
        self.reject()

    def test_diagnostic_cannot_be_normal(self):
        self.process['flags']['npcSurvivalDeathAcceptance'] = True
        self.reject()

    def test_short_soak(self):
        self.receipt['ordinaryNormalPlay']['durationSecondsAtFinalLiveSample'] = 220
        self.reject()

    def test_cross_actor_events(self):
        self.events[-1]['actor'] = 'someone-else'
        self.reject()

    def test_food_without_admitted_provenance(self):
        next(r for r in self.events if r['kind'] == 'food')['requestHash'] = '0' * 64
        self.reject()

    def test_missing_death_reload(self):
        self.death['cases'][0]['scopedReload'] = False
        self.reject()

    def test_death_wrong_incarnation(self):
        self.death['cases'][0]['incarnationAfter'] = 99
        self.reject()

    def test_missing_death_post_hash(self):
        del self.receipt['acceleratedDeathDiagnostic']['buildContentSha256AfterExit']
        self.reject()

    def test_evidence_path_escape(self):
        self.receipt['ordinaryNormalPlay']['process'] = '../outside.json'
        self.reject()

    def test_string_is_not_a_boolean_pass(self):
        self.death['cases'][0]['scopedReload'] = 'false'
        self.reject()

    def test_string_normal_flag_rejected(self):
        self.process['normalFlagsVerified'] = 'false'
        self.reject()

    def test_string_duration_rejected(self):
        self.receipt['ordinaryNormalPlay']['durationSecondsAtFinalLiveSample'] = '900'
        self.reject()


if __name__ == '__main__':
    unittest.main()

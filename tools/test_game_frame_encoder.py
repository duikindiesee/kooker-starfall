"""Validation tests only; synthetic PNG encode evidence is recorded separately."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('encoder', Path(__file__).with_name('encode-game-frames.py'))
encoder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(encoder)


class FrameContractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.index = self.root / 'frames.jsonl'
        self.rows = [dict(frame=i, utcElapsedMs=(i-1)*100, unityTick=i,
                          width=320, height=180, sourceCamera='synthetic', readbackStatus='ok')
                     for i in (1, 2)]
        for i in (1, 2):
            (self.root / f'frame-{i:06d}.png').write_bytes(b'validation-only-fixture')

    def validate(self, end=200):
        self.index.write_text('\n'.join(json.dumps(row) for row in self.rows))
        return encoder.validate(self.index, end)

    def test_ordered_frames(self):
        self.assertEqual(2, len(self.validate()))

    def test_duplicate_time(self):
        self.rows[1]['utcElapsedMs'] = 0
        with self.assertRaises(ValueError): self.validate()

    def test_time_string(self):
        self.rows[1]['utcElapsedMs'] = '100'
        with self.assertRaises(ValueError): self.validate()

    def test_boolean_frame(self):
        self.rows[0]['frame'] = True
        with self.assertRaises(ValueError): self.validate()

    def test_missing_png(self):
        (self.root / 'frame-000002.png').unlink()
        with self.assertRaises(ValueError): self.validate()

    def test_changed_camera(self):
        self.rows[1]['sourceCamera'] = 'different'
        with self.assertRaises(ValueError): self.validate()

    def test_changed_dimensions(self):
        self.rows[1]['width'] = 640
        with self.assertRaises(ValueError): self.validate()

    def test_readback_failed(self):
        self.rows[1]['readbackStatus'] = 'error'
        with self.assertRaises(ValueError): self.validate()

    def test_end_not_after_frame(self):
        with self.assertRaises(ValueError): self.validate(100)

    def test_nonfinite_time(self):
        self.rows[1]['utcElapsedMs'] = float('nan')
        with self.assertRaises(ValueError): self.validate()


if __name__ == '__main__':
    unittest.main()

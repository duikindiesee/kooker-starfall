import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('runner', Path(__file__).with_name('run-living-memory.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class SelectionTests(unittest.TestCase):
    def setUp(self):
        self.inventory = {'models': [
            {'key': 'google/gemma-4-e4b', 'format': 'mlx', 'loaded_instances': [{'id': 'google/gemma-4-e4b'}]},
            {'key': 'google/gemma-4-e4b', 'format': 'gguf', 'loaded_instances': [{'id': 'starfall-local-e4b'}]}]}

    def test_explicit_local_with_both_loaded(self):
        self.assertEqual(runner.select_loaded_instance(self.inventory, 'starfall-local-e4b', 'gguf')['format'], 'gguf')

    def test_explicit_mac_with_both_loaded(self):
        self.assertEqual(runner.select_loaded_instance(self.inventory, 'google/gemma-4-e4b', 'mlx')['format'], 'mlx')

    def test_missing_or_wrong_format_rejected(self):
        for identity, fmt in [('absent', 'gguf'), ('starfall-local-e4b', 'mlx')]:
            with self.assertRaises(RuntimeError):
                runner.select_loaded_instance(self.inventory, identity, fmt)

    def test_duplicate_rejected(self):
        self.inventory['models'].append(self.inventory['models'][1])
        with self.assertRaises(RuntimeError):
            runner.select_loaded_instance(self.inventory, 'starfall-local-e4b', 'gguf')

    def test_wrong_key_rejected(self):
        self.inventory['models'][1]['key'] = 'other'
        with self.assertRaises(RuntimeError):
            runner.select_loaded_instance(self.inventory, 'starfall-local-e4b', 'gguf')


if __name__ == '__main__':
    unittest.main()

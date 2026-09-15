"""Synthetic linkage checks; no inference or player launch."""
import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("context_audit", Path(__file__).with_name("audit-survival-context.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ContextTests(unittest.TestCase):
    def rows(self):
        request = dict(world="w", actor="a", kind="model", code="request-issued",
                       modelRequestSequence=1, requestHash="a"*64, model="test",
                       energy=1800, hydration=2500, foodTick=900, tick=45000,
                       carriedFruit=1, offeredActions="eat fruit,explore west")
        decision = dict(request, kind="decision", code="live-admitted", tick=45050,
                        choice="eat fruit", responseHash="b"*64, finishReason="stop")
        return [request, decision]

    def test_complete(self):
        self.assertEqual(module.audit(self.rows())["admitted"], 1)

    def test_admission_state_may_change(self):
        rows = self.rows()
        rows[1]["energy"] = 1700
        self.assertEqual(module.audit(rows)["severe_energy_requests"], 1)

    def test_bad_linkages(self):
        for field, value in (("modelRequestSequence", 2), ("requestHash", "c"*64),
                             ("model", "other"), ("choice", "drink spring"),
                             ("tick", 1), ("finishReason", "length")):
            with self.subTest(field=field):
                rows = self.rows()
                rows[1][field] = value
                with self.assertRaises(ValueError):
                    module.audit(rows)

    def test_duplicate_admission(self):
        rows = self.rows()
        rows.append(copy.deepcopy(rows[1]))
        with self.assertRaises(ValueError):
            module.audit(rows)

    def test_old_evidence(self):
        with self.assertRaises(ValueError):
            module.audit(self.rows()[1:])


if __name__ == "__main__":
    unittest.main()

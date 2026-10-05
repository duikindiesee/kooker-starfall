"""Read-only request/decision linkage audit, not a runtime or release certificate."""
import argparse
import json
from pathlib import Path
import re


def audit(rows):
    issued = {}
    admitted = set()
    scope = None
    counts = {"requests": 0, "admitted": 0, "severe_energy_requests": 0,
              "severe_hydration_requests": 0}
    for line, row in enumerate(rows, 1):
        current = (row.get("world"), row.get("actor"))
        if not all(current) or (scope is not None and current != scope):
            raise ValueError(f"line {line}: missing or mixed scope")
        scope = current
        if row.get("kind") == "startup":
            if issued:
                raise ValueError("Separate launches must be audited separately")
        if row.get("code") == "request-issued" and row.get("kind") == "model":
            seq = row.get("modelRequestSequence")
            if type(seq) is not int or seq <= 0 or seq in issued:
                raise ValueError(f"line {line}: invalid or duplicate request sequence")
            if not re.fullmatch(r"[0-9a-f]{64}", row.get("requestHash") or ""):
                raise ValueError(f"line {line}: missing request hash")
            for field in ("energy", "hydration", "foodTick", "tick", "carriedFruit"):
                if type(row.get(field)) is not int or row[field] < 0:
                    raise ValueError(f"line {line}: invalid request measurement {field}")
            if not row.get("model") or not row.get("offeredActions"):
                raise ValueError(f"line {line}: missing provider or offered actions")
            issued[seq] = row
            counts["requests"] += 1
            counts["severe_energy_requests"] += row["energy"] < 2000
            counts["severe_hydration_requests"] += row["hydration"] < 2000
        if row.get("kind") == "decision" and row.get("code") == "live-admitted":
            seq = row.get("modelRequestSequence")
            request = issued.get(seq)
            if request is None or seq in admitted:
                raise ValueError(f"line {line}: admission has no unique preceding request")
            if any(row.get(field) != request[field] for field in ("requestHash", "model")):
                raise ValueError(f"line {line}: request/model mismatch")
            if (type(row.get("tick")) is not int or row["tick"] < request["tick"]
                    or row.get("choice") not in request["offeredActions"].split(",")
                    or row.get("finishReason") != "stop"
                    or not re.fullmatch(r"[0-9a-f]{64}", row.get("responseHash") or "")):
                raise ValueError(f"line {line}: invalid admitted choice provenance")
            admitted.add(seq)
            counts["admitted"] += 1
    if not issued or not admitted:
        raise ValueError("Request-time evidence and admitted decisions are required")
    return {"status": "CONTEXT_LINKAGE_ONLY_NOT_GAMEPLAY_ACCEPTANCE", **counts}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("events", type=Path)
    args = parser.parse_args()
    try:
        with args.events.open(encoding="utf-8-sig") as stream:
            print(json.dumps(audit(json.loads(line) for line in stream if line.strip()), indent=2))
    except (ValueError, OSError) as error:
        parser.exit(1, str(error) + "\n")

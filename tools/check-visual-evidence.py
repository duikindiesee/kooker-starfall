"""Reject missing, unreadable or effectively uniform runtime PNG evidence.

This is a technical sanity gate, not visual or gameplay acceptance. Requires Pillow.
Reads files only; emits a JSON receipt to stdout and exits nonzero on failure.
"""
import argparse
import json
from pathlib import Path

from PIL import Image, ImageStat


def inspect(path):
    try:
        with Image.open(path) as source:
            source.load()
            image = source.convert("RGB")
            stat = ImageStat.Stat(image)
            width, height = image.size
            varied = max(stat.stddev) > 1.0
            return {"file": str(path), "passed": width >= 320 and height >= 180 and varied,
                    "width": width, "height": height,
                    "mean_rgb": stat.mean, "stddev_rgb": stat.stddev,
                    "reason": "inspect visually" if varied else "uniform or blank capture"}
    except (OSError, ValueError) as error:
        return {"file": str(path), "passed": False, "reason": str(error)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    paths = sorted(args.directory.glob("*.png"))
    results = [inspect(path) for path in paths]
    passed = bool(results) and all(item["passed"] for item in results)
    print(json.dumps({"status": "PASS" if passed else "FAIL", "count": len(results),
                      "scope": "PNG technical sanity only; not visual acceptance",
                      "checks": results}, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())

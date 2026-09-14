"""Reject missing, unreadable or effectively uniform runtime PNG evidence.

This is a technical sanity gate, not visual or gameplay acceptance. Requires Pillow.
Reads files only; emits a JSON receipt to stdout and exits nonzero on failure.
"""
import argparse
import json
from pathlib import Path
import tempfile

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
                    "reason": "capture too small" if width < 320 or height < 180 else
                              ("inspect visually" if varied else "uniform or blank capture")}
    except (OSError, ValueError) as error:
        return {"file": str(path), "passed": False, "reason": str(error)}


def self_test():
    with tempfile.TemporaryDirectory(prefix="starfall-visual-check-") as directory:
        root = Path(directory)
        for name, colour in (("black", "black"), ("white", "white")):
            path = root / (name + ".png")
            Image.new("RGB", (320, 180), colour).save(path)
            assert not inspect(path)["passed"], name
        varied = Image.new("RGB", (320, 180), "black")
        varied.paste("white", (160, 0, 320, 180))
        path = root / "varied.png"
        varied.save(path)
        assert inspect(path)["passed"]
        varied.resize((32, 18)).save(path)
        assert not inspect(path)["passed"]
        assert not inspect(root / "missing.png")["passed"]
    print("PASS: black, white, undersized and missing captures rejected; varied capture accepted")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, nargs="?")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--require", action="append", default=[], metavar="PNG_NAME",
                        help="Required capture basename; may be repeated")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.directory is None:
        parser.error("directory is required unless --self-test is used")
    paths = sorted(args.directory.glob("*.png"))
    results = [inspect(path) for path in paths]
    present = {path.name for path in paths}
    results.extend({"file": name, "passed": False, "reason": "required capture missing"}
                   for name in args.require if name not in present)
    passed = bool(results) and all(item["passed"] for item in results)
    print(json.dumps({"status": "PASS" if passed else "FAIL", "count": len(results),
                      "scope": "PNG technical sanity only; not visual acceptance",
                      "checks": results}, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())

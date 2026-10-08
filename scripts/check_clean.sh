#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

status=0

python3 - <<'PY' || status=1
import subprocess
import sys

skip_suffixes = {".onnx", ".pdf", ".png", ".ico"}
skip_paths = {"scripts/check_clean.sh"}
dash = b"\xe2\x80\x94"
endash = b"\xe2\x80\x93"
markers = (b"TODO", b"FIXME", b"XXX")

files = subprocess.run(["git", "ls-files"], capture_output=True, text=True).stdout.split()

dash_hits = []
marker_hits = []
comment_hits = []

for name in files:
    if name in skip_paths:
        continue
    suffix = name.rsplit(".", 1)[-1] if "." in name else ""
    if suffix in {"json", "txt", "pdf", "onnx", "png", "ico", "lock"}:
        continue
    data = open(name, "rb").read()
    if dash in data or endash in data:
        dash_hits.append(name)
    if any(marker in data for marker in markers):
        marker_hits.append(name)
    if name.endswith(".cs"):
        for line in data.decode("utf-8", "ignore").splitlines():
            if line.strip().startswith("//"):
                comment_hits.append(name)
                break

failed = False

if dash_hits:
    print("FAIL em dash or en dash in: " + ", ".join(dash_hits))
    failed = True
else:
    print("PASS no em dash or en dash")

if marker_hits:
    print("FAIL marker in: " + ", ".join(marker_hits))
    failed = True
else:
    print("PASS no markers")

if comment_hits:
    print("FAIL comment in: " + ", ".join(comment_hits))
    failed = True
else:
    print("PASS no comments in C# source")

sys.exit(1 if failed else 0)
PY

staged=$(git diff --cached --name-only 2>/dev/null | grep -cE 'auth\.json|\.env|\.key' || true)
if [ "${staged:-0}" -gt 0 ]; then
  echo "FAIL staged secret"
  status=1
else
  echo "PASS no staged secret"
fi

exit "$status"

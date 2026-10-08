#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

echo "fast tier: logic, routing, grounding, no model calls"
dotnet test --nologo 2>&1 | grep -E "Passed!|Failed!"

echo
echo "build tier"
dotnet build --nologo -v quiet 2>&1 | grep -cE "error|warning" || true
echo "warnings and errors above, zero is the pass condition"

echo
echo "web build"
(cd web && npm run build 2>&1 | grep -E "built in|error")

echo
echo "clean gate"
bash scripts/check_clean.sh

echo
echo "integration tier: live model calls, minutes not seconds"
cat <<'NOTE'
The 78 case suite below drives a real LLM and cannot finish in under a minute.
Run it before a demo, not during one.

  colima start
  docker run -d --name dpa-qdrant -p 6333:6333 -p 6334:6334 \
    -v dpa_qdrant:/qdrant/storage qdrant/qdrant:latest
  ollama serve

  LLM_CHAIN=ollama ASPNETCORE_URLS=http://127.0.0.1:5310 \
    dotnet run --project src/Dpa.Rag.Api &

  CHAT_URL=http://127.0.0.1:5310/api/chat \
  STRESS_DEADLINE=900 STRESS_WORKERS=2 \
    python3 scripts/stress_test.py

Use STRESS_WORKERS=2 on a 16GB laptop. Raising it overloads a local 7B model
and produces timeouts that look like application failures.
NOTE
import json
import os
import subprocess
import sys
import time

CASES = [
    ("penalties for unauthorised access", True, "29"),
    ("data portability right", True, "18"),
    ("rights of the data subject", True, "16"),
    ("who is exempt from the Act", True, "4"),
    ("what is sensitive personal information", True, "13"),
    ("security of personal information", True, "20"),
    ("penalties for a security breach", True, None),
    ("how long may records be kept", True, None),
    ("bake sourdough bread", False, None),
    ("capital of France", False, None),
]


def ask(question):
    started = time.time()
    raw = subprocess.run(
        [
            "curl", "-s", "-m", "180", "-X", "POST", os.environ.get("CHAT_URL", "http://127.0.0.1:5199/api/chat"),
            "-H", "Content-Type: application/json",
            "-d", json.dumps({"question": question}),
        ],
        capture_output=True, text=True).stdout
    return json.loads(raw), time.time() - started


failures = []
for question, answerable, expected in CASES:
    reply, seconds = ask(question)
    cited = set(__import__("re").findall(r"Section (\d+)", reply["answer"]))
    supplied = {source["number"] for source in reply["sources"]}
    invented = cited - supplied
    declined = any(marker in reply["answer"] for marker in ("does not cover", "cannot be answered", "not pretend to answer", "outside Republic Act", "outside this Act", "wala iyon sa batas", "hindi saklaw"))

    ok = not invented
    ok = ok and not (answerable and declined)
    ok = ok and not (not answerable and not declined)
    ok = ok and (expected is None or expected in supplied)

    if not ok:
        failures.append(question)

    print(f"{'PASS' if ok else 'FAIL'}  {question[:44]:<44} {seconds:5.1f}s")
    print(f"      {reply['answer'][:110]}")

print()
print(f"{len(CASES) - len(failures)}/{len(CASES)} passed")
sys.exit(1 if failures else 0)

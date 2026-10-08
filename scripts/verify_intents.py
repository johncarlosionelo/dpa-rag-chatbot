import json
import os
import subprocess
import sys
import time

CASES = [
    ("penalties for unauthorised access", True, "29", "grounded"),
    ("data portability right", True, "18", "grounded"),
    ("rights of the data subject", True, "16", "grounded"),
    ("who is exempt from the Act", True, "4", "grounded"),
    ("what is sensitive personal information", True, "13", "grounded"),
    ("security of personal information", True, "20", "grounded"),
    ("penalties for a security breach", True, None, "grounded"),
    ("how long may records be kept", True, None, "grounded"),
    ("section 12", True, "12", "explicit_section"),
    ("expand 32", True, "32", "explicit_section"),
    ("what is section 12", True, "12", "explicit_section"),
    ("hello", False, None, "greeting"),
    ("hi there", False, None, "greeting"),
    ("kamusta", True, None, "greeting"),
    ("pwede mo ba akong tulungan", True, None, "scope"),
    ("what can you do", False, None, "scope"),
    ("How do I bake sourdough bread?", False, None, "redirect"),
    ("what is the capital of France", False, None, "redirect"),
]

FOLLOW_UPS = [
    ("expand 12", ["12"]),
    ("can u explain it more easier for me so i can understand the law better", ["12"]),
    ("go deeper", ["12"]),
    ("why", ["12"]),
]

PRIOR = [
    {"role": "user", "content": "What are the grounds for processing personal information?"},
    {"role": "assistant", "content": "Under Section 12, processing is permitted when consent is given."},
]


def ask(question, history=None):
    started = time.time()
    payload = {"question": question}
    if history:
        payload["history"] = history
    raw = subprocess.run(
        [
            "curl", "-s", "-m", "180", "-X", "POST", os.environ.get("CHAT_URL", "http://127.0.0.1:5199/api/chat"),
            "-H", "Content-Type: application/json",
            "-d", json.dumps(payload),
        ],
        capture_output=True, text=True).stdout
    return json.loads(raw), time.time() - started


failures = []
for question, answerable, expected, kind in CASES:
    reply, seconds = ask(question)
    cited = set(__import__("re").findall(r"Section (\d+)", reply["answer"]))
    supplied = {source["number"] for source in reply["sources"]}
    invented = cited - supplied
    declined = "does not cover" in reply["answer"] or "not pretend to answer" in reply["answer"]

    ok = not invented
    ok = ok and reply.get("kind") == kind
    ok = ok and not (answerable and declined and kind == "grounded")
    ok = ok and (expected is None or expected in supplied)

    if not ok:
        failures.append(question)

    print(f"{'PASS' if ok else 'FAIL'}  {question[:46]:<46} {seconds:5.1f}s  {reply.get('kind')}")
    print(f"      {reply['answer'][:110]}")

print()
print("follow-ups with memory")
for question, expect in FOLLOW_UPS:
    reply, seconds = ask(question, PRIOR)
    supplied = [source["number"] for source in reply["sources"]]
    ok = reply.get("kind") in ("follow_up", "explicit_section") and expect[0] in supplied
    if not ok:
        failures.append(question)
    print(f"{'PASS' if ok else 'FAIL'}  {question[:46]:<46} {seconds:5.1f}s  {reply.get('kind')}")
    print(f"      {reply['answer'][:110]}")

total = len(CASES) + len(FOLLOW_UPS)
print()
print(f"{total - len(failures)}/{total} passed")
sys.exit(1 if failures else 0)

# Data Privacy Act of 2012 Assistant

A retrieval augmented chatbot that answers questions about Republic Act No. 10379, the Data Privacy Act of 2012 of the Philippines. Every answer is retrieved from the text of the Act and cites the sections it used. Questions the Act does not cover are declined rather than answered from memory.

## What it does

- Parses the official PDF of the Act into 45 numbered sections, each with its chapter heading.
- Embeds every section locally with `all-MiniLM-L6-v2` running on ONNX Runtime. No embedding API, no cost, no rate limit.
- Stores those 384 dimension vectors in a **Qdrant** collection over gRPC, one point per section, cosine distance, so search runs server side against a real vector database index rather than in process.
- Resolves questions through a router before searching: greetings, capability questions, explicit section references, conversational follow ups, out of scope questions, and general questions each take a different path.
- Answers in English or Tagalog, choosing from the language of the current question rather than the previous turn.
- Clamps the answer to the sentence count the question asked for, in English or Filipino.
- Scores a question with a hybrid of the Qdrant dense score and IDF weighted lexical overlap, then blends the two.
- Pins the section a question names, so `section 12` returns Section 12, and pins the definitions section for definitional questions so `what is X` reaches Section 3.
- Keeps the last four turns of context, so `expand that` resolves to the section under discussion.
- Sends only the retrieved sections to the LLM and instructs it to cite them and never invent a section number.
- Replaces any section number the model cites that was not actually supplied as evidence.
- Answers out of scope questions plainly, then redirects to sections the Act does cover.
- Returns the answer, the sections used, a relevance score, and the routing decision to a single page client.

## Source data

Two files under `data/` are committed on purpose.

`data/source/dpa_npc.pdf` is the official National Privacy Commission document. It is the provenance of every statement the app makes, and keeping it in the repository means the parsed corpus can be regenerated and audited against the original.

`data/dpa_articles.json` is that PDF parsed into 45 sections. It is derived data, committed so the corpus is deterministic and reviewable in a diff. Both are inputs. The Qdrant collection and `data/section_index.json` are outputs, rebuilt from these two inputs on startup, and are gitignored.

## Architecture

```
qdrant container (colima)      vector database, one point per section, cosine
        |
        |  384 dim embeddings, upsert once at startup
        v
data/source/dpa_npc.pdf        official PDF from the National Privacy Commission
        |
        |  tools/Dpa.Rag.Ingest
        v
data/dpa_articles.json         45 sections, parsed and committed
        |
        |  src/Dpa.Rag.Core, MiniLmEmbedder, ONNX Runtime
        v
data/section_index.json        cached vectors, built once on first run
        |
        |  src/Dpa.Rag.Core, ActIndex, QdrantVectorStore
        |    cosine distance from Qdrant blended with IDF weighted
        |    lexical overlap at 55 percent lexical, 45 percent dense
        v
  top 5 sections, one per chapter for a broad question
        |
        |  src/Dpa.Rag.Core, ActAnswerer
        |    system prompt forbids outside knowledge and invented sections
        v
  src/Dpa.Rag.Core, LlmClient
        |    qwen/qwen3.8-27b, then gpt-oss-120b, then z-ai/glm-5.3-flash,
        |    then qwen2.5:7b locally, each rung optional
        v
  src/Dpa.Rag.Api              POST /api/chat, GET /api/act, GET /api/health
        |
        v
  web                          React, Motion, Animate UI primitives, served from wwwroot
```

## Design notes

**C# and .NET 10** across the backend, `all-MiniLM-L6-v2` through ONNX Runtime for embeddings, Qdrant for storage and search, React with Motion for the client.

**Embeddings run locally.** Every embedding model on the NVIDIA NIM endpoint was end of life when this was built, so a local model removes the dependency, the cost and the rate limit. The 384 dimension vectors are computed once and cached, so the running system makes no embedding calls.

**Chunking is by section.** Section numbers are the citation key, so fixed token windows would cut a provision in half and make the citation meaningless.

**The model never sees the question alone.** It receives the question plus the retrieved sections, which makes citing something outside that evidence harder. Any section number it cites that was not supplied is stripped, and the interface labels a section as used only when the answer cites it.

## Corpus provenance

Source: National Privacy Commission, `data/source/dpa_npc.pdf`, downloaded manually from the official agency site.

The publisher's file name reads `Republic Act 10173`, which is a typo on their side. The correct act number is **10379**. The document content is correct and the parsed text matches the enacted law.

The parser strips the site's navigation chrome, repeated page headers, "Back To Top" links and the table of contents, then splits the body on `SEC. n.` and `SECTION n.` headings. It also removes the site footer that sits mid document, which otherwise swallows the signature block in Section 45, and collapses the repeated names that PDF text extraction leaves behind. All 45 sections are present in ascending order with no gaps, no chapter text leaking into section bodies, and no site furniture in the output.

## Running it

Two commands. See [RUNBOOK.md](RUNBOOK.md) for the full walkthrough.

```bash
make dev
```

Then open <http://127.0.0.1:5199>.

`make dev` starts Qdrant, loads `.env` if it exists, and runs the app. No API key
is required: the chain tries Groq, then NVIDIA, then the local model, and every
rung after the first is optional.

Put keys in `.env`, which is ignored by git and never print them:

```bash
make local     # start the local model, the fallback
make secrets   # show whether a cloud key is loaded, never prints it
make infra    # vector database only
make dev      # vector database and app, the demo path
make test     # verification, about six seconds
make check    # print what the vector database holds
make status   # which services are up
make stop     # stop everything
make help     # every target
```

## Verification

```bash
bash scripts/verify_all.sh
```

`verify_all.sh` runs the fast tier and finishes in about six seconds. It is the command to run live in a demo.

The suite is split in two on purpose. Logic tests never call a model, so they are instant and deterministic. Tests that drive a real LLM cannot be, and putting them in the same run makes the suite slow and quota dependent.

| Tier | What it covers | Cost |
| --- | --- | --- |
| `dotnet test` | fifty cases: routing, section pinning, bilingual detection, citation stripping, sentence clamping, parser cleanup | about one second |
| `stress_test.py` | fifty three cases against a live model, across legal accuracy, refusal, drift, prompt injection, malformed input, language routing, breadth, citation honesty, length and consistency | minutes |

`LLM_CHAIN=ollama` pins the integration tier to the local model so it costs nothing and returns identical results every run.

`verify_retrieval.py` asks ten questions and fails on three conditions: a cited section that was not supplied as evidence, an answerable question that was declined, or an unanswerable question that was not.

`verify_intents.py` covers twenty two routing cases: grounded legal questions, explicit section references, greetings, capability questions, out of scope questions, and follow ups that resolve against conversation memory.

`stress_test.py` runs fifty three cases across ten categories: grounded legal accuracy, out of scope refusal, drift resistance, adversarial prompt injection, malformed input, language routing, broad questions, citation honesty, length instructions, and five identical repeats of the same question. It fails on a degenerate repetition loop in any answer, which is the failure mode a small local model falls into, and on any chip that names a section the answer did not cite.

## Layout

```
src/Dpa.Rag.Core      parser, embedder, index, answerer, LLM client
src/Dpa.Rag.Api       HTTP layer and static hosting
tools/Dpa.Rag.Ingest  PDF to sections
tests/Dpa.Rag.Tests   parser, router and grounding unit tests
web                   client
models                MiniLM ONNX weights and vocabulary, 86 MB
data                  source PDF, parsed corpus, cached vectors
scripts               verification harness
```

## Known limits

- Free hosted models are the fast path at roughly half a second, and they are rate limited per day. The local model answers in five to fifteen seconds with no limit and no cost, which is why it sits last in the chain rather than first.
- The relevance floor and the blend weights are tuned against this harness, not a larger benchmark.
- No persistence. The transcript lives in the browser and is lost on refresh, though the last four turns are carried as context while the page stays open.
- No rate limiting on the API. It is a local demonstration surface.
- The Act text is the 2012 law only. The Implementing Rules and later amendments are not included.

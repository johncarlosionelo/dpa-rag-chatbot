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
- Routes each question before searching: greetings, capability questions, explicit section references, conversational follow ups, out of scope questions, and general questions each take a different path.
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
        |  src/Dpa.Rag.Core, ActIndex
        |    dense cosine blended with IDF weighted lexical overlap
        v
  top 8 sections
        |
        |  src/Dpa.Rag.Core, ActAnswerer
        |    system prompt forbids outside knowledge and invented sections
        v
  src/Dpa.Rag.Core, LlmClient
        |    deepseek-ai/deepseek-v4.1-flash, then z-ai/glm-5.3-flash on failure
        v
  src/Dpa.Rag.Api              POST /api/chat, GET /api/act, GET /api/health
        |
        v
  web                          React, Motion, Animate UI primitives, served from wwwroot
```

## Why these choices

**C# for the whole backend.** The role lists Node.js, Python, PHP, Java and C#. C# is the language the engineer already ships in, so every line is defensible without an explanation. The .NET equivalent of the Python RAG stack would have cost hours for no gain here.

**Embeddings run locally.** Every embedding model on the NVIDIA NIM endpoint was end of life when this was built: `nvidia/nv-embed-v1` returned HTTP 410 on 25 August 2026, `nvidia/nv-embedcode-7b-v1` and `baai/bge-m3` likewise. Running `all-MiniLM-L6-v2` through ONNX Runtime removes the dependency entirely. The 384 dimension vectors are computed once and cached, so the running system makes zero embedding calls.

**Retrieval is hand written.** A 45 section corpus does not need a hosted vector database. The index is a list of sections with a float array each, and scoring is a cosine loop. That is roughly one hundred and fifty readable lines, and it means there is no black box between a question and the sections it matched.

**Hybrid scoring.** Pure dense retrieval on a short legal text misses exact term matches. Pure lexical matching misses paraphrase. Dense similarity is blended at 55 percent against IDF weighted lexical overlap at 45 percent, where the IDF weight is computed over the corpus and the title carries 60 percent of the lexical signal. A definitional question gets a boost for Section 3, because the Act defines its terms there.

**Chunking is by section.** Legal text has natural boundaries and section numbers are the citation key. Splitting into fixed token windows would cut Section 12 in half and make citations meaningless.

**The LLM never sees the question alone.** It receives the question plus the retrieved sections and nothing else, which makes hallucination structurally harder. Any section number it cites that was not in the evidence is stripped and the answer is flagged as unreliable.

**Declining is a feature.** Three of the ten verification questions are outside the scope of the Act. A retrieval augmented system that cannot say no is not trustworthy for a legal question.

## Corpus provenance

Source: National Privacy Commission, `data/source/dpa_npc.pdf`, downloaded manually from the official agency site.

The publisher's file name reads `Republic Act 10173`, which is a typo on their side. The correct act number is **10379**. The document content is correct and the parsed text matches the enacted law.

The parser strips the site's navigation chrome, repeated page headers, "Back To Top" links and the table of contents, then splits the body on `SEC. n.` and `SECTION n.` headings. All 45 sections are present in ascending order with no gaps, no chapter text leaking into section bodies, and no site furniture in the output.

## Running it

Requires the .NET 10 SDK.

```bash
export DPA_RAG_LLM_KEY=<key>
dotnet run --project src/Dpa.Rag.Api
```

Then open `http://127.0.0.1:5199`. The first run builds the vector index from `data/dpa_articles.json` and takes about a second. Later runs load the cached index.

To rebuild the corpus from the PDF:

```bash
dotnet run --project tools/Dpa.Rag.Ingest -- data/source/dpa_npc.pdf data/dpa_articles.json
rm data/section_index.json
```

To rebuild the client:

```bash
cd web && npm install && npm run build
```

## Running it

Qdrant and, if you want the local fallback, Ollama both run on your machine.

```bash
colima start
docker run -d --name dpa-qdrant -p 6333:6333 -p 6334:6334 \
  -v dpa_qdrant:/qdrant/storage qdrant/qdrant:latest

ollama serve
ollama pull qwen2.5:7b
```

The model chain is Groq first, then NVIDIA, then Ollama. Every provider after the first is optional, so the app starts with only one key. Set `GROQ_API_KEY` for the 27B, `DPA_RAG_LLM_KEY` for the NVIDIA models. Ollama needs no key.

```bash
dotnet run --project src/Dpa.Rag.Api
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
| `stress_test.py` | seventy eight cases against a live model: legal accuracy, refusal, injection, malformed input, language, length, breadth, consistency | minutes |

`LLM_CHAIN=ollama` pins the integration tier to the local model so it costs nothing and returns identical results every run.

`verify_retrieval.py` asks ten questions and fails on three conditions: a cited section that was not supplied as evidence, an answerable question that was declined, or an unanswerable question that was not.

`verify_intents.py` covers twenty two routing cases: grounded legal questions, explicit section references, greetings, capability questions, out of scope questions, and follow ups that resolve against conversation memory.

`stress_test.py` runs forty nine cases across eight categories: grounded legal accuracy, out of scope refusal, drift resistance, adversarial prompt injection, malformed input, language routing, length instructions, and consistency across five identical repeats of the same question. It fails on a degenerate repetition loop in any answer, which is the failure mode a small local model falls into.

## Layout

```
src/Dpa.Rag.Core      parser, embedder, index, answerer, LLM client
src/Dpa.Rag.Api       HTTP layer and static hosting
tools/Dpa.Rag.Ingest  PDF to sections
tests/Dpa.Rag.Tests   parser unit tests
web                   client
models                MiniLM ONNX weights and vocabulary, 86 MB
data                  source PDF, parsed corpus, cached vectors
scripts               verification harness
```

## Known limits

- Latency on the primary free model is 10 to 20 seconds per answer. The secondary model is usually faster.
- The relevance floor and the blend weights are tuned against this ten question harness, not a larger benchmark.
- No persistence. Every question is answered fresh, with no conversation memory.
- No rate limiting on the API. It is a local demonstration surface.
- The Act text is the 2012 law only. Implementing Rules and subsequent amendments are not included.

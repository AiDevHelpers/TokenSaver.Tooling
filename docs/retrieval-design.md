# Retrieval Design: Conditional Rerank + Abstain Path

> Status: design, 2026-07-07. Derived from `retrieval-benchmark/RESULTS.md` (74-query stratified
> sample, GTX 1660). Applies to the embed→rerank retrieval used by `WorkspaceIndexService` /
> `BuildConciseContext` / `CodeIndex`, and cross-referenced by the SimpleModelServer pipeline plan
> (`SimpleModelServer/docs/staged-pipeline-plan.md`).

## What the benchmark established

- Embed-only retrieval is fast (~53ms/query) and saturated on lexically-anchored queries
  (symbol_lookup nDCG 0.954, doc_derived 0.933) — a reranker adds nothing there.
- The reranker earns its cost only on adversarial (+0.05) and conceptual (+0.04) queries, and
  costs 6.6s/query (CUDA) / 11.6s (CPU) at K=12 — disqualifying for any interactive path if run
  unconditionally.
- 20% of "doesn't exist" queries returned a confident (>0.5) top hit even after reranking —
  retrieval must be able to say "no match."
- CUDA's rerank advantage is only 1.7x (no tensor cores on the 1660), and the reranker takes
  1.88GB VRAM that the SimpleModelServer codegen pair wants.

## Design

### 1. Placement

| Stage | Device | Rationale |
|---|---|---|
| Embedder (Qwen3-Embedding-0.6B) | CPU (or GPU if free) | ~53ms/query either way at this scale |
| Reranker (Qwen3-Reranker-0.6B) | **CPU, always** | Off the interactive path (below); 1.7x penalty acceptable; frees ~1.9GB VRAM for the codegen pair |

GPU stays reserved for SimpleModelServer codegen.

### 2. Conditional reranking

Rerank is skipped by default and triggered only when embedding scores signal a hard query:

```
results = embed_topK(query, K=12)
margin  = score[0] - score[1]
if score[0] >= HIGH_CONF and margin >= CLEAR_MARGIN:
    return results          # clear winner — symbol_lookup/doc_derived profile (~85% of traffic)
elif score[0] < FLOOR:
    return NO_MATCH          # nothing plausible — don't rerank noise (see §3)
else:
    return rerank(results, K=RERANK_K)   # muddy middle — adversarial/conceptual profile
```

- Initial thresholds to calibrate on the benchmark set: `HIGH_CONF`, `CLEAR_MARGIN`, `FLOOR`
  chosen so ≥95% of symbol_lookup/doc_derived skip rerank and ≥90% of adversarial/conceptual
  trigger it. The 332-query labeled set is the calibration data — no new labeling needed.
- `RERANK_K`: benchmark at K=4 and K=8 vs the measured K=12. Latency is roughly linear in K;
  if K=4 preserves the adversarial discrimination rate (100% at K=12), expected rerank cost drops
  to ~2–4s CPU, and it only applies to the hard minority of queries.
- Log per query: margin, whether rerank fired, pre/post top-1 — this is the tuning telemetry.

### 3. Abstain path (the 20% problem)

Retrieval tools must return an explicit no-match rather than their best guess:

- After rerank (or embed-only), if top probability < `ABSTAIN_THRESHOLD`, return
  `{ "match": "none", "best_candidate": …, "confidence": … }` instead of a hit.
- Calibrate `ABSTAIN_THRESHOLD` on the 15 negative + 25 conceptual queries: pick the threshold
  minimizing (false positives on negatives) + (false abstains on real queries). Current data:
  3/15 negatives score >0.5 → the threshold likely lands above 0.5; verify against the full
  332-query run before fixing it.
- **Contract change:** `CodeIndex` / `BuildConciseContext` MCP tool responses gain a
  `no_match: true` shape. Downstream (context building for codegen) treats no-match as "omit this
  context," never "include the best guess" — a confidently wrong chunk is hallucination fuel for
  the code model.

### 4. Where reranking is always fine: background indexing

Batch/background jobs (AI summaries, index enrichment, staleness re-checks) have no latency
budget — they may rerank unconditionally at K=12 on CPU, throttled by the existing
`SimpleModelServerCapacityClient` concurrency logic so they never contend with interactive work.

## Follow-ups (ordered)

1. **Full 332-query run** — done, but against production (`nomic-embed-text` via Ollama, no
   rerank), not the candidate Qwen3 pair this doc was written against. See
   `docs/context-builder-evaluation.md` for the runnable script (`run_eval.py`) and current
   baseline: 91.1% doc_derived+symbol_lookup recall@5, but **100% negative-query false-positive
   rate** — worse than the 20% this doc's §3 was scoped against, since production has no rerank
   and no abstain threshold yet. The CPU-vs-CUDA tie-break question below is still open on the
   candidate pair specifically; re-run `run_benchmark.py` full-332 if that comparison still
   matters for the model-upgrade decision.
2. **Conditional rerank — implemented 2026-07-10**, but as an **LLM-judged rerank via the
   existing "rerank" model route** (`RerankService`/`ConditionalRetrievalService` in
   `TokenSaver.Library`), not the benchmarked Qwen3-Reranker-0.6B cross-encoder. That model needs
   its own inference runtime (llama.cpp/transformers) with no existing integration point in this
   codebase — TokenSaver only ever reaches models through Ollama/SimpleModelServer's HTTP API (see
   CLAUDE.md). Instead, the muddy-middle candidate pool gets one chat-model call asking it to
   score each candidate 0.0-1.0; response is parsed and re-sorted, degrading to embed-only ranking
   on any call/parse failure. `HIGH_CONF`/`CLEAR_MARGIN` calibrated at `0.79`/`0.05`
   (`TokenEconomyConfig.SemanticSearchHighConfidence`/`SemanticSearchClearMargin`) — favors never
   missing a hard (adversarial/conceptual) query over minimizing rerank latency on easy ones (see
   that config's doc comment for the full sweep). The K=4/K=8/K=12 candidate-pool sweep from this
   doc's original write-up was scoped to the cross-encoder pair's per-candidate cost and doesn't
   transfer to this approach's cost driver — here it's *reliability* (does the model return a
   correctly-counted JSON array), not latency.

   **Live-measured 2026-07-10** against the full 332-query golden set with "rerank" repointed at
   `qwen2.5-coder:3b` (`docs/retrieval-benchmark/rerank_eval.py`, results in `RERANK-EVAL.md`) at
   two pool sizes:

   | Pool size | Muddy-middle queries | Rerank fired | Negative-FP (0.66 floor) | doc/symbol recall@5 |
   |---|---|---|---|---|
   | (embed-only baseline) | — | — | 100% (15/15) | 91.1% |
   | (abstain-only, no rerank) | — | — | 40% (6/15) | 91.1% |
   | 12 | 97 | 37% (36/97) | 33.3% (5/15) | 90.0% |
   | **6 (current default)** | 97 | **92% (89/97)** | **20.0% (3/15)** | 90.7% |

   At pool size 12, a 3b model asked to score all 12 candidates unreliably drops or adds an entry
   to the JSON array, so 63% of muddy-middle queries silently fell back to embed-only ranking
   (correct behavior, just a missed opportunity). Shrinking the prompt to 6 candidates raised the
   parseable-response rate to 92% and, as a direct result, dropped negative-FP to 20% — hitting
   this doc's original §3 ceiling target — with recall held flat. `SemanticSearchRerankCandidatePoolSize`
   defaults to `6` as of this measurement. Re-run the sweep before changing it further, or if the
   rerank model changes (a more capable model may tolerate a larger pool without losing
   reliability).
3. **Threshold calibration** — done for the *narrow* half
   (`TokenSaver.ContextHelper.Calibration.RetrievalCalibrationService` self-tunes
   `SemanticThreshold` downward from live "caller had to re-request a file" signal — see
   `docs/context-builder-evaluation.md`) and now also for the *wide* half via the conditional-rerank
   implementation above, at the `SemanticGrep` MCP tool layer specifically (not yet
   `WorkspaceIndexService`/`BuildConciseContext`/`CodeIndex`).
4. **Abstain shape in the MCP tool contract** — done for `SemanticGrep`: a top hit scoring below
   `TokenEconomyConfig.SemanticGrepAbstainThreshold` (0.66, calibrated 2026-07-10 against the
   332-query set — sweep in `docs/token-economy-tools-contract.md`'s SemanticGrep section) now
   returns `{"match":"none", best_candidate, confidence}` instead of a confident hit, checked both
   before and after the conditional-rerank pass above. Abstain-only (no rerank) got negative-FP
   from 100% down to 40% (6/15) at ~2% cost to correct-hit recall (5/246); the rerank pass is meant
   to close more of that remaining gap on the queries it fires for, but end-to-end negative-FP
   with rerank live hasn't been measured against the full golden set yet (would need extending
   `run_eval.py` to make a real chat-model call per muddy-middle query — not done, and blocked on
   the model-availability gap in #2 above). `BuildConciseContext`/`CodeIndex` don't have an
   equivalent abstain shape yet — `06_SemanticRelevanceFilterStage` already drops individual
   low-relevance chunks per-threshold, but there's no query-level no-match signal at that surface.
5. **Incremental corpus embedding**: at 4.5 chunks/s a full re-index is ~100s per 442 chunks —
   fine today; wire embedding into the existing git-staleness incremental path
   (`GitStalenessChecker`) before the corpus grows 10x. Not done.
6. **Embedding-kNN intent router experiment** (SimpleModelServer side): reuse this embedder to classify
   SimpleModelServer router intents via nearest labeled examples (~53ms vs the 1.5B's 2.7s p50). Test:
   the 20 A4 router prompts against a labeled example set; success bar = match the 1.5B's 80%
   informal accuracy at <200ms. Tracked in `SimpleModelServer/docs/staged-pipeline-plan.md`.

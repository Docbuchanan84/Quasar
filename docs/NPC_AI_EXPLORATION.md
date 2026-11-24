# Strategic NPC AI — design exploration

**Discussion date:** 23 November 2026  
**Status:** Exploratory; no model, provider, runtime, architecture or implementation is approved.  
**Authority:** The [Quasar Game Design Record](SOURCES.md#owners-shared-design-record) owns design decisions. This page preserves the options and reasons discussed, not a shipping specification.

## Why this is under consideration

Quasar needs believable decisions by many kinds of actors across a 4X simulation, including governments, commanders, firms and individual characters. The owner asked whether models running on an iPhone might be capable of strategic decisions or NPC portrayal, and how a function-calling model differs from a decision/scoring model. The practical question is **which choices benefit from learned inference and which should remain conventional game AI**.

The simulation remains authoritative in C#: it tracks facts, resources, orders, timing and permitted transitions. An NPC AI is a client of this state and action system, not an independent editor of canonical world state.

## Candidate division of responsibilities — unapproved

| Layer | Possible role | Important caveat |
| --- | --- | --- |
| Deterministic/utility/goal AI | Frequent tactics, resource management, scheduling and routine priorities | Gives a measurable, explainable baseline; may need careful rules and tuning |
| Decision/scoring model | Rank a bounded set of legal intentions given current actor facts and goals | A scored selection is not a full planner; ensure the model sees only facts that actor knows |
| Small general LLM | Occasional strategic reasoning, high-level plans, diplomacy, personality-rich NPC dialogue and unusual contexts | Reliability, latency, consistency, memory cost and offline operation untested |
| Function-call model, e.g. FunctionGemma | Map approved intentions or natural-language orders into typed function arguments | Translation is not the same as strategic decision-making; C# must validate every call |
| Action validator/executor in C# | Enforce legal targets, costs, timing, authority and persistence | Always required regardless of inference approach |

A model pipeline *might* be: simulation snapshot plus actor knowledge → choose/scored intention → optional natural-language interpretation → validated structured order → deterministic simulation execution → recorded outcome. Not every choice requires every stage. In particular, direct code can often emit valid structured actions without adding a function-call LLM.

**FunctionGemma versus a decision model:** A function-call specialist can choose/format tool invocations based on natural language; a decision model scores alternatives according to its learned or supplied selection criteria. Neither alone guarantees valid multi-step grand strategy. A general-purpose LLM is yet another option, with broader language/world knowledge but different resource and reliability tradeoffs.

## On-device iPhone model candidates — not chosen

Small Qwen variants, Apple Foundation Models, small Gemma variants and FunctionGemma were discussed as candidate technologies. No Quasar-specific benchmark establishes that any can deliver the desired strategic reasoning quality, sustained speed, RAM footprint, or battery endurance on the target iPhone. No named model size, token rate, provider price or API availability should be treated as a verified requirement. Cloud services and external decision APIs were also part of the conceptual comparison; concerns about cloud dependence were expressed, but the acceptable network boundary has **not** been formally set.

An important unresolved distinction is *where the authoritative world is running*. The playable prototype runs its C# simulation on a host and can continue through browser disconnection. If strategic NPC inference depends on a phone-side model, it could stall, disappear, or behave differently when the phone locks. Options include host-side local inference, deterministic fallback, deliberate paused-only player assistance, asynchronous intent submission, or a different future deployment. None is selected.

## Open design questions and reasons

1. **Scope and cadence:** Which actor classes and decision horizons warrant expensive reasoning? Thousands of routine choices cannot assume a language-model call each tick.
2. **Information boundaries:** NPCs should use their own knowledge, doctrine, relationships and memory, not unrestricted omniscient access to the C# world.
3. **Legality and reproducibility:** How will constraints, serialization, model-version changes, seeds, retries and audit trails keep outcomes coherent through saves and long advances?
4. **Value versus cost:** Does inference create better strategies or storytelling than a small conventional baseline, and at what latency/energy/maintenance cost?
5. **Connection/deployment:** Can AI function while the phone is disconnected and Continuous advances on the host? Where is memory stored and can the result be replayed?
6. **Player controls:** What delegation, override, explanation, personalities and autonomy levels actually make NPC AI worthwhile?

## Possible evaluation slice — proposal only

Build a single scenario with a small actor, concrete goals, known facts, legal orders and deterministic scoring. Compare (a) utility AI, (b) a bounded decision model and (c) one local small language model. Record legal-action rate, strategic outcomes, latency, memory and battery, and how consistently an actor behaves with unchanged inputs. Retain the action validator for every approach. Add realistic long-advance and disconnect tests before selecting device-dependent inference.

**Not decided:** actual on-device LLM adoption, FunctionGemma integration, Perplexity or any other decision API, AI deployment location, NPC memory format, model choice, architecture, scheduling or engineering work. No implementation is authorized by this exploration alone.

## Related documents

- [Decision record](DECISIONS.md) — approvals and unresolved options
- [Implementation plan](PROJECT_PLAN.md) — current shipped work versus future investigation
- [System Lab](SYSTEM_LAB.md) — authoritative C# time and command behavior
- [Source roles](SOURCES.md) — live owner record versus repository documentation

# Round 209 scoped restart review

Build `KookerStarfallIntegrated-0.0.11-survival.1-20260915-111220`; full-content SHA256 `2b3e9da88225315bfa5c98fa517b6cfa836d51686d822e7d78054891b1e99939`.

| Claim | Status | Evidence | Remaining gap |
|---|---|---|---|
| Normal, non-diagnostic launch | Verified process observation | `evidence/local/normal-survival/run-17/process.json`: normal flags true, smoke/probe/death diagnostics false | Process flags alone do not prove behavior |
| Scoped body and knowledge restored | Verified in runtime rows | Run16 final and run17 startup share foodTick438, energy9243, hydration6138, berry/meal knowledge true; startup `earned-survival-authority-reloaded` | Not full world/object saving or migration of older marker-less stores |
| Fresh model choice after reload | Verified | Run17 request sequence1, `explore north`, admitted tick145, exact local instance `starfall-local-e4b`, finish `stop`, 2357ms under survival budget | This is not the separate 1500ms delivery-reflection budget |
| Choice actually executed | Verified | Same sequence1 reaches north at tick323/foodTick445, from (62.171,0.059,-86) to (62.000,-0.037,-80.171) | Human visual review remains separate |
| New post-reload food transaction | Not yet proven | Last persisted food request remains15 | Need successful transaction with increasing ID; exploration does not prove this |
| Death, return and grounded lesson | Open | Not demonstrated by this normal restart run | Separate labelled diagnostic and retained cause-only lesson evidence required |
| Freshwater discovery/drinking | Open | Spring knowledge false in this run | Do not force thirst or remove genuine berry hydration to obtain coverage |

Raw action evidence: `evidence/local/normal-survival/run-17/survival-evidence/normal-survival.jsonl`. First request SHA256 `fe73e8c8161b4c759408bd38c4d9ae5426df9683b79bed5c9ea901d0c7cd02c0`; response SHA256 `2f9903004434638096235f807452389a083b8611456eee0a1557f13e92af1c35`.

The worker reports launcher session14728 exited0 and all184 build files retained the same hash. Retain its terminal receipt separately; startup `process.json` does not itself contain exit/duration/post-hash evidence. The narrow restart action result does not promote the full milestone, bypass the survival promotion verifier, or accept the panorama visuals.

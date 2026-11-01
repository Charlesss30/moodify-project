# Bilingual AI regression cases

These are authored diagnostic examples used while fixing the application mapper. They are not blind independent gold labels, training data, or model-quality approval evidence.

- `bilingual-flow-cases.json`: 134 additional Vietnamese/English cases. The runner combines them with 40 baseline examples for 174 base live-model cases.
- `mood`: example expectation; null means no fixed categorical mood expectation.
- `expectAbstain`: true means the application should ask for clarification/manual choice instead of accepting an inferred mood.
- `category`: the behavior being exercised; includes negation, temporal scope, content goals, other people, quotes, mixed affect, hypotheticals, sarcasm markers, content attribution, reactions, formatting, polysemy, shorthand and code switching.

Run the integration executable with `--bilingual-full-audit` after an isolated build and local AI startup. Receipts, recommendation sessions and history are created only in the temporary database.

Exit code 2 can mean a labeled example was rejected for low confidence; inspect the report's reason codes, guarded failures and accepted wrong moods separately. Do not loosen thresholds to make this file pass.

See `docs/MOBILE_AI_HARDENING_2026-10-31.md` for commands, scope and remaining limits.

The v4 runner records the original full-text prediction separately when input focusing applies, then verifies gateway raw coordinates against the model's actual selected input. The production gateway makes one model call; the extra full-text comparison exists only in this diagnostic.

V5 adds `mobile-ai-edge-cases.json` (82 additional bilingual ownership/time/content/negation/non-mood cases), bringing the full runner to 256 cases. `language-routing-cases.json` contains 10 routing fixtures shared with Flutter. Context expectations and real-model agreement are checked separately; unknown output is not automatically a transport failure. Run `--mood-edge-contract` for the context/routing fixtures.

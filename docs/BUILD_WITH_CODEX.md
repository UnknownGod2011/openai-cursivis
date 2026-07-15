# Build with Codex

This file records verified Codex-assisted milestones. It does not invent session IDs, model claims, tests, or remote state.

## Phase 0 - Audit and architecture

Completed with Codex on 2026-07-15.

Work:

- Read the complete 3,680-line product brief.
- Audited `UnknownGod2011/MX--Cursivis` at `08f294ff5643a67bd64a5064a572a1408c9ec3af`.
- Audited `UnknownGod2011/keyboard.wtf` at `946a156e132a04ef4353152fd1ed2f744c3c1b91`.
- Identified fixed-delay selection, unauthenticated localhost control, silent form submission, model-trusted risk, duplicate hotkey ownership, raw voice retention, Settings architecture, and startup/installer defects.
- Created the PRD, architecture, rebuilt Settings design, action safety policy, and phase plan.
- Verified the requested GPT-5.6 and GPT-Realtime-2.1 model IDs against current official OpenAI documentation.
- Diagnosed the local WinUI environment: default framework-dependent runtime failed registration; latest self-contained 2.2 path crashed in `Microsoft.UI.Xaml.dll`; pinned Windows App SDK 1.8 self-contained unpackaged path built and displayed a real responsive top-level window.

Validation:

- `.env` ignored and untracked.
- Filename-only secret scan passed.
- `git diff --cached --check` passed before the final commit.
- Local and remote branch SHA matched after push.

Milestone:

- Commit: `3a813fdd9584900809f6e229a811b50889504b74`
- Branch: `codex/build-cursivis-next`
- Remote: `https://github.com/UnknownGod2011/openai-cursivis`
- Draft PR: `https://github.com/UnknownGod2011/openai-cursivis/pull/1`

## Codex task reference

The work was performed in the current Codex desktop task. No durable Codex session ID was surfaced to the repository-writing workflow, so none is recorded here. The user should add the real task/session reference from the Codex UI when preparing final hackathon evidence.

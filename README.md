# Cursivis Next

Cursivis Next is a Windows-first, cursor-first productivity layer built for OpenAI Build Week.

> Selection = Context. Trigger = Intent. Cursivis = Action.

The product is being built from scratch in this repository. The earlier MX-Cursivis and keyboard.wtf repositories were inspected only as read-only behavioral references; no Logitech code, Gemini integration, local-LLM provider, or legacy Settings UI is being carried forward.

## Current status

Phase 0 audit and architecture are complete in the working tree. Product implementation begins only after this foundation is committed and pushed. No runtime capability is claimed complete yet.

The launch-proven Windows baseline is:

- C# and .NET 8;
- WinUI 3;
- Windows App SDK 1.8, pinned;
- self-contained, unpackaged x64 deployment;
- per-user installer;
- OpenAI Responses, Realtime, vision, and transcription APIs behind typed gateways.

This baseline was selected after a clean WinUI probe built successfully and produced a real responsive top-level window on the development machine. The newest alpha template/runtime combination was rejected after it crashed during startup.

## Product flows

- Context Trigger: selected text, region capture, and local click-to-color.
- Realtime Live Mode: low-latency voice with interruption and explicit tools.
- Take Action: typed proposals, deterministic execution, verification, and undo where possible.
- Smart Dictation: partial transcription, cleanup, insertion, and undo.
- Custom Quick Task: one persistent workflow, defaulting to Prompt Optimizer.

## Documentation

- [Reference audit](docs/reference-audit.md)
- [Product requirements](docs/product-requirements.md)
- [Architecture](docs/architecture.md)
- [Settings design](docs/settings-design.md)
- [Action safety policy](docs/action-safety-policy.md)
- [Implementation plan](docs/implementation-plan.md)

## Development secrets

The root `.env` is a local, ignored development input. It is used only by explicitly enabled live tests after mocked tests pass. The installed application stores its OpenAI API key with Windows current-user encryption and never depends on `.env`.

Never commit, log, screenshot, print, or place the key in command-line arguments. Copy `.env.example` only when setting up a new local checkout.

## Canonical repository

https://github.com/UnknownGod2011/openai-cursivis

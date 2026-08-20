# CodexCapper: OpenAI Codex & ChatGPT CLI Usage Tracker and Capper

Tracks and caps OpenAI Codex, ChatGPT CLI, and reasoning model usage before plan limits or API budgets run out.

## Features
- Proactive Interception: Blocks CLI agents and automation tools when usage reaches your threshold.
- System Tray Background Persistence: Keeps watch in the Windows notification area when minimized or closed.
- Model Breakdown: Live tracking for o1/o3 reasoning limits, GPT-4o rate limits, and monthly budgets.
- Daily Budget Pacing: Distributes monthly credit usage across the billing cycle.
- Zero-Dependency Native Binary: CodexCapper.exe runs out of the box on Windows.

## Setup
Point your CLI hook or wrapper to check-usage.ps1 and launch CodexCapper.exe.

## License
Licensed under Apache License 2.0. Copyright 2026 [Yasir Mo](https://github.com/yasir-mo).

---
description: "Workspace policy: prioritize Copilot Terminal Tools with named terminals and route command execution through reusable terminal sessions."
applyTo: "**"
---

Use the skill at .github/skills/copilot-terminal-tools/SKILL.md as the default workflow for command execution.

Policy:
- Prefer `terminal-tools_sendCommand` for command execution.
- Reuse named terminals and keep process continuity.
- Avoid disposable terminals and random terminal names.
- Capture output only when needed for diagnostics or decision making.
- Keep long-running processes in dedicated terminals such as `dev-server`.

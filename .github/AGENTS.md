# AGENTS

This repository uses a terminal orchestration policy for all coding agents.

## Terminal Execution Policy
- Prefer named terminal tools over spawning disposable terminals.
- Use `terminal-tools_sendCommand` as the default command execution path.
- Reuse workflow-specific terminal names (for example: `dev-server`, `build`, `test`, `package-manager`, `git`, `general`).
- Use `terminal-tools_executeCommandWithOutput` only when command output must be captured for reasoning.
- Use `terminal-tools_cancelCommand` to interrupt stuck or long-running commands.
- Use `terminal-tools_deleteTerminal` only for explicit cleanup.

## Skill Preference
- Prefer the workspace skill at `.github/skills/copilot-terminal-tools/SKILL.md` for command-routing decisions.

## Goal
- Keep terminal usage clean, predictable, and continuous across agent interactions.

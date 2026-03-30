---
name: copilot-terminal-tools
description: "Use when running shell commands with terminal reuse, named terminals, and command routing to avoid terminal spam. Keywords: terminal chaos, named terminal, sendCommand, executeCommandWithOutput, listTerminals, cancel command, reusable terminal workflow."
---

# Copilot Terminal Tools

## Mission
- Execute commands in stable, named terminals instead of spawning disposable terminals.
- Preserve process continuity for dev servers, watches, and long-running workflows.
- Keep terminal usage organized, predictable, and easy to debug.

## Tool Priority
1. `listTerminals`
2. `sendCommand` (primary execution path, auto-creates terminal if needed)
3. `executeCommandWithOutput` (only when output must be captured for reasoning)
4. `cancelCommand` (interrupt stuck/long-running foreground commands)
5. `deleteTerminal` (explicit cleanup only)

## Core Rules
- Reuse an existing named terminal before creating a new one.
- Use one terminal per workflow type; do not create throwaway terminals for every command.
- Keep long-running processes in dedicated terminals (for example `dev-server`).
- Capture output only when needed for diagnostics or decision making.
- Do not delete active terminals unless explicitly asked.

## Recommended Terminal Names
- `dev-server`: development servers and watch mode
- `build`: build and packaging operations
- `test`: automated tests and watch-tests
- `package-manager`: dependency install/update/remove
- `git`: version control tasks
- `docker`: container commands
- `database`: migrations and database CLI
- `cloud`: cloud provider CLIs
- `general`: file operations and one-off utilities
- `scripts`: project automation scripts

## Command Routing Playbook
1. Dependency install/update
   - terminal: `package-manager`
   - command examples: `npm install`, `pnpm add`, `pip install -r requirements.txt`
2. Start development server
   - terminal: `dev-server`
   - command examples: `npm run dev`, `dotnet watch`, `python manage.py runserver`
3. Build
   - terminal: `build`
   - command examples: `npm run build`, `cargo build`, `dotnet build`
4. Tests
   - terminal: `test`
   - command examples: `npm test`, `pytest`, `cargo test`
5. Version control
   - terminal: `git`
   - command examples: `git status`, `git add -A`, `git commit -m "..."`

## Output Strategy
- Prefer `sendCommand` for routine execution where persistent context matters.
- Use `executeCommandWithOutput` when command output must be parsed or quoted back.
- Summarize only high-signal lines (errors, warnings, key success lines) rather than full logs.

## Recovery and Cancellation
- If a command blocks unexpectedly, use `cancelCommand` before retrying.
- Retry in the same terminal unless context is corrupted.
- Move to a different named terminal only when workflow boundaries change.

## Boundaries
- This skill defines terminal orchestration behavior only.
- It does not replace project-specific build/test commands.
- It does not enforce one shell implementation.

## Quick Execution Template
1. Check existing terminals with `listTerminals`.
2. Pick or create a recommended terminal name.
3. Run command via `sendCommand`.
4. If machine-readable output is needed, use `executeCommandWithOutput`.
5. Use `cancelCommand` for stuck runs.
6. Keep terminal for reuse; delete only when explicitly cleaning up.

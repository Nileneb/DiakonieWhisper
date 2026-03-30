# Portable Skill Template: Copilot Terminal Tools

Use this folder to add the same skill to other repositories in under one minute.

## Install Into Another Workspace
1. Create target folder in the destination repo:
   - `.github/skills/copilot-terminal-tools/`
2. Copy `SKILL.md` from this template into that folder.
3. Reload VS Code window or restart Copilot Chat session.

## Quick Verification
1. Ask Copilot to run a command while reusing a named terminal.
2. Verify it plans around terminal reuse (`listTerminals` then `sendCommand`).
3. Verify it avoids creating disposable terminals for each command.

## Optional Customization
- Add or remove terminal names to match your team workflow.
- Add project-specific routing examples for your stack.
- Keep trigger keywords in frontmatter description so the skill stays discoverable.

# Bakabase agent instructions

Before working in this repository, read and follow the shared
[project instructions](.claude/CLAUDE.md). Claude and Codex use that same source
for architecture, development, commit messages and GitHub issue management.

Also read the applicable files in [the shared rules directory](.claude/rules/).
Match each rule's `paths` front matter against repository-relative paths to
determine its scope. Rules without a `paths` filter apply whenever their subject
is relevant.

Keep policy changes in `.claude/CLAUDE.md` or `.claude/rules/`; this file is only
the Codex entry point. Do not copy the rules into a separate Codex policy tree.

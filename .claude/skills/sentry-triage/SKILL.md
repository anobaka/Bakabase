---
name: sentry-triage
description: Pull unresolved issues from Sentry (bakabase.sentry.io) for the bakabase-web and bakabase-service projects, triage each, reuse or create concise Chinese GitHub issues with labels, fix them on a feature branch, and open a PR that auto-closes those issues. Use when the user asks to process, triage, or fix Sentry issues / production errors.
---

# Sentry Issue Triage Workflow

End-to-end batch workflow: **Sentry unresolved issues → analysis → Chinese
GitHub issues → fixes → PR**.

This skill is the **authoritative process** for batch-handling Sentry issues. It
intentionally **supersedes** the generic per-issue "自动化 Issue 处理规范"
4-step flow in `.claude/CLAUDE.md`: here you proceed automatically for issues
that are fixable and small/medium scope, and only pause to ask the user about
issues that are **unfixable** or **very large in scope**.

## Tooling — do not assume any specific CLI

This workflow may run locally or in a cloud agent. Each step states an *intent*;
achieve it with whatever the environment provides:

- **HTTP** (Sentry API) — `curl`, or any built-in fetch capability.
- **GitHub** (issues, PRs) — the `gh` CLI if installed; otherwise the GitHub
  REST API or the authorized GitHub integration available to the agent.
- **git** (branch, commit, push) — always available; it is a git repository.

Repository: `anobaka/Bakabase`. Default branch: `main`.

## Language

- **GitHub issues** — title and body in **Chinese**, concise (简洁).
- **Commit messages and PR title/description** — in **English**.

## Prerequisites

- `BAKABASE_SENTRY_PAT` — Sentry auth token, read from the environment. If it is
  not set, stop and tell the user.
- Run from the `Bakabase/` git repository root.

## Step 1 — Fetch unresolved Sentry issues

Sentry org slug: `bakabase`. API base: `https://bakabase.sentry.io/api/0`.
Authenticate every request with the header
`Authorization: Bearer <BAKABASE_SENTRY_PAT>`.

Scope: only the projects `bakabase-web` and `bakabase-service`. Do **not**
list or scan any other project in the org.

1. For each of the two project slugs, list unresolved issues — follow
   `Link`-header cursor pagination until exhausted:
   `GET /projects/bakabase/<project-slug>/issues/?query=is%3Aunresolved&sort=freq&limit=100`
2. For each issue, fetch the latest event for its stack trace:
   `GET /issues/<issueId>/events/latest/`

From each issue keep `shortId`, `title`, `culprit`, `level`, `count`,
`userCount`, `permalink`, `metadata`. From the latest event keep the
`exception` entry (stack frames) and `breadcrumbs`.

Example with `curl`:

```bash
curl -sS -H "Authorization: Bearer $BAKABASE_SENTRY_PAT" \
  "https://bakabase.sentry.io/api/0/projects/bakabase/<project-slug>/issues/?query=is%3Aunresolved&sort=freq&limit=100"
```

## Step 2 — Triage each issue

For every issue:

1. Read the title, culprit, level, and frequency (`count` / `userCount`).
2. Walk the exception stack frames; map in-app frames to local source files and
   read the relevant code.
3. Determine the root cause and a concrete fix plan.
4. Group issues that share one root cause — fix them together, with one GitHub
   issue per distinct root cause (or one per Sentry issue when unrelated).

Prioritise by impact (`count` × `userCount`, then `level`).

## Step 3 — Classify: fix, or ask the user

- **Fixable, small/medium scope** → proceed (Steps 4–6); do not ask.
- **Cannot be fixed** (needs reproduction you cannot do, third-party/SDK bug,
  environment-only, insufficient data) **or estimated to be a very large
  change** → ask the user via `AskUserQuestion` whether to **skip** it. State
  the root cause briefly and why it is hard. Batch these questions when several
  issues qualify.

Issues the user chooses to skip: leave them untouched and list them in the
final summary and the PR description.

## Step 4 — Reuse or create a GitHub issue

**Before creating anything, look for an existing open issue** that already
covers the same root cause — a previous run of this workflow, or the
maintainer, may have already filed it. List or search open issues on
`anobaka/Bakabase` (match on the Sentry `shortId`, the exception type, or the
culprit) and read any candidate.

- **A suitable open issue already exists** → reuse it: record its number `#N`
  and skip creation. Do **not** open a duplicate; leave its body as-is.
- **No suitable open issue** → create one as described below.

An issue counts as "suitable" only when it describes the same root cause (or
the same Sentry issue) **and is still open** — a closed issue does not count,
file a fresh one. When uncertain, prefer reusing over creating.

### Creating a new issue

Follow `.claude/CLAUDE.md` → "Workflow: GitHub Issue Management". Every issue
MUST be **中文**, **简洁**, and **labelled** — pick existing repo labels
(`bug` is the usual fit for Sentry crashes); create one only if none fits.

Issue body template (Chinese):

```
## 问题
<一句话描述错误现象>

## 根因
<简要根因分析>

## 修复
<简要修复方案>

## Sentry
<shortId> · <permalink>
```

Create it on `anobaka/Bakabase` — `gh issue create --repo anobaka/Bakabase
--title ... --body ... --label bug`, or the equivalent GitHub API call. Record
the returned issue number `#N`.

## Step 5 — Fix on a feature branch

- Create one feature branch for the batch off `main`:
  `git checkout -b fix/sentry-triage-<YYYYMMDD>`.
- Implement the fixes. Split into **multiple commits** by logical unit — the
  number of commits is unconstrained.
- Build / verify per project conventions where feasible (`dotnet build`,
  frontend checks).
- Commit messages are in **English**; each body MUST contain a closing-keyword
  line per issue it resolves:

  ```
  fix: <concise English description>

  Fixes #N
  ```

  `Fixes` / `Closes` / `Resolves` are interchangeable. Do **not** add
  `[skip ci]` to code-fix commits.

## Step 6 — Open the PR

Push the branch and open one PR for the batch (`gh pr create` or the GitHub
API). Title and description in **English**. The description MUST also list every
closing keyword — auto-close fires when the commits reach `main` via the merged
PR:

```
## Summary
Batch fixes for unresolved Sentry issues.

## Closes
Closes #N1
Closes #N2

## Skipped
- <shortId>: <reason> (confirmed with the user)
```

Output the PR URL to the user.

## Step 7 — (Optional) Resolve in Sentry

Only if the user asks, mark the corresponding Sentry issues resolved after the
PR is merged:

```bash
curl -sS -X PUT \
  -H "Authorization: Bearer $BAKABASE_SENTRY_PAT" \
  -H "Content-Type: application/json" \
  -d '{"status":"resolved"}' \
  "https://bakabase.sentry.io/api/0/organizations/bakabase/issues/<issueId>/"
```

## Final summary

Report back: issues fixed (with `#N`), issues skipped (with reason), and the
PR URL.

# Bakabase

Local media manager for organizing files of any type.

## Project Structure

- **Bakabase/** - Main application
  - `src/web/` - React frontend (TypeScript)
  - `src/apps/Bakabase/` - C# Windows desktop entry point
  - `src/apps/Bakabase.Service/` - C# HTTP API layer
  - `src/apps/Bakabase.Cli/` - C# offline build-time tool (SDK/constants generation). Reserve for dev-time codegen only.
  - `src/abstractions/` - C# interfaces & shared types
  - `src/modules/` - C# feature modules (preferred location for new features)
  - `src/legacy/` - C# historical core code, actively maintained
    - New features: prefer `modules/`, but `legacy/` OK if scope is small or tightly coupled with existing legacy code
- **Bakabase.Infrastructures/** - C# hosting & DI setup
- **Bakabase.Updater/** - C# application updater
- **LazyMortal/** - C# internal base framework

## Module Dependencies

```
Bakabase (desktop) → Bakabase.Service → modules → abstractions
                                      ↘ abstractions ↗
```

## Common Commands

```bash
# Frontend
cd Bakabase/src/web && yarn dev          # Start dev server
cd Bakabase/src/web && yarn run gen-sdk  # Regenerate API SDK (offline, no running backend required)

# Backend
dotnet build                                    # Build solution
dotnet run --project Bakabase/src/apps/Bakabase  # Run desktop app

# Database Migration
# - ALWAYS generate via the command below; never hand-write or hand-edit a migration file.
# - Keep the generated migration pure-schema. Do NOT add `migrationBuilder.Sql(...)`
#   calls or any data-migration code inside it — we'd rather accept legacy data
#   loss (especially during beta) than ship a backfill that has to be reasoned
#   about, tested, and maintained alongside the schema change.
dotnet ef migrations add {MigrationName} --verbose --project src/legacy/Bakabase.InsideWorld.Business --startup-project src/apps/Bakabase.Service --context BakabaseDbContext
```

## SDK Generation

`yarn gen-sdk` runs fully offline — it drives `Bakabase.Cli` to emit a fresh
`swagger.json` + `constants.ts` from the built assemblies (no HTTP server,
no SQLite, no side effects), then feeds them through `swagger-typescript-api`
and `openapi-typescript`.

Pipeline source: `src/web/tools/gen-sdk.js` (orchestrator) +
`src/apps/Bakabase.Cli/` (swagger + constants emitters).

Outputs (all committed, **never hand-edit**):
- `src/web/src/sdk/Api.ts`
- `src/web/src/sdk/BApi2.d.ts`
- `src/web/src/sdk/constants.ts`

Scratch: `src/web/.sdk-cache/` (gitignored).

For **when** to regenerate and the rule about static data (e.g.
`ExtensionMediaTypes`) shipping via `constants.ts` instead of a runtime
endpoint, see `.claude/rules/api-conventions.md`.

## Commit & PR Conventions

- **Write commit messages and pull request titles/descriptions in English.**
  GitHub issues are the exception — those stay in Chinese (see the issue
  workflow below).
- Keep the conventional-commit prefixes already used in `git log`
  (`feat`, `fix`, `chore`, `ci`, `docs`, `refactor`, …).

## Workflow: GitHub Issue Management

After completing **any self-contained change** (a feature, fix, or refactor —
one logical unit of work), and before treating the task as done, check in with
the user about GitHub issues. Do this **once per completed change**, not per
file edit, and not for trivial in-progress steps.

Use `AskUserQuestion` to ask what the user wants, offering:

- **创建并关联 issue** — Create a concise Chinese issue describing the change,
  then reference it in the commit so GitHub auto-closes it.
- **跳过** — No issue action needed.

`AskUserQuestion` already appends an **Other** option, so the user can still
type custom instructions (e.g. "把 #12 #15 标记为已处理") without a dedicated
choice — do not add one.

### Creating an issue

Create issues with `gh issue create` against `anobaka/Bakabase`. Every created
issue MUST be:

- **中文** — title and body written in Chinese.
- **简洁** — a clear one-line title plus a short body (what changed / why).
- **Labelled** — pick appropriate label(s) from the existing repo labels.
  Common ones: `bug`, `enhancement`, `feature`, `chores`, `documentation`,
  `architecture optimization`, `breaking-changes`, `test`, `file-processor`,
  `third-party`, `translation`. Check `gh label list -R anobaka/Bakabase`
  when unsure; only create a new label if none fits.

```bash
gh issue create --repo anobaka/Bakabase \
  --title "简洁的中文标题" \
  --body "简要说明改动内容与原因" \
  --label "feature"
```

### Marking issues resolved in the commit

To let GitHub auto-close an issue, add a closing keyword line to the commit
message body — one per issue:

```
Closes #123      # general changes
Fixes #123       # bug fixes
```

Auto-close only fires when the commit reaches the **default branch** (directly
or via a merged PR). After `gh issue create` prints the new issue number, add
its `Closes #N` line to the commit for the change.

### Skip CI for docs / `.claude` changes

`deploy.yml` ("Build and Deploy") auto-runs **only on push to `release/v*`**
— pushing a release branch is a deliberate publish. `main` / `debug-actions`
are **not** auto-deployed; they deploy only via manual `workflow_dispatch`, so
merging several PRs into `main` no longer fires a release per merge (batch
them, then dispatch when ready). `ci.yml` runs only on PRs and is enforced as
a required status check via branch protection, so the merge commit is never
re-tested and deploy is no longer gated on CI.

`[skip ci]` therefore only matters on the auto-deploy branch: add it to a
commit you push to `release/v*` that touches **only** documentation,
`.claude/**`, or other non-code files, to skip that release build. It does
**not** affect `Closes #N` issue auto-closing. On `main` nothing auto-deploys,
so the directive is moot there.

```
docs: 更新 GitHub issue 管理工作流程 [skip ci]

Closes #123
```

**Trap**: GitHub matches the skip tokens (`[skip ci]`, `[ci skip]`,
`[no ci]`, `[skip actions]`, `[actions skip]`) as literal substrings
*anywhere* in the commit message — subject **or** body. So when a
commit's body *describes* the directive (e.g. explaining a workflow's
manual-override path), don't write the literal token inline, or that
commit itself will skip the pipeline. Reword it (`the skip-ci
directive`, `[ skip ci ]` with spaces, etc.) instead.

## 自动化 Issue 处理规范

当被要求处理 GitHub Issue 时，你必须严格遵循以下 4 步流程：

1. **审阅**：使用 `gh` 命令获取 Issue 详情，并结合本地代码库进行分析。
2. **确认（强制暂停）**：向我口述排查结果和你的修改计划。此时**必须停止操作**，并明确询问我：“是否同意按此方案处理？”
3. **处理**：只有在我明确回复同意（如"yes"、"继续"）后，你才可以新建分支并开始修改代码。
4. **提 PR**：代码修改并测试完成后，使用 `gh pr create` 提交 Pull Request，在描述中关联 Issue 号（例如 Closes #xxx），并将 PR 的网页链接输出给我。

# Post parsing

Shared capabilities for reading post content and extracting download information.
The user-facing feature remains **帖子解析** (Post parser).

## Boundaries

- `IPostContentService` reads a supported URL into title, body, comments and lock metadata.
  The legacy adapter selects site-specific readers before the generic HTML reader.
- `IPostDownloadInfoExtractor` extracts links, content groups, access codes, archive passwords
  and ordered post-download file-processing plans using the configured Post parser AI feature.
- `IPostAvailabilityAnalyzer` evaluates expiry and restoration reports in the captured text.
  Its assessment is separate from a provider's observed link availability.
- `IPostLinkHealthChecker` checks supported share metadata conservatively. Unknown providers,
  authentication, network failures and inconclusive pages remain `unknown`.
- These capabilities do not download files, purchase content, create resources, or schedule work.
  Callers decide how to use the result and whether an existing purchase policy applies.

The module depends on the AI module, without depending on acquisition, workflow or
legacy task storage. Legacy readers and the workflow adapter live in the business layer.

## Workflow usage

`postParser.manual` accepts either a post URL or pasted text, with an optional title.
`postParser.readContent` saves the readable snapshot before any purchase or AI operation.
`postParser.unlockContent` assesses expiry reports and applies the caller's purchase policy;
remaining locks suspend the run with a partial result. `postParser.extractDownloadInfo`
produces structured download and post-download processing information, and `postParser.checkLinks`
attaches observed link health. These nodes can run without a resource or acquisition task.
The built-in parse-only workflow is shared by the tool page and its compatibility APIs.

Saved parser tasks link to a workflow run and revision. Re-parsing supersedes and cancels
the old execution; an old run cannot overwrite newer results. Retrying resumes the failed
run through the workflow engine. Existing task records, automatic parsing, batch input and
Tampermonkey endpoints remain available.

SoulPlus tasks may use the configured per-item automatic purchase threshold (default 0).
The minimum remaining account balance also defaults to 0. Purchases refresh both price and
balance under an account lock; unknown positive prices or unknown balances never authorize
spending. Manual approval can override the automatic threshold and expiry assessment, but
still respects the minimum balance and the previously reviewed price. Partially purchased
posts remain incomplete, including when a visible URL could still lack a hidden access code
or archive password.
The parser interface sends users to the original post for manual purchase and then refreshes
the captured content. Existing explicit-purchase APIs remain available for compatibility.
Standalone manual runs do not inherit permission to purchase locked content.

## Versioned instructions

New results use `schemaVersion: 3`. The `resources` array remains flat and each resource may
reference a content group with `groupId`. The accompanying `groups` array contains `id`,
`title`, `kind`, an optional one-sentence `summary`, and short original-text `evidence`.
Kinds are `main`, `preview`, `supplement`, `related`, `tool`, and `unknown`.

The existing extraction call assesses content identity and purpose together. Only links
that refer to the same actual content may share a group, including mirrors across different
providers. Preview files, full releases, different versions, tools, related resources and
supplements stay separate. Required archive volumes are separate downloads, not interchangeable
mirrors. Uncertain identity stays in independent groups or ungrouped; grouping by provider or
assuming every link is the main content would hide meaningful differences. Grouping is an AI
assessment, not verification of remote files, and preserves every distinct URL and its own
credentials and processing plan.

Grouping metadata is bounded to 128 referenced groups, 80-character IDs, 160-character titles,
500-character summaries and eight 300-character evidence snippets per group. Empty or oversized
IDs, duplicate IDs, missing titles and dangling references fall back to ungrouped links.
Unknown purposes become `unknown`. Older results and responses without groups remain usable.
Exact-link deduplication may complete missing membership, but conflicting nonempty group IDs
are kept separate. Persisted resource indices continue to identify the flat array, including
when the interface presents resources in groups.

Each download resource can carry `extraction` with
`requirement: required | notRequired | unknown` and ordered `steps`. The existing field and
DTO names remain for compatibility; the plan describes all post-download file processing,
not only archive extraction. `required` means some processing is needed, even for a plan
containing only rename/move operations. An explicit `notRequired` plan has no steps.

Every step has a unique `id`, an `op`, an `input` (`download` or any prior step id), and an
optional `selector` matching file names or relative paths within that input's outputs.

| Operation | Parameters | Effect |
| --- | --- | --- |
| `renameExtension` | `extension`, such as `.7z` | Change the extension while retaining the relative directory and numeric volume suffix. |
| `renameFile` | `targetName`, such as `archive.7z` | Use this exact file name in each selected input's relative directory. It cannot contain path separators. |
| `moveFile` | `targetDirectory`, such as `books/vol1` | Move selected files to this directory relative to the resource's output root, preserving each file name. `.` means the output root. |
| `extractArchive` | optional `password` | Extract each archive/volume group. Every extraction round retains its own password. |

Operations can occur in any order, for example rename → move → extract → rename → move.
Evidence preserves the source instructions; missing target names, directories or passwords
must not be invented. All target paths must remain within this run's output. Absolute paths,
parent traversal, unsupported operations and conflicting destinations are rejected. Plans
contain no shell commands, arbitrary code, deletes or overwrite operations.

The local executor stages inputs and commits each step into a separate output directory.
Original downloads and earlier step outputs remain intact, so later steps may safely refer
to them and interrupted work can resume from its checkpoints. Final output contains the
remaining file artifacts with their instructed relative paths. A selector matching several
files must not rename/move them to the same destination. Unknown plans require human review.

New AI results that omit an extraction plan receive an explicit `unknown` plan. Historical
results without these fields retain their previous interpretation. A partial result has
`isComplete: false` and may be displayed while its run waits, but is not a complete set of
download credentials.

The SoulPlus reader normalizes thread links to page one and records structured comment
identities, floor numbers, authors and timestamps when the page provides them. This is a
first-page assessment; it does not claim that unread pages contain no expiry or restoration
reports. The account balance is read only from a recognized account panel, never an author's
post profile. Missing balances remain unknown.

Link checks currently recognize explicit unavailable-share pages on Baidu and OneDrive and
query MEGA public-file metadata. MEGA folders, other providers, login or captcha pages and
unrecognized responses remain unknown. Probes are bounded, send no account cookies and only
connect to public addresses of the supported providers. They do not download shared files.

Saved tasks record their first creation time and the successful completion time of
their current parsing execution. Re-parsing clears completion while keeping creation;
failed, cancelled, incomplete or superseded runs cannot mark a task complete. Historical
timestamps remain unknown when no timestamp was recorded. The API and incremental
updates expose UTC instants, and the tool page displays local times, exports both
fields and offers a copy action beside each post link.

## From parsing to acquisition

The tool page lets users review and select links before adding a pending resource.
Multiple selected links describe alternate downloads for one resource; different resources
must be imported separately. Import validates the saved result revision and rejects
conflicting ownership or passwords before creating records.

Each imported lead keeps its access code, archive password and original post reference.
Already-extracted links enter acquisition as resolved links, so they do not require another
AI parsing pass. Import itself does not start acquisition or download files.

Workflow history offers a bounded output preview (20 items, 65,536 characters), with an
explicit truncation indicator. Large file groups retain a summary containing their output
directory and file count. This preview is not used for execution recovery.

## Verification

- Module tests cover extraction and preservation of links and credentials.
- Service tests cover workflow dispatch/retry, revisions, stale results, legacy adapters,
  serializer compatibility, import validation and resolved acquisition inputs.
- Frontend tests cover input modes, selection and import, legacy result shapes, workflow
  labels and the independent manual-run form.

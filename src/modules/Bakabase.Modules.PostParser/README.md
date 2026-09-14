# Post parsing

Shared capabilities for reading post content and extracting download information.
The user-facing feature remains **帖子解析** (Post parser).

## Boundaries

- `IPostContentService` reads a supported URL into title, body, comments and lock metadata.
  The legacy adapter selects site-specific readers before the generic HTML reader.
- `IPostDownloadInfoExtractor` extracts all links, access codes and archive passwords
  using the configured Post parser AI feature.
- Neither capability downloads files, purchases content, creates resources, or schedules work.
  Callers decide how to use the result and whether an existing purchase policy applies.

The module depends on the AI module, without depending on acquisition, workflow or
legacy task storage. Legacy readers and the workflow adapter live in the business layer.

## Workflow usage

`postParser.manual` accepts either a post URL or pasted text, with an optional title.
`postParser.readContent` produces post content; `postParser.extractDownloadInfo` produces
structured download information. These nodes can run without a resource or acquisition task.
The built-in parse-only workflow is shared by the tool page and its compatibility APIs.

Saved parser tasks link to a workflow run and revision. Re-parsing supersedes and cancels
the old execution; an old run cannot overwrite newer results. Retrying resumes the failed
run through the workflow engine. Existing task records, automatic parsing, batch input and
Tampermonkey endpoints remain available.

Legacy SoulPlus tasks may use the already-configured automatic purchase threshold.
Standalone manual runs do not inherit permission to purchase locked content.

## From parsing to acquisition

The tool page lets users review and select links before adding a pending resource.
Multiple selected links describe alternate downloads for one resource; different resources
must be imported separately. Import validates the saved result revision and rejects
conflicting ownership or passwords before creating records.

Each imported lead keeps its access code, archive password and original post reference.
Already-extracted links enter acquisition as resolved links, so they do not require another
AI parsing pass. Import itself does not start acquisition or download files.

Workflow history offers a bounded output preview (20 items, 65,536 characters), with an
explicit truncation indicator. This preview is not used for execution recovery.

## Verification

- Module tests cover extraction and preservation of links and credentials.
- Service tests cover workflow dispatch/retry, revisions, stale results, legacy adapters,
  serializer compatibility, import validation and resolved acquisition inputs.
- Frontend tests cover input modes, selection and import, legacy result shapes, workflow
  labels and the independent manual-run form.

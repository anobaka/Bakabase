# Acquisition execution boundaries

Acquisition coordinates a workflow using shared application capabilities. It owns
the requested result, plan, approvals, provenance and overall admission limit.
The capability that performs an operation owns its execution policy and runtime
facts. Starting another acquisition must not depend on the previous acquisition
finishing every stage.

## Admission and execution

`AcquisitionOptions.Concurrency` limits executing acquisition workflows and defaults
to **20** (range 1–100). Existing saved preferences are preserved. Both legacy
recipes and dynamically compiled plans use the same `acquisition.workflow` BTask
group. Different runs of one recipe have independent conflict keys. Initial runs,
retries, resumes and startup recovery resolve their scheduling policy through the
workflow scheduler.

The acquisition queue submits work to that scheduler; it does not keep a second
count of running acquisition rows. Local recovery batches and continuation requests
also submit independently after their own short admission checks. Per-task locks,
approval validation and storage guards still protect each operation.

An executing workflow counts toward the overall limit while waiting for a
capability. A durable workflow suspension for user input ends that execution and
releases its slot. Lowering the configured limit leaves active executions alone
and postpones new admissions until capacity is available.

There are no acquisition scheduler exceptions for particular activity names or
download stages. Providers enforce their own additional limits:

- Download Center owns download queues and channel/account concurrency. Acquisition
  submits and observes downloads through its adapters and does not count downloads
  against a second acquisition-specific download limit.
- AI owns inference admission through `ILlmFeatureExecutionPolicy`. The file
  processing feature currently permits one provider request at a time. The policy
  applies inside `ILlmService`, including streaming; cache hits bypass inference.
  Cancellation, failure and stream disposal release the lease. Other features
  retain their own policies, including post-parser AI admission.
- File processing and publication own artifact/directory exclusion and their
  storage checks. The acquisition processing activity consumes
  `IFileArtifactProcessor`; workflow activities remain the public orchestration
  boundary. File work on unrelated artifacts can progress concurrently.

## Runtime information

Activities execute through `IWorkflowActivity` and, where applicable,
`IResumableWorkflowActivity`. A capability can additionally register an
`IWorkflowActivityRuntimeProvider` to interpret its existing runtime evidence.
`WorkflowActivityRuntimeRegistry` awaits those providers; acquisition consumes the
resulting snapshot without knowing each capability's stage names. Both registry
and providers use a scoped lifetime, so a provider can inject its own scoped reader
or database context. `ReadAsync(context, cancellationToken)` reads existing local
or status-API snapshots with provider-owned read budgets and caching; it never
starts work or probes a source. Pure projections return a completed `ValueTask`.
Existing batched download observations are supplied once as typed evidence, without
adding a read per task. Calls within one request are awaited sequentially so scoped
database readers are not used concurrently.

The snapshot includes capability identity, presentation metadata, and any observed
stage, progress, queue state or policy information the provider can substantiate.
Missing information stays unknown. A workflow's running state alone does not prove
that an underlying provider has acquired capacity. Live progress must match the
current run, node and cursor; observations never authorize an operation or mutate
its checkpoint.

Download-specific source binding and recovery approvals remain acquisition domain
rules. Download transfer progress is interpreted by the download capability, and
archive progress and AI phase information by the file-processing capability.
Unknown activities can still expose their registered name and generic progress;
the frontend uses provided names when it has no specialized presentation.

## Recovery actions and re-analysis

Continuing a run and recomputing part of a plan are different operations. A
capability can expose read-only recovery actions through
`IWorkflowRunActionProvider`. The scoped `WorkflowRunActionRegistry` collects
descriptors from all registered providers without knowing whether the provider
belongs to AI, downloads, file processing or a future capability. Descriptors
identify the capability, action mode and stable plan `NodeId`; they do not carry
provider-specific inputs or mutate a run. Providers can inspect their own
artifacts asynchronously and return no action when the run is outside a safe
boundary.

The Continue dialog previews actions for each selected task. Continuing the current
node is the default. A stopped transfer can reuse a verified receipt or retry the
provider operation under its original source and account binding. A retry of an
earlier transfer has a separate, opt-in action when its receipt and the downstream
download support a replay. An active or partially downloaded file keeps its
current binding.

Optional batch actions implement `IAcquisitionBatchActionProvider`. Each column
lists one action reported by the providers. Its header selects matching tasks
across the current filter, and each row retains one choice. Capacity checks and
submission use the same per-task choices. The queue revalidates the chosen
action against the saved task revision and provider evidence before it starts.
File reanalysis uses the selected artifact lineage or the current assessment
node, so repeated assessment nodes in a run retain their individual settings.

The list should expose a state vector instead of treating every exceptional
case as `attention`: execution status (`queued`, `running`, `waiting`,
`succeeded`, `failed`, `cancelled`), current node instance, readiness/blocking
reason, next action, capability, and data validity (source, download, artifact)
are separate facts. The existing stage remains a coarse grouping for navigation;
facets such as `nextAction`, `capability`, `waitReason`, and `currentNodeId`
allow a user to select tasks that share one action contract. A batch preview then
returns the available action descriptors per task and groups compatible rows;
rows that need input or a different action remain visible with their reason.

Local recovery currently uses 100-item partitions because preview checks and the
SQLite write transaction are deliberately bounded. The product should keep that
as an internal partition size, while introducing a durable partition group for a
larger user selection: each partition owns its preview token, evidence, capacity
check and queue state, and the group reports aggregate progress. This avoids
holding hundreds of task/run locks in one transaction and avoids claiming that a
single space acknowledgement covers unrelated partitions. The UI can then say
“split into N batches” and submit compatible partitions independently, without
silently dropping tasks or forcing the user to repeat the whole selection.

## Adding a capability

1. Implement the reusable service API and enforce resource admission in its owning
   module, so callers outside acquisition receive the same behavior.
2. Adapt it through a workflow activity. Keep acquisition's plan and approval rules
   outside the capability's execution policy.
3. Register a scoped runtime provider when generic activity progress is insufficient.
   Read its own bounded snapshots asynchronously with cancellation, or use typed,
   provider-owned evidence already collected in a batch. Do not depend on acquisition
   DTOs or add source probes inside the status path.
4. Verify overlapping acquisitions, provider limit enforcement, cancellation and
   independent completion. Adding a capability should not require a new branch in
   the acquisition scheduler or runtime display dispatcher.

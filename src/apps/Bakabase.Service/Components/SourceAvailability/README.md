# Source availability

A metadata observation is independent of download readiness and artifact completion.
`ISourceAvailabilityProbe` never purchases, creates a registered user account, transfers, redeems a
share, or downloads payloads. A provider may return `confirmedUnavailable` only from
explicit provider evidence; network failures, login requirements, empty lists and
unsupported probes stay separate. Generic HTTP 404/410 is inconclusive.

GoFile's website automatically prepares a guest session even without login. Its
adapter keeps that anonymous credential privately under this AppData store and
reuses it across checks/restarts; session initialization is separate from the
bounded content GET. It never requests a user's email/password or creates a
registered account. Official `error-notFound` and expiry gates are distinct from
generic HTTP errors, passwords and private content. The website signing script
is fetched only from its fixed HTTPS endpoint, expires after one hour, and runs
in a fresh JS-only interpreter with no host bindings and finite budgets. Script
or protocol changes remain unknown; old signing secrets are never hardcoded.

`SourceAvailabilityService` shares bounded observations between post parsing,
acquisition preparation and explicit checks. Four global requests and two per
provider can run concurrently. Keys include the probe implementation/version,
locator, access code and network settings. Public metadata is shared across download
methods; authenticated probes must opt into `UsesAccountScope`, isolating account
and credential generations. Bump `CacheVersion` whenever classification changes.
Normal task rendering reads the cache without probing. Preparation has a bounded
check budget; post parsing checks at most four links concurrently within 45 seconds.
Unknown or stale results never authorize disabling a source.

The private AppData `source-availability` store atomically replaces observations and
preview snapshots, and retains append-only task-scoped decision histories. Disabling
is a reversible overlay, not deletion of a URL, task, plan, file or workflow history.
A corrected locator/access code has its own decision identity. A ready batch fixes
task/source IDs, plan revision/hash, evidence and decision version for 20 minutes.
Apply rechecks these under the same acquisition gate used by plan start/replacement.
Active and completed tasks are protected. Unfinished checks become failed after a
restart and can be checked again. Batch checking is a BTask with bounded concurrency;
no disabling or restoring happens until the explicit apply request.

New automatic acquisition plans preserve only producer-confirmed same-content
mirrors. Required parts remain independent. Each mirror owns its credentials,
processing instructions and provenance. Global provider priority (Baidu first by
default) is frozen into new plans. Manual choices and old plans retain their source.
Fallback is allowed only before any historical workflow execution and moves source
instructions together, recording a new plan revision. Once a transfer/download may
have occurred, existing execution and artifact-reuse rules take precedence; later
source observations/disabling never cancel or switch that execution.

Current probes cover Baidu public share metadata, GoFile anonymous content metadata, Google Drive public folder listings, Pixeldrain `/u` and `/l`, public
SharePoint folder root metadata and HTTP HEAD. Existing MEGA parsing metadata checks
remain available at the parser boundary. Unsupported providers and account-remote
paths stay unknown/unsupported rather than being labeled expired.

Google Drive folder checks read the public listing used by gdown. Visible entries confirm access to folder metadata. A Google 404 can also indicate missing permission. That response keeps the share selectable with unknown availability.

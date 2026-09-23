# Installed native GUI acceptance

`run-probe.py` installs the audited unified and client packages on a fresh,
architecture-matched GitHub-hosted runner. It never uses a user's installation,
changes accessibility permissions, or treats HTTP content as visible UI.

The existing required arguments are `--unified-packages`, `--client-packages`,
`--rid`, `--version`, `--provenance` and `--results-directory`. Add
`--flow empty-library` to perform the bounded native empty-library workflow.

On macOS, `--macos-observer direct-ax` explicitly selects public direct AX calls
through the existing `osascript` host. The default remains `system-events`;
Windows always uses UIA and must not receive `direct-ax`.

Before any direct-AX workflow action, both installed products must pass the
separate availability diagnostic and a **complete** read-only tree gate.
The independent trees/reports are saved under each product's `direct-ax-tree/`.
The previous System Events observations remain in `native-accessibility/`.
A failed or partial direct tree does not fall back to another action provider.
`capability` mode stops after these diagnostics without driving product controls.

The direct observer reads only the exact owned application's window/child tree:
at most eight windows, 1,000 nodes and depth 40, with the existing 24-second read,
30-second helper and 90-second readiness bounds. The empty flow retains its
90-second per-state and 600-second overall deadline. Metadata is explicitly
selected; editable values are never read. Unknown subtree errors, cycles and
budget exhaustion produce partial trees and cannot establish absence/uniqueness.
Only the conservative `AXStaticText` leaf allowlist may report unsupported
`AXChildren`. Containers, editable controls and unknown roles must return an
explicit array. A `NoValue` result is accepted as empty only when the separate
`AXUIElementGetAttributeValueCount` call succeeds with exactly zero, with process
identity checked before and after. The node records `no-value-count-zero` evidence.
Unknown, nonzero or failed counts and every other child-read error remain partial.

Every node's PID is checked before reading its role or metadata. Descendants of
`AXTextField`, `AXTextArea` and `AXComboBox` are structurally traversed, but their
names, text, identifiers and actions are not read or retained and cannot become
action candidates. This also prevents an editable value exposed as a child
`AXStaticText` from entering the saved evidence.

The first unknown PID still fails the complete-tree gate without reading its
role, text, values or actions. Only a node obtained directly from a verified
owned parent's `AXChildren` receives pointer-only diagnostics: the parent edge
and application's window are re-read, and the foreign `AXParent` / `AXWindow`
references must compare equal to those held native objects. Both AX calls must
succeed and the node's PID must remain stable.

A hosted-only, at most two-second `libproc` helper samples the exact application
and embedded PIDs twice, recording executable paths, UIDs, PPIDs and microsecond
start times. These identities must agree with the application's identity from
before and after the failed read, have matching UIDs, and predate the diagnostic.
This may prepare **one candidate for the next read**; the failed first read does
not become complete. No executable basename, process name or PPID grants access.

The next read checks both full OS identities before and after native inspection.
Before any embedded metadata, it re-resolves the owned application's window
and parent path, and requires the exact reciprocal root edge and window reference.
An `AXWebArea` can be the content root directly. An `AXGroup` anchor must instead
lead through a unique, single-child chain of `AXGroup` wrappers to exactly one
`AXWebArea`. Discovery reads only roles, PID and parent/child/window references;
zero/multiple children, other roles, cycles or another PID fail the gate.
Wrapper nodes remain structure-only: no labels, identifiers, values, actions or
geometry are read. Only the resulting WebArea and its proven descendants may
expose content. Each descendant must have the bound PID and a reciprocal
parent/child chain to the anchor with the same held native window. An unvisited hit-test descendant is followed only
through pointer relations until it reaches an already verified ancestor.
Another PID, the same PID outside this root, a changed edge/window, reused PID,
changed identity, incomplete tree or exhausted budget fails closed.

Only a complete successful next read promotes `embeddedAXBinding` into the
app and snapshot. `embeddedAXProof` records the anchor, wrapper role/count chain,
exact content-root path and stable OS checks; failure diagnostics contain no
wrapper content. Bounded `axReadCounts` distinguish call-budget exhaustion from
structural failures. Actions require that same complete snapshot binding and
content scope, re-read the full tree, compare held native identities, recheck the root after the action and
verify both OS identities again. A partial read never authorizes an action.
All checks consume the existing read/action/readiness deadlines; no processes
are enumerated and argv, environment, UI of unrelated processes and logs are
never read. `capture(..., deadline=...)` can shorten the readiness deadline to
an enclosing flow's absolute deadline, and cannot extend its 90-second default.

A child does not inherit visibility from its window. Named web controls and
static text require finite geometry and an application-scoped, read-only hit
test within their verified window. The hit must be the target or an ancestor
chain-confirmed descendant, with matching process and window references.
Within one hit-test ascent only, its first complete chain proof avoids repeated
whole-chain traversal. Every subsequent edge/PID/window is still read afresh,
and the root is checked again at the end. No proof is shared between tree walk
and exposure, separate reads, or actions; the 16,000-check/24-second bounds remain.
Offscreen, clipped/occluded within that application and no-hit controls are not visible; an unknown
geometry/read prevents a complete-tree claim. This does not perform coordinate
clicks or prove physical presentation/occlusion by other applications. Each node
records a fixed `visibilityEvidence` classification.

Actions re-read the complete current tree in the same provider, require a unique
semantic match, resolve the same provider path, and recheck live element/window
identity before acting. Windows aliases require identical runtime IDs and
consistent metadata; equal names alone do not collapse distinct elements.

The flow interface accepts `operation="set"` with a string `value` of at most
4,096 characters and `operation="scroll"` with the usual selector. `set` requires
a visible, enabled, nonsecure text field with observed and rechecked
`valueSettable=true`: macOS uses public `AXUIElementIsAttributeSettable` /
`AXUIElementSetAttributeValue`; Windows uses `ValuePattern.IsReadOnly` /
`SetValue`. Existing field values are never read back, and submitted values
must not enter flow logs. Secure controls and all editable descendants remain
ineligible for every action.

`scroll` may select an offscreen control or named noneditable semantic region
from the complete tree. It requires an observed and rechecked native capability:
Windows `ScrollItemPattern.ScrollIntoView`, or macOS `AXScrollToVisible` **only
when returned by public `AXUIElementCopyActionNames`**. [WebKit exposes this
provider action](https://github.com/WebKit/WebKit/blob/main/Source/WebCore/accessibility/mac/WebAccessibilityObjectWrapperMac.mm)
but it is not an Apple SDK standard action constant. The observer
uses public `AXUIElementPerformAction`, without private API, guessed action or
fallback. Unsupported targets fail explicitly. macOS semantic region labels
are limited to `AXGroup`, `AXHeading` and `AXScrollArea` outside editable
subtrees. A noneditable `AXStaticText` may also expose an exact displayed-value
label and its own advertised scroll action; no ancestor is guessed as a scroll
target. Snapshot fields `scrollToVisible`, `valueSettable` and
`editableAncestor` describe these boundaries. A successful submission is not
proof of the resulting visible state; the flow must observe that separately.

The action helper retains its 15-second outer and 12-second internal bounds.
There are no coordinate, keyboard or DOM fallbacks. Checkbox/scope presses keep
their existing `AXPress` / UIA `TogglePattern` behavior.

Local validation uses only synthetic native-API fixtures and pure in-memory CF
and AXValue point/size containers. It never checks local AX trust or reads the desktop:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
  -s src/tests/native-gui-acceptance -p 'test_*.py'
```

Neither availability nor a complete tree is a successful product workflow.
The empty-library gate remains distinct from native pairing, remote query/detail
and offline recovery (`mainFlowPassed` stays false).

`--flow federated --source-publish <hosted-publish-directory>` exercises these
remaining workflows with one installed unified reader and the explicit
`Bakabase.NativeGui.SourceHost` fixture. The fixture uses production Shell and
Service assemblies from the execution commit, plus web assets verified against
the candidate package's portable ZIP. Both provenances are recorded. It has a
private executable, AppData profile, loopback port and single-instance ID; this
does not certify two installed unified copies or separate physical devices.

The four named phases (source pairing, reader pairing, reader query, reader
recovery) share one 600-second deadline. Each retains 20 actions and 40 reads,
with the existing per-action/read/readiness bounds. Resource preparation alone
uses production API writes. Native controls accept terms, enable sharing,
request and approve read-only access, enable independent browsing, search and
open the exact resource detail. Read-only API checks confirm directional grants
and unchanged source metadata, media and playback history.

The source is then stopped and restarted with its original data and grants.
Native UI must show partial coverage while offline and recover without reader
restart or re-pairing. On macOS the restarted source must establish a fresh
complete embedded-process binding. Source cleanup verifies owned process exit
and removes only its private profile and preflighted test bundle cache domains.
`mainFlowPassed` is set only after all workflow steps and source/installed-product
cleanup succeed. The fixture's own report never declares that overall result.
The direct-AX runner also arms exact renderer identities for both installed
products while they are alive. After their main processes stop, the existing
lifecycle's pre-removal callback verifies those renderers have exited using
read-only exact-PID observations (at most 30 seconds per product). An uncertain
or absent binding retains the installed fixtures and fails cleanup; no renderer
is signalled or selected by executable name.

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

A PID mismatch still fails the complete-tree gate. For the first mismatch
obtained directly from a verified parent's `AXChildren`, diagnostics retain the
expected/actual PIDs and numeric tree path. After rechecking the owned parent
edge and application's window reference, the observer reads only the foreign
node's `AXParent` and `AXWindow` references and records equality booleans and AX
error numbers. It does not read that node's role, text, values or actions, follow
its pointers, or treat matching references as permission to continue.

One hosted-only helper may then sample exactly that PID twice using public
`libproc` calls, recording executable path, UID, PPID and microsecond start time
only if stable. Its two-second cap also fits within the original read/readiness
deadlines; it never enumerates processes or reads argv, environment, UI or logs.
The report labels these facts as diagnostics with `ownershipEstablished=false`.
They do not establish that a WebKit process belongs to the product or make a
partial tree pass.

A child does not inherit visibility from its window. Named web controls and
static text require finite geometry and an application-scoped, read-only hit
test within their verified window. The hit must be the target or an ancestor
chain-confirmed descendant, with matching process and window references.
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
subtrees. Snapshot fields `scrollToVisible`, `valueSettable` and
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
The empty-library gate remains distinct from untested native pairing, remote
query/detail and offline recovery (`mainFlowPassed` stays false).

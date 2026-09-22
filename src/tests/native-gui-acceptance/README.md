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

A child does not inherit visibility from its window. Named web controls and
static text require finite geometry and an application-scoped, read-only hit
test within their verified window. The hit must be the target or an ancestor
chain-confirmed descendant, with matching process and window references.
Offscreen, clipped/occluded within that application and no-hit controls are not visible; an unknown
geometry/read prevents a complete-tree claim. This does not perform coordinate
clicks or prove physical presentation/occlusion by other applications. Each node
records a fixed `visibilityEvidence` classification.

Direct actions currently support only observed `AXPress` controls needed by the
empty-library workflow. They re-read the complete current tree, require a unique
visible enabled match, resolve the same provider path, compare live native
element/window references, and recheck metadata and process identity before
pressing. The action helper retains its 15-second bound. There are no coordinates,
keyboard fallback or editable-field actions in this mode.

Local validation uses only synthetic native-API fixtures and pure in-memory CF
and AXValue point/size containers. It never checks local AX trust or reads the desktop:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
  -s src/tests/native-gui-acceptance -p 'test_*.py'
```

Neither availability nor a complete tree is a successful product workflow.
The empty-library gate remains distinct from untested native pairing, remote
query/detail and offline recovery (`mainFlowPassed` stays false).

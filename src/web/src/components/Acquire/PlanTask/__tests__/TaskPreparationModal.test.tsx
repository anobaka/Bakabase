import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import TaskPreparationModal from "../TaskPreparationModal";
import cnAcquire from "@/locales/cn/components/acquire.json";
import enAcquire from "@/locales/en/components/acquire.json";
import cnAcquisition from "@/locales/cn/pages/acquisition.json";
import enAcquisition from "@/locales/en/pages/acquisition.json";

const mocks = vi.hoisted(() => ({ previewPreparation: vi.fn(), confirmPreparation: vi.fn(), createPortal: vi.fn(), openLink: vi.fn() }));
vi.mock("../api", () => ({ planTaskApi: mocks }));
vi.mock("../DraftPreparation", () => ({ default: () => <p>title search</p> }));
vi.mock("../PostImageSourceAction", () => ({ default: () => <button>choose images from post</button> }));
vi.mock("@/components/ExternalLink", () => ({ default: ({ href, children, ...props }: any) =>
  <a href={href} aria-label={props["aria-label"]} onClick={event => { event.preventDefault(); mocks.openLink(href); }}>{children}</a> }));
vi.mock("../../MethodPreparation", () => ({ default: ({ onAccountChange }: any) =>
  <button onClick={() => onAccountChange("another-account")}>change source account</button> }));
vi.mock("../../SourceResolution", () => ({ default: () => null }));
vi.mock("@/components/SourceAvailability/SourcePriorityModal", () => ({ default: () => null }));
vi.mock("@/components/SourceAvailability/SourceHealthBadge", () => ({ default: ({ health }: any) => <span>{health?.status}</span> }));
vi.mock("@/components/ProcessingPlanRevisions/SourceProcessingPlanPanel", () => ({ default: () => null }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({ useBakabaseContext: () => ({ createPortal: mocks.createPortal }) }));
vi.mock("@/components/PasswordSelector/ArchivePasswordInput", () => ({ default: ({ label, value, onValueChange }: any) =>
  <input aria-label={label} value={value} onChange={event => onValueChange(event.target.value)} /> }));
vi.mock("@/components/bakaui", async () => {
  const { createContext, useContext } = await import("react");
  const radio = createContext<{ value?: string; isDisabled?: boolean; onValueChange?: (value: string) => void }>({});
  return {
    Modal: ({ children, footer, title }: any) => <div role="dialog" aria-label={title}>{children}{footer}</div>,
    Spinner: () => <span>loading</span>,
    Chip: ({ children }: any) => <span>{children}</span>,
    Button: ({ children, onPress, isDisabled, isLoading, "aria-label": label }: any) =>
      <button aria-label={label} disabled={isDisabled || isLoading} onClick={onPress}>{children}</button>,
    Checkbox: ({ children, isSelected, isDisabled, onValueChange }: any) => <label>
      <input type="checkbox" checked={isSelected} disabled={isDisabled} onChange={event => onValueChange(event.target.checked)} />{children}</label>,
    RadioGroup: ({ children, label, value, isDisabled, onValueChange }: any) => (
      <div role="radiogroup" aria-label={label}>
        <radio.Provider value={{ value, isDisabled, onValueChange }}>{children}</radio.Provider>
      </div>
    ),
    Radio: ({ children, value }: any) => {
      const group = useContext(radio);
      return <label>
        <input type="radio" checked={group.value === value} disabled={group.isDisabled} onChange={() => group.onValueChange?.(value)} />
        {children}
      </label>;
    },
    Input: ({ label, value, onValueChange }: any) => <input aria-label={label} value={value} onChange={event => onValueChange(event.target.value)} />,
    Select: ({ label, selectedKeys, dataSource, onSelectionChange, isDisabled }: any) => <select aria-label={label} value={selectedKeys[0]} disabled={isDisabled}
      onChange={event => onSelectionChange(new Set([event.target.value]))}>
      {dataSource.map((item: any) => <option key={item.value} value={item.value}>{item.label}</option>)}</select>,
  };
});
const sources = [
  { id: "a", kind: "auto", url: "https://gofile.io/d/example", archivePassword: "  exact password  ",
    processing: { requirement: "required", steps: [], evidence: [{ origin: "post" }] } },
  { id: "b", kind: "auto", url: "https://pixeldrain.com/u/example" },
  { id: "invalid", kind: "auto", url: "https://example.com/expired" },
];
const detail: any = { task: { id: 600, title: "Content", planRevision: 7, builderId: "draft", phase: "preparing" },
  draftSources: sources, planHistory: [], issues: [], steps: [] };
const viewOf = (source: any) => ({ candidate: { ...source, extractionPlanJson: source.processing && JSON.stringify(source.processing) },
  methods: [], canSelect: source.id !== "invalid", canSelectManually: true, state: "ready", requirements: [],
  availability: { status: source.id === "invalid" ? "confirmedUnavailable" : "available" } });
const action = async (fn: () => Promise<any>) => { await fn(); };
const onClose = vi.fn();
function mockPreview(_id: number, request: any) {
  const choices = request.selectedSourceIds ?? ["b"];
  return Promise.resolve({ expectedRevision: 7, title: "Content", providerPriority: ["pixeldrain", "gofile"],
    allowUnavailableSourceIds: request.allowUnavailableSourceIds ?? [],
    requiresRelationshipChoice: true, relationship: request.relationship,
    groups: [{ id: "main-sources", title: "Content", required: true,
      selectionMode: request.relationship === "independent" ? "multiple" : "single",
      selectedSourceIds: choices, recommendedSourceIds: ["b"], sources: (request.sources ?? sources).map(viewOf) }],
    requirements: [], canConfirm: choices.length > 0 && (!choices.includes("invalid") || request.allowUnavailableSourceIds?.includes("invalid")) });
}
beforeEach(() => { vi.clearAllMocks(); mocks.previewPreparation.mockImplementation(mockPreview); mocks.confirmPreparation.mockResolvedValue(detail); });
afterEach(() => vi.unstubAllGlobals());

const sharedDownload = {
  groups: [
    { partId: "alpha", title: "Alpha release", deliveryDirectory: "/library/acquired/alpha" },
    { partId: "beta", title: "Beta release", deliveryDirectory: "/library/acquired/beta" },
    { partId: "gamma", title: "Gamma release", deliveryDirectory: "/library/acquired/gamma" },
  ],
  canUseArchiveDirectories: true,
  alreadyUsesArchiveDirectories: false,
  fingerprint: "a".repeat(64),
};
const sharedAction = { name: "acquisition.preparation.sharedDownload.open" };
const sharedDialog = { name: "acquisition.preparation.sharedDownload.title" };
const sharedConfirm = { name: "acquisition.preparation.sharedDownload.confirm" };

function previewSharedDownload(preview = sharedDownload) {
  mocks.previewPreparation.mockImplementation(async (id, request) => ({
    ...await mockPreview(id, request), requiresRelationshipChoice: false, sharedDownload: preview,
  }));
}

async function openSharedDownload() {
  const open = await screen.findByRole("button", sharedAction);
  await waitFor(() => expect(open).toBeEnabled());
  fireEvent.click(open);
  return within(screen.getByRole("dialog", sharedDialog));
}

it("requires a deliberate group choice and saves only the reviewed shared download decision", async () => {
  previewSharedDownload();
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const review = await openSharedDownload();
  expect(review.getByText("acquisition.preparation.sharedDownload.scope")).toBeVisible();
  expect(review.getByText("acquisition.preparation.sharedDownload.next")).toBeVisible();
  for (const { title, deliveryDirectory } of sharedDownload.groups) {
    expect(review.getByText(title)).toBeVisible();
    expect(review.getByText(deliveryDirectory)).toBeVisible();
  }
  expect(review.getAllByRole("radio")).toHaveLength(3);
  expect(review.getByRole("button", sharedConfirm)).toBeDisabled();
  expect(mocks.confirmPreparation).not.toHaveBeenCalled();

  const selected = review.getByRole("radio", { name: /Beta release/ });
  fireEvent.click(selected);
  expect(selected).toBeChecked();
  fireEvent.click(review.getByRole("checkbox", { name: "acquisition.preparation.sharedDownload.archiveDirectories" }));
  fireEvent.click(review.getByRole("button", sharedConfirm));

  await waitFor(() => expect(mocks.confirmPreparation).toHaveBeenCalledExactlyOnceWith(600, {
    expectedRevision: 7, sharedDownload: { keepPartId: "beta", useArchiveDirectories: true, expectedFingerprint: sharedDownload.fingerprint },
  }));
  expect(onClose).toHaveBeenCalledOnce();
});

it("cancels the review without saving its decision or closing preparation", async () => {
  previewSharedDownload({ ...sharedDownload, alreadyUsesArchiveDirectories: true });
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const review = await openSharedDownload();
  expect(review.queryByRole("checkbox")).toBeNull();
  expect(review.getByText("acquisition.preparation.sharedDownload.archiveDirectoriesAlreadyEnabled")).toBeVisible();
  fireEvent.click(review.getByRole("radio", { name: /Alpha release/ }));
  fireEvent.click(review.getByRole("button", { name: "common.action.cancel" }));
  expect(screen.queryByRole("dialog", sharedDialog)).toBeNull();
  expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeEnabled();
  expect(mocks.confirmPreparation).not.toHaveBeenCalled();
  expect(onClose).not.toHaveBeenCalled();
});

it("blocks an open shared download confirmation after a task revision changes", async () => {
  previewSharedDownload();
  const props = { detail, busy: false, onAction: action, onClose };
  const view = render(<TaskPreparationModal {...props} />);
  const review = await openSharedDownload();
  fireEvent.click(review.getByRole("radio", { name: /Beta release/ }));
  expect(review.getByRole("button", sharedConfirm)).toBeEnabled();

  view.rerender(<TaskPreparationModal {...props} detail={{ ...detail, task: { ...detail.task, planRevision: 8 } }} />);
  expect(review.getByRole("alert")).toHaveTextContent("PLAN_VERSION_CONFLICT");
  expect(review.getByRole("button", sharedConfirm)).toBeDisabled();
  fireEvent.click(review.getByRole("button", sharedConfirm));
  expect(mocks.confirmPreparation).not.toHaveBeenCalled();
});

it("keeps an unavailable shared download decision open for review after refusal", async () => {
  previewSharedDownload();
  mocks.confirmPreparation.mockRejectedValueOnce(new Error("SHARED_DOWNLOAD_UNAVAILABLE: Folder changed"));
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const review = await openSharedDownload();
  fireEvent.click(review.getByRole("radio", { name: /Beta release/ }));
  fireEvent.click(review.getByRole("button", sharedConfirm));
  expect(await review.findByRole("alert")).toHaveTextContent("acquisition.plan.replan.error.SHARED_DOWNLOAD_UNAVAILABLE");
  expect(review.getByRole("button", sharedConfirm)).toBeDisabled();
  fireEvent.click(review.getByRole("button", sharedConfirm));
  expect(mocks.confirmPreparation).toHaveBeenCalledOnce();
  expect(onClose).not.toHaveBeenCalled();
});

it("hides combining sources after an unsaved source change", async () => {
  previewSharedDownload();
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  await waitFor(() => expect(screen.getByRole("button", sharedAction)).toBeEnabled());
  fireEvent.click(screen.getByRole("checkbox", { name: "gofile.io" }));
  expect(screen.queryByRole("button", sharedAction)).toBeNull();
  expect(mocks.confirmPreparation).not.toHaveBeenCalled();
});

it("shows explicit default mirror mode and priority recommendation, and confirms preparation without a run", async () => {
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const confirm = await screen.findByRole("button", { name: "acquisition.preparation.confirm" });
  await waitFor(() => expect(confirm).toBeEnabled());
  expect(screen.getByRole("combobox", { name: "acquisition.preparation.relationship" })).toHaveValue("mirrors");
  expect(screen.getByRole("checkbox", { name: "pixeldrain.com" })).toBeChecked();
  await waitFor(() => expect(screen.getByRole("checkbox", { name: "example.com" })).toBeEnabled());
  expect(screen.getByRole("checkbox", { name: "example.com" })).not.toBeChecked();
  expect(screen.queryByText("title search")).toBeNull();
  expect(mocks.confirmPreparation).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("checkbox", { name: "gofile.io" }));
  await waitFor(() => expect(confirm).toBeEnabled());
  expect(screen.getByRole("checkbox", { name: "pixeldrain.com" })).not.toBeChecked();
  fireEvent.click(confirm);
  await waitFor(() => expect(mocks.confirmPreparation).toHaveBeenCalledWith(600, expect.objectContaining({
    expectedRevision: 7, relationship: "mirrors", selectedSourceIds: ["a"], sources: undefined, checkAvailability: false,
  })));
  expect(onClose).toHaveBeenCalledOnce();
  expect(mocks.previewPreparation.mock.calls[0][1].checkAvailability).toBe(true);
  expect(mocks.previewPreparation.mock.calls.at(-1)?.[1].checkAvailability).toBe(false);
});

it("permits independent selections only after choosing the visible relationship mode", async () => {
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const relation = await screen.findByRole("combobox", { name: "acquisition.preparation.relationship" });
  await waitFor(() => expect(relation).toBeEnabled());
  fireEvent.change(relation, { target: { value: "independent" } });
  await waitFor(() => expect(screen.getByRole("checkbox", { name: "gofile.io" })).toBeEnabled());
  fireEvent.click(screen.getByRole("checkbox", { name: "gofile.io" }));
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeEnabled());
  expect(screen.getByRole("checkbox", { name: "pixeldrain.com" })).toBeChecked();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.preparation.confirm" }));
  await waitFor(() => expect(mocks.confirmPreparation).toHaveBeenCalledWith(600, expect.objectContaining({
    relationship: "independent", selectedSourceIds: ["b", "a"],
  })));
});

it("adds a source over insecure HTTP without crypto.randomUUID and preserves raw passwords and processing evidence", async () => {
  vi.stubGlobal("crypto", { getRandomValues: crypto.getRandomValues.bind(crypto) });
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const add = await screen.findByRole("button", { name: "acquisition.draft.addSource" });
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.draft.addSource" })).toBeEnabled());
  fireEvent.click(screen.getByRole("button", { name: "acquisition.draft.addSource" }));
  const editor = within(screen.getByRole("dialog", { name: "acquisition.preparation.sourceDetails" }));
  fireEvent.change(editor.getByRole("textbox", { name: "acquisition.preparation.url" }), { target: { value: "https://example.net/new" } });
  fireEvent.change(editor.getByRole("textbox", { name: "acquisition.sharedList.column.password" }), { target: { value: "  newly added  " } });
  fireEvent.click(editor.getByRole("button", { name: "common.action.save" }));
  await waitFor(() => expect(mocks.previewPreparation).toHaveBeenLastCalledWith(600, expect.objectContaining({ sources: expect.arrayContaining([
    expect.objectContaining({ id: "a", archivePassword: "  exact password  ", processing: sources[0].processing }),
    expect.objectContaining({ id: expect.stringMatching(/^[0-9a-f-]{36}$/), url: "https://example.net/new", archivePassword: "  newly added  " }),
  ]) }), expect.any(AbortSignal)));
  expect(mocks.confirmPreparation).not.toHaveBeenCalled();
});

it("preserves known part membership and chooses one source within each mirror group", async () => {
  mocks.previewPreparation.mockImplementation((_id, request) => Promise.resolve({ expectedRevision: 7,
    requiresRelationshipChoice: false, groups: ["one", "two"].map((group, index) => ({
      id: group, required: true, selectionMode: "single", recommendedSourceIds: [`${group}-a`],
      selectedSourceIds: (request.selectedSourceIds ?? ["one-a", "two-a"]).filter((id: string) => id.startsWith(group)),
      sources: ["a", "b"].map(suffix => viewOf({ id: `${group}-${suffix}`, groupId: group, kind: "auto", url: `https://${group}-${suffix}.example/file` })),
    })), requirements: [], canConfirm: true }));
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const choice = await screen.findByRole("checkbox", { name: "one-b.example" });
  await waitFor(() => expect(choice).toBeEnabled());
  fireEvent.click(choice);
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeEnabled());
  expect(screen.getByRole("checkbox", { name: "one-a.example" })).not.toBeChecked();
  expect(screen.getByRole("checkbox", { name: "two-a.example" })).toBeChecked();
  expect(screen.queryByRole("combobox", { name: "acquisition.preparation.relationship" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.preparation.confirm" }));
  await waitFor(() => expect(mocks.confirmPreparation).toHaveBeenCalledWith(600, expect.objectContaining({ selectedSourceIds: ["one-b", "two-a"] })));
});

it("blocks confirmation when the task revision changes after a manual source choice", async () => {
  const view = render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const choice = await screen.findByRole("checkbox", { name: "gofile.io" });
  await waitFor(() => expect(choice).toBeEnabled());
  fireEvent.click(choice);
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeEnabled());
  view.rerender(<TaskPreparationModal detail={{ ...detail, task: { ...detail.task, planRevision: 8 } }} busy={false} onAction={action} onClose={onClose} />);
  expect(screen.getByRole("alert")).toHaveTextContent("PLAN_VERSION_CONFLICT");
  expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeDisabled();
  expect(mocks.confirmPreparation).not.toHaveBeenCalled();
});

it("pins even untouched preview choices to the revision that produced them", async () => {
  const view = render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeEnabled());
  const requests = mocks.previewPreparation.mock.calls.length;
  view.rerender(<TaskPreparationModal detail={{ ...detail, task: { ...detail.task, planRevision: 8 } }} busy={false} onAction={action} onClose={onClose} />);
  expect(screen.getByRole("alert")).toHaveTextContent("PLAN_VERSION_CONFLICT");
  expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeDisabled();
  expect(mocks.previewPreparation).toHaveBeenCalledTimes(requests);
});

it("cannot save an editor opened before an external revision change", async () => {
  const view = render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  await waitFor(() => expect(screen.getAllByRole("button", { name: "acquisition.preparation.editSource" })[0]).toBeEnabled());
  fireEvent.click(screen.getAllByRole("button", { name: "acquisition.preparation.editSource" })[0]);
  view.rerender(<TaskPreparationModal detail={{ ...detail, task: { ...detail.task, planRevision: 8 } }} busy={false} onAction={action} onClose={onClose} />);
  const editor = within(screen.getByRole("dialog", { name: "acquisition.preparation.sourceDetails" }));
  expect(editor.getByRole("button", { name: "common.action.save" })).toBeDisabled();
  expect(editor.getByRole("alert")).toHaveTextContent("PLAN_VERSION_CONFLICT");
});

it.each([
  ["share", "https://pixeldrain.com/u/changed", true],
  ["share", sources[0].url, false],
  ["remote", "/another/remote/path", false],
])("reclassifies URL edits while retaining unchanged methods and remote locator semantics (%s, %s)", async (kind, editedUrl, reset) => {
  const original = { ...sources[0], kind, providerId: "gofile", methodId: "gofile-cli", accountId: "selected-account" };
  mocks.previewPreparation.mockImplementation((id, request) => mockPreview(id, { ...request, sources: request.sources ?? [original] }));
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const edit = await screen.findByRole("button", { name: "acquisition.preparation.editSource" });
  await waitFor(() => expect(edit).toBeEnabled());
  fireEvent.click(edit);
  const editor = within(screen.getByRole("dialog", { name: "acquisition.preparation.sourceDetails" }));
  fireEvent.change(editor.getByRole("textbox", { name: "acquisition.preparation.url" }), { target: { value: editedUrl } });
  fireEvent.click(editor.getByRole("button", { name: "common.action.save" }));
  await waitFor(() => expect(mocks.previewPreparation).toHaveBeenLastCalledWith(600, expect.objectContaining({ sources: [expect.objectContaining({
    id: "a", url: editedUrl, kind: reset ? "auto" : kind, providerId: reset ? undefined : "gofile",
    methodId: reset ? undefined : "gofile-cli", accountId: reset ? undefined : "selected-account",
    archivePassword: original.archivePassword, processing: original.processing,
  })] }), expect.any(AbortSignal)));
});

it("does not submit a synthetic preview group ID when adding to an ungrouped source", async () => {
  mocks.previewPreparation.mockImplementation(async (id, request) => ({ ...await mockPreview(id, request), requiresRelationshipChoice: false }));
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const add = await screen.findByRole("button", { name: "acquisition.draft.addSource" });
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.draft.addSource" })).toBeEnabled());
  fireEvent.click(screen.getByRole("button", { name: "acquisition.draft.addSource" }));
  const editor = within(screen.getByRole("dialog", { name: "acquisition.preparation.sourceDetails" }));
  fireEvent.change(editor.getByRole("textbox", { name: "acquisition.preparation.url" }), { target: { value: "https://example.net/new" } });
  fireEvent.click(editor.getByRole("button", { name: "common.action.save" }));
  await waitFor(() => expect(mocks.previewPreparation).toHaveBeenLastCalledWith(600, expect.objectContaining({ sources: expect.arrayContaining([
    expect.objectContaining({ url: "https://example.net/new", groupId: undefined }),
  ]) }), expect.any(AbortSignal)));
});

it("shows one selection requirement and disables confirmation until an invalid source is explicitly selected", async () => {
  mocks.previewPreparation.mockImplementation(async (id, request) => {
    const preview = await mockPreview(id, request);
    return { ...preview, canConfirm: false, groups: preview.groups.map(group => ({ ...group, selectedSourceIds: [],
      sources: group.sources.map((source: ReturnType<typeof viewOf>) => ({ ...source, canSelect: false, availability: { status: "confirmedUnavailable" } })),
    })), requirements: [
      { code: "SOURCE_SELECTION_REQUIRED", kind: "source", message: "Choose one per group" },
      { code: "SOURCE_SELECTION_REQUIRED", kind: "source", message: "Choose at least one" },
    ] };
  });
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  expect(await screen.findByText("acquire.requirement.SOURCE_SELECTION_REQUIRED")).toBeVisible();
  expect(screen.getAllByText("acquire.requirement.SOURCE_SELECTION_REQUIRED")).toHaveLength(1);
  await waitFor(() => expect(screen.getAllByRole("checkbox").every(checkbox => !(checkbox as HTMLInputElement).disabled)).toBe(true));
  expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeDisabled();
});

it.each(["cn", "en"] as const)("provides %s text for every new preparation requirement and GoFile missing-share evidence", language => {
  const acquire: Record<string, string> = language === "cn" ? cnAcquire : enAcquire;
  const acquisition: Record<string, string> = language === "cn" ? cnAcquisition : enAcquisition;
  for (const code of ["SOURCE_SELECTION_REQUIRED", "SOURCE_RELATIONSHIP_REQUIRED", "DOWNLOAD_INFO_MISSING", "SOURCE_PROCESSING_INVALID", "SOURCE_REPLACEMENT_LOCAL_FILES"]) {
    expect(acquire[`acquire.requirement.${code}`]).toBeTruthy();
    expect(acquire[`acquire.requirement.${code}`]).not.toContain(code);
  }
  expect(acquisition["sourceAvailability.reason.error-notFound"]).toBeTruthy();
  expect(acquisition["sourceAvailability.reason.error-notFound"]).not.toContain("error-notFound");
});

it("retains every parsed source card and omits the post image chooser from preparation and source editing", async () => {
  const view = render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  await waitFor(() => expect(screen.getByRole("checkbox", { name: "gofile.io" })).toBeEnabled());
  expect(screen.getAllByRole("article")).toHaveLength(sources.length);
  for (const source of sources) expect(screen.getByText(source.url)).toBeVisible();
  expect(screen.queryByRole("button", { name: "choose images from post" })).toBeNull();
  fireEvent.click(screen.getAllByRole("button", { name: "acquisition.preparation.editSource" })[0]);
  expect(screen.queryByRole("button", { name: "choose images from post" })).toBeNull();
  const editor = within(screen.getByRole("dialog", { name: "acquisition.preparation.sourceDetails" }));
  fireEvent.change(editor.getByRole("textbox", { name: "acquisition.draft.accessCode" }), { target: { value: "updated" } });
  fireEvent.click(editor.getByRole("button", { name: "common.action.save" }));
  await waitFor(() => expect(mocks.previewPreparation).toHaveBeenLastCalledWith(600, expect.objectContaining({
    sources: expect.arrayContaining(sources.map(source => expect.objectContaining({ id: source.id, url: source.url }))),
  }), expect.any(AbortSignal)));
  expect(screen.getAllByRole("article")).toHaveLength(sources.length);
  view.unmount();
  render(<TaskPreparationModal detail={{ ...detail, draftSources: [] }} busy={false} onAction={action} onClose={onClose} />);
  expect(screen.getByText("title search")).toBeVisible();
  expect(screen.queryByRole("button", { name: "choose images from post" })).toBeNull();
});

it("allows explicit invalid-source selection without enabling unsupported sources or hiding the invalid state", async () => {
  mocks.previewPreparation.mockImplementation(async (id, request) => {
    const result = await mockPreview(id, request);
    return { ...result, groups: result.groups.map(group => ({ ...group, sources: [...group.sources, {
      ...viewOf({ id: "unsupported", kind: "auto", url: "https://unsupported.example/file" }),
      canSelect: false, canSelectManually: false, availability: { status: "confirmedUnavailable" },
      requirements: [{ code: "SOURCE_UNSUPPORTED", message: "Unsupported", kind: "source" }],
    }] })) };
  });
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  await waitFor(() => expect(screen.getByRole("checkbox", { name: "example.com" })).toBeEnabled());
  expect(screen.getByRole("checkbox", { name: "unsupported.example" })).toBeDisabled();
  expect(screen.getByRole("checkbox", { name: "example.com" })).not.toBeChecked();
  fireEvent.click(screen.getByRole("checkbox", { name: "example.com" }));
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeEnabled());
  const invalid = screen.getByRole("checkbox", { name: "example.com" }).closest("article")!;
  expect(invalid).toHaveClass("bg-default-100");
  expect(within(invalid).getByText("confirmedUnavailable")).toBeVisible();
  expect(within(invalid).getByText("acquisition.preparation.unavailableSelected")).toBeVisible();
  expect(screen.getByRole("checkbox", { name: "pixeldrain.com" })).not.toBeChecked();
  expect(screen.getAllByRole("article")).toHaveLength(4);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.preparation.confirm" }));
  await waitFor(() => expect(mocks.confirmPreparation).toHaveBeenCalledWith(600, expect.objectContaining({
    selectedSourceIds: ["invalid"], allowUnavailableSourceIds: ["invalid"], sources: undefined,
  })));
});

it.each(["unselect", "otherMirror", "editCredentials"])("clears a manual unavailable-source override after %s", async change => {
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  await waitFor(() => expect(screen.getByRole("checkbox", { name: "example.com" })).toBeEnabled());
  fireEvent.click(screen.getByRole("checkbox", { name: "example.com" }));
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeEnabled());
  if (change === "editCredentials") {
    const invalid = screen.getByRole("checkbox", { name: "example.com" }).closest("article")!;
    fireEvent.click(within(invalid).getByRole("button", { name: "acquisition.preparation.editSource" }));
    const editor = within(screen.getByRole("dialog", { name: "acquisition.preparation.sourceDetails" }));
    fireEvent.change(editor.getByRole("textbox", { name: "acquisition.draft.accessCode" }), { target: { value: "updated-code" } });
    fireEvent.click(editor.getByRole("button", { name: "common.action.save" }));
  } else {
    fireEvent.click(screen.getByRole("checkbox", { name: change === "unselect" ? "example.com" : "gofile.io" }));
  }
  await waitFor(() => expect(mocks.previewPreparation).toHaveBeenLastCalledWith(600, expect.objectContaining({
    allowUnavailableSourceIds: [], selectedSourceIds: change === "otherMirror" ? ["a"] : [],
  }), expect.any(AbortSignal)));
  expect(screen.queryByText("acquisition.preparation.unavailableSelected")).toBeNull();
});

it("opens the exact source link for verification without selecting it or granting an override", async () => {
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const link = await screen.findByText(sources[2].url);
  await waitFor(() => expect(screen.getByRole("checkbox", { name: "example.com" })).toBeEnabled());
  expect(link).toHaveAttribute("href", sources[2].url);
  fireEvent.click(link);
  expect(mocks.openLink).toHaveBeenCalledExactlyOnceWith(sources[2].url);
  expect(screen.getByRole("checkbox", { name: "example.com" })).not.toBeChecked();
  expect(screen.getByRole("checkbox", { name: "pixeldrain.com" })).toBeChecked();
  expect(mocks.confirmPreparation).not.toHaveBeenCalled();
  expect(mocks.previewPreparation.mock.lastCall?.[1].allowUnavailableSourceIds).toEqual([]);
});

it("names a resolved Weiyun share without changing the original link or offering account setup", async () => {
  const originalUrl = "https://links.example/saved-share";
  const resolvedUrl = "https://share.weiyun.com/fixture";
  const requirement = {
    code: "WEIYUN_PUBLIC_SHARE_UNSUPPORTED",
    category: "source",
    message: "Recorded provider capability",
  };
  mocks.previewPreparation.mockImplementation(async (id, request) => {
    const result = await mockPreview(id, { ...request, selectedSourceIds: [] });
    return {
      ...result,
      canConfirm: false,
      groups: result.groups.map(group => ({
        ...group,
        sources: [{
          ...viewOf({ id: "weiyun-share", kind: "auto", url: originalUrl, providerId: "unknown" }),
          resolvedSource: { id: "weiyun-share", kind: "share", providerId: "weiyun", originalUrl, url: resolvedUrl },
          canSelect: false,
          canSelectManually: false,
          methods: [],
          requirements: [requirement],
        }],
      })),
    };
  });
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  const card = within(await screen.findByRole("article"));
  await waitFor(() => expect(card.getByRole("button", { name: "acquisition.preparation.editSource" })).toBeEnabled());
  expect(card.getByRole("checkbox", { name: "weiyun" })).toBeDisabled();
  expect(card.getByText("acquire.requirement.WEIYUN_PUBLIC_SHARE_UNSUPPORTED")).toBeVisible();
  expect(card.queryByRole("button", { name: /account|token|login/i })).toBeNull();
  const link = card.getByRole("link", { name: "acquisition.preparation.openSource" });
  expect(link).toHaveAttribute("href", originalUrl);
  fireEvent.click(link);
  expect(mocks.openLink).toHaveBeenCalledExactlyOnceWith(originalUrl);
  expect(mocks.confirmPreparation).not.toHaveBeenCalled();
});

it.each([
  { kind: "remote", url: "https://example.com/remote-locator" },
  { kind: "remote", url: "/account/folder" },
  { kind: "magnet", url: "magnet:?xt=urn:btih:example" },
  { kind: "auto", url: "javascript:alert(1)" },
])("does not make a remote or non-HTTP locator a verification link ($kind, $url)", async source => {
  mocks.previewPreparation.mockImplementation((id, request) => mockPreview(id, { ...request,
    sources: [{ ...source, id: "non-web" }], selectedSourceIds: [],
  }));
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  await screen.findByRole("article");
  expect(screen.queryByRole("link")).toBeNull();
  expect(mocks.openLink).not.toHaveBeenCalled();
});

it.each(["method", "account"])("revokes the explicit unavailable-source choice after changing the download %s", async change => {
  mocks.previewPreparation.mockImplementation(async (id, request) => {
    const result = await mockPreview(id, request);
    return { ...result, groups: result.groups.map(group => ({ ...group, sources: group.sources.map((item: ReturnType<typeof viewOf>) => item.candidate.id === "invalid" ? {
      ...item, selectedMethodId: "first", methods: ["first", "second"].map(methodId => ({ id: methodId, displayName: methodId, supported: true, ready: true })),
    } : item) })) };
  });
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  await waitFor(() => expect(screen.getByRole("checkbox", { name: "example.com" })).toBeEnabled());
  fireEvent.click(screen.getByRole("checkbox", { name: "example.com" }));
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeEnabled());
  if (change === "method") fireEvent.change(screen.getByRole("combobox", { name: "acquire.method.label" }), { target: { value: "second" } });
  else fireEvent.click(screen.getByRole("button", { name: "change source account" }));
  await waitFor(() => expect(mocks.previewPreparation).toHaveBeenLastCalledWith(600, expect.objectContaining({
    allowUnavailableSourceIds: [], selectedSourceIds: [], sources: expect.arrayContaining([
      expect.objectContaining({ id: "invalid", [change === "method" ? "methodId" : "accountId"]: change === "method" ? "second" : "another-account" }),
    ]),
  }), expect.any(AbortSignal)));
});

it.each([false, true])("recomputes untouched defaults after priority changes while retaining manual invalid selection (%s)", async manual => {
  let recommended = "b";
  mocks.previewPreparation.mockImplementation(async (id, request) => {
    const result = await mockPreview(id, { ...request, selectedSourceIds: request.selectedSourceIds ?? [recommended] });
    return { ...result, groups: result.groups.map(group => ({ ...group, recommendedSourceIds: [recommended] })) };
  });
  render(<TaskPreparationModal detail={detail} busy={false} onAction={action} onClose={onClose} />);
  await waitFor(() => expect(screen.getByRole("checkbox", { name: "example.com" })).toBeEnabled());
  if (manual) {
    fireEvent.click(screen.getByRole("checkbox", { name: "example.com" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.preparation.confirm" })).toBeEnabled());
  }
  fireEvent.click(screen.getByRole("button", { name: "sourceAvailability.priority.title" }));
  const preferences = mocks.createPortal.mock.lastCall?.[1];
  expect(preferences.onDestroyed).toBeTypeOf("function");
  recommended = "a";
  act(() => preferences.onDestroyed());
  await waitFor(() => expect(mocks.previewPreparation).toHaveBeenLastCalledWith(600, expect.objectContaining({
    selectedSourceIds: [manual ? "invalid" : "a"], allowUnavailableSourceIds: manual ? ["invalid"] : [],
  }), expect.any(AbortSignal)));
  expect(screen.getByRole("checkbox", { name: manual ? "example.com" : "gofile.io" })).toBeChecked();
  expect(screen.getByRole("checkbox", { name: "pixeldrain.com" })).not.toBeChecked();
});

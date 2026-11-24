import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Children, cloneElement, useState } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import AcquisitionPage from "..";
import { acquisitionListPreferencesKey } from "../listPreferences";
import { AcquisitionStatus as Status } from "@/sdk/constants";
import LegacyTaskDetailModal from "../components/LegacyTaskDetailModal";
import AcquisitionSettingsModal from "../components/AcquisitionSettingsModal";
import DeleteAcquisitionTasksModal from "../components/DeleteAcquisitionTasksModal";
import AcquisitionStorageMaintenanceModal from "../components/AcquisitionStorageMaintenanceModal";
import AcquisitionContinueModal from "../components/AcquisitionContinueModal";
import AcquisitionDownloadDestinationModal from "../components/AcquisitionDownloadDestinationModal";
import SourceAvailabilityReviewModal from "@/components/SourceAvailability/SourceAvailabilityReviewModal";
const mocks = vi.hoisted(() => ({
  search: vi.fn(),
  legacySearch: vi.fn(),
  getOptions: vi.fn(),
  setUp: vi.fn(),
  get: vi.fn(),
  recipes: vi.fn(),
  portal: vi.fn(),
  runtime: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: {
      searchAcquisitionTasks: mocks.search,
      getAcquisitionOptions: mocks.getOptions,
      setUpAcquisition: mocks.setUp,
      searchAcquisitions: mocks.legacySearch,
      getAcquisition: mocks.get,
      getAcquisitionRecipes: mocks.recipes,
      getAcquisitionRuntimeOverviews: mocks.runtime,
    },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.portal }),
}));
vi.mock("@/components/HelpCenter", () => ({ HelpCenterButton: () => null }));
vi.mock("@/components/Workflow/WorkflowIntegrationHint", () => ({ default: () => null }));
vi.mock("../components/InboxDrawer", () => ({ default: () => null }));
vi.mock("../components/AcquisitionSettingsModal", () => ({ default: () => null }));
vi.mock("../components/DeleteAcquisitionTasksModal", () => ({ default: () => null }));
vi.mock("../components/AcquisitionStorageMaintenanceModal", () => ({ default: () => null }));
vi.mock("../components/AcquisitionContinueModal", () => ({ default: () => null }));
vi.mock("../components/AcquisitionDownloadDestinationModal", () => ({ default: () => null }));
vi.mock("../components/RecipeCatalog", () => ({ default: () => null }));
vi.mock("../components/StartAcquisitionModal", () => ({ default: () => null }));
vi.mock("../components/LegacyTaskDetailModal", () => ({ default: () => null }));
vi.mock("@/components/SourceAvailability/SourceAvailabilityReviewModal", () => ({
  default: () => null,
}));
vi.mock("@/components/Acquire/PlanTask/PlanTaskDetailModal", () => ({ default: () => null }));
vi.mock("@/components/Acquire/PlanTask/PlanTaskRow", () => ({
  default: ({ task, onDelete }: any) => (
    <div>
      <p>{task.title}</p>
      <button onClick={onDelete}>delete row #{task.id}</button>
    </div>
  ),
}));
vi.mock("../components/AcquisitionRow", () => ({
  default: ({ task, onDelete }: any) => (
    <div>
      <p>{task.resourceName}</p>
      <button onClick={onDelete}>delete row #{task.id}</button>
    </div>
  ),
}));
vi.mock("@heroui/react", () => ({
  Drawer: () => null,
  DrawerBody: () => null,
  DrawerContent: () => null,
  DrawerHeader: () => null,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isDisabled,
    isLoading,
    startContent,
    endContent,
    size,
    color,
    variant,
    ...props
  }: any) => (
    <button {...props} disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Tooltip: ({ children, content }: any) => <div data-tooltip={content}>{children}</div>,
  Badge: ({ children, content, isInvisible }: any) => (
    <span>
      {children}
      {!isInvisible && <span data-testid="queue-count">{content}</span>}
    </span>
  ),
  Checkbox: ({
    children,
    isSelected,
    isDisabled,
    isIndeterminate,
    onValueChange,
    ...props
  }: any) => (
    <label>
      <input
        type="checkbox"
        aria-label={props["aria-label"]}
        checked={isSelected}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Select: ({ selectedKeys, dataSource, onSelectionChange, ...props }: any) => (
    <select
      aria-label={props["aria-label"]}
      value={Array.from(selectedKeys)[0] as string}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Autocomplete: ({
    inputValue,
    onInputChange,
    onKeyDown,
    inputProps,
    items,
    children,
    onSelectionChange,
    ...props
  }: any) => (
    <div>
      <input
        aria-label={props["aria-label"]}
        value={inputValue}
        onChange={(event) => onInputChange(event.target.value)}
        onKeyDown={onKeyDown}
        onKeyDownCapture={inputProps?.onKeyDownCapture}
      />
      {items.map((item: any) => (
        <button key={item.id} onClick={() => onSelectionChange(String(item.id))}>
          {children(item)}
        </button>
      ))}
    </div>
  ),
  AutocompleteItem: ({ children }: any) => <span>{children}</span>,
  Spinner: () => <span>loading</span>,
  Input: ({ label, value, onValueChange, ...props }: any) => (
    <input
      aria-label={props["aria-label"] ?? label}
      value={value}
      onChange={(e) => onValueChange(e.target.value)}
    />
  ),
  Dropdown: ({ children }: any) => {
    const [open, setOpen] = useState(false);
    const items = Children.toArray(children) as any[];
    return (
      <div>
        {cloneElement(items[0], { onOpen: () => setOpen(!open) })}
        {open && items[1]}
      </div>
    );
  },
  DropdownTrigger: ({ children, onOpen }: any) => cloneElement(children, { onPress: onOpen }),
  DropdownMenu: ({ children, disabledKeys = [], onAction }: any) => (
    <div role="menu">
      {Children.map(children, (child: any) =>
        cloneElement(child, {
          onPress: () => onAction(child.key),
          isDisabled: disabledKeys.includes(child.key),
        }),
      )}
    </div>
  ),
  DropdownItem: ({ children, onPress, isDisabled }: any) => (
    <button role="menuitem" disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Pagination: ({ page, total, onChange }: any) => (
    <button onClick={() => onChange(page + 1)}>
      page {page}/{total}
    </button>
  ),
}));
const counts = { all: 60, preparing: 3, ready: 2, running: 20, attention: 30, completed: 5 };
const tags = {
  sourceUnavailable: 20,
  localStorage: 4,
  accessRequired: 3,
  networkIssue: 5,
  dependencyMissing: 1,
  downloadInfoMissing: 1,
  downloadSourceUnsupported: 2,
  sourceSelectionRequired: 1,
  configurationRequired: 0,
  fileProcessing: 1,
  other: 0,
};
const response = (overrides: any = {}) => ({
  code: 0,
  data: {
    items: [
      {
        id: 60,
        title: "Current page task",
        builderId: "post",
        status: Status.Waiting,
        workflowRunId: 160,
        attentionTags: ["sourceUnavailable"],
      },
    ],
    totalCount: 60,
    totalTasks: 60,
    page: 1,
    pageSize: 24,
    stageCounts: counts,
    downloadSourceCounts: { all: 60, http: 24, bt: 2, "site:14": 20 },
    attentionCounts: tags,
    factCounts: { downloadedPending: 12, spaceInsufficient: 8 },
    taskFacts: {},
    filteredTaskIds: [60, 59, 58],
    checkableTaskIds: [60, 59, 58],
    taskIdsTruncated: false,
    ...overrides,
  },
});
const show = (url = "/acquisitions") =>
  render(
    <MemoryRouter initialEntries={[url]}>
      <AcquisitionPage />
    </MemoryRouter>,
  );
const stageButton = (stage: string, count: number) =>
  screen.getByRole("button", { name: `acquisition.stage.${stage} ${count}` });
const actions = () => screen.getByRole("button", { name: "acquisition.continueEntry.open" });
const selectAll = () => {
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.selection.page" }));
  const crossPage = screen.queryByRole("button", { name: "acquisition.selection.allFiltered" });
  if (crossPage) fireEvent.click(crossPage);
};
const reviewSources = () => {
  selectAll();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.actionbar.checkSources" }));
};

it.each([true, false])(
  "uses the same deletion confirmation for one row (planned=%s), independent of bulk selection",
  async (planned) => {
    mocks.search.mockResolvedValue(
      response({
        items: [
          {
            id: 60,
            title: "Current page task",
            resourceName: "Legacy task",
            builderId: planned ? "post" : undefined,
            status: Status.Waiting,
          },
        ],
      }),
    );
    show();
    fireEvent.click(await screen.findByRole("button", { name: "delete row #60" }));
    expect(mocks.portal).toHaveBeenLastCalledWith(
      DeleteAcquisitionTasksModal,
      expect.objectContaining({ taskIds: [60], onChanged: expect.any(Function) }),
    );
  },
);
beforeEach(() => {
  localStorage.clear();
  vi.clearAllMocks();
  mocks.search.mockReset().mockResolvedValue(response());
  mocks.getOptions
    .mockReset()
    .mockResolvedValue({
      code: 0,
      data: { useEmergencySpaceReserve: false, minimumFreeSpaceBytes: 50 * 1024 ** 3 },
    });
  mocks.setUp.mockReset();
  mocks.get.mockReset();
  mocks.recipes.mockReset().mockResolvedValue({ code: 0, data: [] });
  mocks.runtime.mockReset().mockResolvedValue({ code: 0, data: [] });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

it("restores search, filters and sorting without restoring selected tasks or the saved page", async () => {
  localStorage.setItem(
    acquisitionListPreferencesKey(),
    JSON.stringify({
      version: 1,
      stage: "attention",
      keyword: "saved name",
      sort: "sizeAsc",
      attentionTags: ["localStorage"],
      facts: ["downloadedPending"],
      page: 9,
      selectedTaskIds: [60],
    }),
  );
  show();
  await waitFor(() =>
    expect(mocks.search).toHaveBeenCalledWith(
      expect.objectContaining({
        keyword: "saved name",
        stage: "attention",
        sort: "sizeAsc",
        attentionTags: ["localStorage"],
        facts: ["downloadedPending"],
        page: 1,
      }),
      expect.anything(),
    ),
  );
  expect(screen.getByRole("combobox", { name: "acquisition.sort.label" })).toHaveValue("sizeAsc");
  expect(
    screen.queryAllByRole("checkbox").every((input) => !(input as HTMLInputElement).checked),
  ).toBe(true);
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.sort.label" }), {
    target: { value: "sizeDesc" },
  });
  await waitFor(() =>
    expect(JSON.parse(localStorage.getItem(acquisitionListPreferencesKey())!)).toEqual({
      version: 1,
      stage: "attention",
      keyword: "saved name",
      sort: "sizeDesc",
      attentionTags: ["localStorage"],
      facts: ["downloadedPending"],
    }),
  );
});

it("shows unfinished queue task counts from the existing search response, independently of the current list count", async () => {
  mocks.search.mockResolvedValue(response({ totalCount: 2, queueTaskCount: 17 }));
  show();
  expect(await screen.findByTestId("queue-count")).toHaveTextContent("17");
  const queue = screen.getByRole("button", { name: "acquisition.continueEntry.queue" });
  expect(queue.closest("[data-tooltip]")).toHaveAttribute(
    "data-tooltip",
    "acquisition.continueEntry.queueCountHint",
  );
  fireEvent.click(queue);
  expect(mocks.portal).toHaveBeenCalledWith(
    AcquisitionContinueModal,
    expect.objectContaining({ onChanged: expect.any(Function) }),
  );
});

it("keeps the queue entry usable without a badge before the server supplies a count", async () => {
  show();
  await screen.findByRole("button", { name: "acquisition.continueEntry.queue" });
  expect(screen.queryByTestId("queue-count")).toBeNull();
});

it("honors the explicit URL stage and taskId when restoring browser preferences", async () => {
  localStorage.setItem(
    acquisitionListPreferencesKey(),
    JSON.stringify({
      version: 1,
      stage: "attention",
      keyword: "previous search",
      sort: "sizeAsc",
      attentionTags: ["localStorage"],
      facts: ["spaceInsufficient"],
    }),
  );
  mocks.get.mockResolvedValue({ code: 0, data: { id: 987, builderId: "post" } });
  show("/acquisitions?stage=completed&taskId=987");
  await waitFor(() =>
    expect(mocks.search).toHaveBeenCalledWith(
      expect.objectContaining({
        stage: "completed",
        keyword: "previous search",
        attentionTags: [],
        facts: [],
        sort: "sizeAsc",
      }),
      expect.anything(),
    ),
  );
  await waitFor(() =>
    expect(mocks.portal).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({ taskId: 987 }),
    ),
  );
});

it("keeps waiting workflow rows in the server's running facet when their downloads are active", async () => {
  mocks.search.mockResolvedValue(
    response({
      items: [
        {
          id: 634,
          title: "Transferring original",
          builderId: "post",
          workflowRunId: 3246,
          status: Status.Waiting,
          stage: "running",
          downloadActivity: "running",
        },
        {
          id: 635,
          title: "Queued original",
          builderId: "post",
          workflowRunId: 3247,
          status: Status.Waiting,
          stage: "running",
          downloadActivity: "queued",
        },
      ],
      totalCount: 2,
      stageCounts: { ...counts, running: 2 },
      filteredTaskIds: [634, 635],
    }),
  );
  show("/acquisitions?stage=running");
  await screen.findByText("Transferring original");
  expect(screen.getByText("Queued original")).toBeVisible();
  expect(stageButton("running", 2)).toBeVisible();
  expect(mocks.search).toHaveBeenLastCalledWith(
    expect.objectContaining({ stage: "running" }),
    expect.any(Object),
  );
});

it("uses server pagination and one acquisition settings entry without cancelled or import controls", async () => {
  show();
  await screen.findByText("Current page task");
  expect(mocks.search).toHaveBeenCalledWith(
    {
      keyword: "",
      stage: "all",
      attentionTags: [],
      facts: [],
      page: 1,
      pageSize: 24,
      sort: "newest",
    },
    expect.any(Object),
  );
  expect(mocks.legacySearch).not.toHaveBeenCalled();
  for (const key of [
    "acquisition.stage.cancelled",
    "acquisition.legacyImport.title",
    "acquisition.sharedList.title",
    "sourceAvailability.priority.title",
  ])
    expect(screen.queryByText(key)).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.settings.title" }));
  expect(mocks.portal).toHaveBeenCalledWith(
    AcquisitionSettingsModal,
    expect.objectContaining({ onDone: expect.any(Function) }),
  );
  mocks.search.mockResolvedValue(
    response({
      page: 2,
      items: [{ id: 36, title: "Second page", builderId: "post", status: Status.Running }],
    }),
  );
  fireEvent.click(screen.getByText("page 1/3"));
  await screen.findByText("Second page");
  expect(mocks.search).toHaveBeenLastCalledWith(
    expect.objectContaining({ page: 2, pageSize: 24 }),
    expect.any(Object),
  );
  expect(mocks.runtime).toHaveBeenLastCalledWith({ taskIds: [36] }, expect.any(Object));
});

it("resets pagination and refreshes batch IDs when the size order changes", async () => {
  show();
  await screen.findByText("Current page task");
  mocks.search.mockResolvedValue(response({ page: 2 }));
  fireEvent.click(screen.getByText("page 1/3"));
  await screen.findByText("page 2/3");
  let finish!: (value: unknown) => void;
  mocks.search.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.sort.label" }), {
    target: { value: "sizeAsc" },
  });
  expect(actions()).toBeDisabled();
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ sort: "sizeAsc", page: 1 }),
      expect.any(Object),
    ),
  );
  await act(async () => finish(response({ filteredTaskIds: [9, 8], checkableTaskIds: [9, 8] })));
  reviewSources();
  expect(mocks.portal).toHaveBeenLastCalledWith(
    SourceAvailabilityReviewModal,
    expect.objectContaining({ taskIds: [9, 8] }),
  );
  mocks.search.mockResolvedValue(response());
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.sort.label" }), {
    target: { value: "sizeDesc" },
  });
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ sort: "sizeDesc", page: 1 }),
      expect.any(Object),
    ),
  );
});

it("requests last processing order from the first page", async () => {
  show();
  await screen.findByText("Current page task");
  fireEvent.click(screen.getByText("page 1/3"));
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.sort.label" }), {
    target: { value: "lastProcessed" },
  });
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({
        sort: "lastProcessed",
        page: 1,
      }),
      expect.any(Object),
    ),
  );
});

it("keeps explicit task selection across pages and submits only selected IDs for local processing", async () => {
  mocks.search.mockResolvedValue(response({ filteredTaskIds: [60, 36] }));
  show();
  await screen.findByText("Current page task");
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.selection.task" }));
  mocks.search.mockResolvedValue(
    response({
      page: 2,
      filteredTaskIds: [60, 36],
      items: [{ id: 36, title: "Second selected task", builderId: "post", status: Status.Waiting }],
    }),
  );
  fireEvent.click(screen.getByText("page 1/3"));
  await screen.findByText("Second selected task");
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.selection.task" }));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueEntry.open" }));
  expect(mocks.portal).toHaveBeenLastCalledWith(
    AcquisitionContinueModal,
    expect.objectContaining({ taskIds: [60, 36], onChanged: expect.any(Function) }),
  );
  expect(screen.getByRole("checkbox", { name: "acquisition.selection.page" })).toBeChecked();
});

it("preserves explicit selections when sorting but clears them when the filter scope changes", async () => {
  show();
  await screen.findByText("Current page task");
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.selection.page" }));
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.sort.label" }), {
    target: { value: "sizeAsc" },
  });
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "acquisition.continueEntry.open" })).toBeEnabled(),
  );
  expect(screen.getByRole("checkbox", { name: "acquisition.selection.task" })).toBeChecked();
  fireEvent.click(stageButton("attention", 30));
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "acquisition.continueEntry.open" })).toBeDisabled(),
  );
  expect(screen.getByRole("checkbox", { name: "acquisition.selection.task" })).not.toBeChecked();
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.selection.task" }));
  fireEvent.change(screen.getByLabelText("acquisition.tasks.search"), {
    target: { value: "another" },
  });
  expect(screen.getByRole("button", { name: "acquisition.continueEntry.open" })).toBeDisabled();
});

it("never selects all filtered pages when selecting the current page and can reopen queues without a selection", async () => {
  show();
  await screen.findByText("Current page task");
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.selection.page" }));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueEntry.open" }));
  expect(mocks.portal).toHaveBeenLastCalledWith(
    AcquisitionContinueModal,
    expect.objectContaining({ taskIds: [60] }),
  );
  fireEvent.click(screen.getByRole("button", { name: "acquisition.selection.clear" }));
  expect(screen.getByRole("button", { name: "acquisition.continueEntry.open" })).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueEntry.queue" }));
  expect(mocks.portal).toHaveBeenLastCalledWith(AcquisitionContinueModal, {
    onChanged: expect.any(Function),
  });
});

it("keeps every action explicitly scoped to selected tasks instead of silently using the filter", async () => {
  show();
  await screen.findByText("Current page task");
  expect(screen.queryByRole("button", { name: "acquisition.tasks.filteredActions" })).toBeNull();
  for (const label of [
    "acquisition.continueEntry.open",
    "acquisition.actionbar.checkSources",
    "acquisition.actionbar.downloadDestination",
    "acquisition.actionbar.storagePolicy",
    "acquisition.actionbar.workspaceCleanup",
    "acquisition.actionbar.delete",
  ])
    expect(screen.getByRole("button", { name: label })).toBeDisabled();
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.selection.task" }));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.actionbar.delete" }));
  expect(mocks.portal).toHaveBeenLastCalledWith(
    DeleteAcquisitionTasksModal,
    expect.objectContaining({ taskIds: [60] }),
  );
  fireEvent.click(screen.getByRole("button", { name: "acquisition.actionbar.checkSources" }));
  expect(mocks.portal).toHaveBeenLastCalledWith(
    SourceAvailabilityReviewModal,
    expect.objectContaining({ taskIds: [60] }),
  );
});

it("offers cross-page selection directly and can replace it with just the current page", async () => {
  show();
  await screen.findByText("Current page task");
  expect(screen.queryByRole("button", { name: "acquisition.selection.onlyPage" })).toBeNull();
  selectAll();
  expect(screen.getAllByRole("button", { name: "acquisition.selection.clear" })).toHaveLength(1);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.selection.onlyPage" }));
  expect(screen.queryByRole("button", { name: "acquisition.selection.onlyPage" })).toBeNull();
  fireEvent.click(actions());
  expect(mocks.portal).toHaveBeenLastCalledWith(
    AcquisitionContinueModal,
    expect.objectContaining({ taskIds: [60] }),
  );
});

it("does not show cross-page select when the filtered result fits on one page", async () => {
  mocks.search.mockResolvedValue(
    response({ totalCount: 1, totalTasks: 1, filteredTaskIds: [60], checkableTaskIds: [60] }),
  );
  show();
  await screen.findByText("Current page task");
  expect(screen.queryByRole("button", { name: "acquisition.selection.allFiltered" })).toBeNull();
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.selection.page" }));
  expect(screen.queryByRole("button", { name: "acquisition.selection.allFiltered" })).toBeNull();
});

it("supports selections beyond 100 and enforces each operation's own limit without truncation", async () => {
  const ids = Array.from({ length: 2001 }, (_, i) => 2001 - i);
  mocks.search.mockResolvedValue(
    response({ totalCount: ids.length, filteredTaskIds: ids, checkableTaskIds: ids }),
  );
  show();
  await screen.findByText("Current page task");
  selectAll();
  for (const key of [
    "acquisition.continueEntry.open",
    "acquisition.actionbar.downloadDestination",
    "acquisition.actionbar.checkSources",
  ]) {
    const button = screen.getByRole("button", { name: key });
    expect(button).toBeDisabled();
    expect(button.closest("[data-tooltip]")).toHaveAttribute(
      "data-tooltip",
      "acquisition.actionbar.limit",
    );
    fireEvent.click(button);
  }
  expect(mocks.portal).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.actionbar.delete" }));
  expect(mocks.portal).toHaveBeenLastCalledWith(
    DeleteAcquisitionTasksModal,
    expect.objectContaining({ taskIds: ids }),
  );
  fireEvent.click(screen.getByRole("button", { name: "acquisition.selection.firstByOrder" }));
  fireEvent.click(actions());
  expect(mocks.portal).toHaveBeenLastCalledWith(
    AcquisitionContinueModal,
    expect.objectContaining({ taskIds: ids.slice(0, 100) }),
  );
});

it("captures cross-page selection once instead of automatically adding later matches", async () => {
  vi.useFakeTimers();
  await act(async () => {
    show();
  });
  selectAll();
  mocks.search.mockResolvedValue(response({ filteredTaskIds: [60, 59, 58, 57] }));
  await act(async () => {
    vi.advanceTimersByTime(5000);
  });
  fireEvent.click(screen.getByRole("button", { name: "acquisition.actionbar.delete" }));
  expect(mocks.portal).toHaveBeenLastCalledWith(
    DeleteAcquisitionTasksModal,
    expect.objectContaining({ taskIds: [60, 59, 58] }),
  );
});

it("drops selections removed by a refreshed filter snapshot instead of retaining invisible targets", async () => {
  show();
  await screen.findByText("Current page task");
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.selection.task" }));
  mocks.search.mockResolvedValue(
    response({
      page: 2,
      filteredTaskIds: [36],
      items: [{ id: 36, title: "Remaining task", builderId: "post" }],
    }),
  );
  fireEvent.click(screen.getByText("page 1/3"));
  await screen.findByText("Remaining task");
  expect(screen.getByRole("button", { name: "acquisition.continueEntry.open" })).toBeDisabled();
});

it("uses server attention facets and combines filters without deriving tags from translated text", async () => {
  show();
  await screen.findByText("Current page task");
  expect(
    screen.queryByRole("button", { name: /acquisition\.attention\.tag\.planNeedsInput/ }),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "acquisition.attention.tag.downloadSourceUnsupported 2" }),
  ).toBeVisible();
  expect(
    screen.getByRole("button", { name: "acquisition.attention.tag.downloadInfoMissing 1" }),
  ).toBeVisible();
  fireEvent.click(stageButton("attention", 30));
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ stage: "attention", page: 1 }),
      expect.any(Object),
    ),
  );
  const tag = await screen.findByRole("button", {
    name: "acquisition.attention.tag.sourceUnavailable 20",
  });
  fireEvent.click(tag);
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ stage: "attention", attentionTags: ["sourceUnavailable"] }),
      expect.any(Object),
    ),
  );
  expect(
    await screen.findByRole("button", { name: "acquisition.attention.tag.sourceUnavailable 20" }),
  ).toHaveAttribute("aria-pressed", "true");
  fireEvent.change(screen.getByLabelText("acquisition.tasks.search"), {
    target: { value: "#969" },
  });
  expect(actions()).toBeDisabled();
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: "#969", attentionTags: ["sourceUnavailable"], page: 1 }),
      expect.any(Object),
    ),
  );
  fireEvent.click(
    await screen.findByRole("button", { name: "acquisition.attention.tag.sourceUnavailable 20" }),
  );
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ attentionTags: [] }),
      expect.any(Object),
    ),
  );
  expect(stageButton("all", 60)).toBeVisible();
});

it("filters local storage problems independently before diagnosing the fixed matching task IDs", async () => {
  show();
  await screen.findByText("Current page task");
  fireEvent.click(stageButton("attention", 30));
  fireEvent.click(
    await screen.findByRole("button", { name: "acquisition.attention.tag.localStorage 4" }),
  );
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ stage: "attention", attentionTags: ["localStorage"], page: 1 }),
      expect.any(Object),
    ),
  );
  expect(
    await screen.findByRole("button", { name: "acquisition.attention.tag.localStorage 4" }),
  ).toHaveAttribute("aria-pressed", "true");
  selectAll();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueEntry.open" }));
  expect(mocks.portal).toHaveBeenCalledWith(
    AcquisitionContinueModal,
    expect.objectContaining({
      taskIds: [60, 59, 58],
      scopeLabel: expect.stringContaining("acquisition.attention.tag.localStorage"),
    }),
  );
});

it("selects multiple reasons with AND semantics and clears conditions without changing the stage", async () => {
  show("/acquisitions?stage=attention");
  await screen.findByText("Current page task");
  fireEvent.click(
    screen.getByRole("button", { name: "acquisition.attention.tag.sourceUnavailable 20" }),
  );
  fireEvent.click(screen.getByRole("button", { name: "acquisition.attention.tag.networkIssue 5" }));
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({
        stage: "attention",
        attentionTags: ["networkIssue", "sourceUnavailable"],
        facts: [],
      }),
      expect.any(Object),
    ),
  );
  await screen.findByText("Current page task");
  expect(
    screen.getByRole("button", { name: "acquisition.attention.tag.sourceUnavailable 20" }),
  ).toHaveAttribute("aria-pressed", "true");
  expect(
    screen.getByRole("button", { name: "acquisition.attention.tag.networkIssue 5" }),
  ).toHaveAttribute("aria-pressed", "true");
  expect(screen.getByText("acquisition.filters.matchAll")).toBeVisible();
  selectAll();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.filters.clear" }));
  expect(actions()).toBeDisabled();
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ stage: "attention", attentionTags: [], facts: [], page: 1 }),
      expect.any(Object),
    ),
  );
  expect(screen.queryByRole("button", { name: "acquisition.filters.clear" })).toBeNull();
});

it("uses the server's full cross-page intersection for downloaded and space-blocked continuation", async () => {
  mocks.search.mockImplementation(async (query) =>
    response({
      page: query.page,
      totalCount: query.facts?.length === 2 ? 2 : 60,
      filteredTaskIds: query.facts?.length === 2 ? [72, 31] : [60, 59, 58],
      checkableTaskIds: [],
      // A task fact is display evidence, not an alternative source of matching IDs.
      taskFacts: { 60: ["downloadedPending", "spaceInsufficient"] },
    }),
  );
  show();
  await screen.findByText("Current page task");
  fireEvent.click(screen.getByText("page 1/3"));
  await screen.findByText("page 2/3");
  selectAll();
  const tags = within(screen.getByRole("group", { name: "acquisition.filters.title" }));
  expect(tags.queryByRole("group")).toBeNull();
  expect(
    tags.getByRole("button", { name: "acquisition.attention.tag.localStorage 4" }),
  ).toBeVisible();
  fireEvent.click(
    tags.getByRole("button", { name: "acquisition.filters.fact.spaceInsufficient 8" }),
  );
  expect(actions()).toBeDisabled();
  fireEvent.click(
    screen.getByRole("button", { name: "acquisition.filters.fact.downloadedPending 12" }),
  );
  fireEvent.click(screen.getByRole("button", { name: "acquisition.attention.tag.localStorage 4" }));
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({
        attentionTags: ["localStorage"],
        facts: ["downloadedPending", "spaceInsufficient"],
        page: 1,
      }),
      expect.any(Object),
    ),
  );
  await screen.findByText("Current page task");
  expect(screen.queryByText("page 2/3")).toBeNull();
  selectAll();
  fireEvent.click(actions());
  expect(mocks.portal).toHaveBeenLastCalledWith(
    AcquisitionContinueModal,
    expect.objectContaining({
      taskIds: [60],
      scopeLabel: expect.stringContaining("acquisition.filters.matchAll"),
    }),
  );
  const props = mocks.portal.mock.calls.at(-1)?.[1];
  expect(props.scopeLabel).toContain("acquisition.filters.fact.downloadedPending");
  expect(props.scopeLabel).toContain("acquisition.filters.fact.spaceInsufficient");
  expect(props.scopeLabel).toContain("acquisition.attention.tag.localStorage");
});

it("can add a condition while another is loading and aborts the old request without keeping old selections", async () => {
  show();
  await screen.findByText("Current page task");
  selectAll();
  let finishOld!: (value: unknown) => void;
  mocks.search.mockReturnValueOnce(
    new Promise((resolve) => {
      finishOld = resolve;
    }),
  );
  fireEvent.click(
    screen.getByRole("button", { name: "acquisition.filters.fact.downloadedPending 12" }),
  );
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ facts: ["downloadedPending"] }),
      expect.any(Object),
    ),
  );
  const first = mocks.search.mock.calls.at(-1)?.[1].signal;
  expect(actions()).toBeDisabled();
  const secondCondition = screen.getByRole("button", {
    name: "acquisition.filters.fact.spaceInsufficient 8",
  });
  expect(secondCondition).toBeEnabled();
  mocks.search.mockResolvedValueOnce(response({ filteredTaskIds: [42], checkableTaskIds: [] }));
  fireEvent.click(secondCondition);
  await screen.findByText("Current page task");
  expect(first.aborted).toBe(true);
  expect(actions()).toBeEnabled();
  fireEvent.click(actions());
  expect(mocks.portal).toHaveBeenLastCalledWith(
    AcquisitionContinueModal,
    expect.objectContaining({ taskIds: [42], filteredAnalysis: true }),
  );
  await act(async () => finishOld(response({ filteredTaskIds: [999], checkableTaskIds: [999] })));
  selectAll();
  fireEvent.click(actions());
  expect(mocks.portal).toHaveBeenLastCalledWith(
    AcquisitionContinueModal,
    expect.objectContaining({ taskIds: [42] }),
  );
});

it("captures all filtered checkable IDs for batch review, not only the visible page", async () => {
  show();
  await screen.findByText("Current page task");
  reviewSources();
  expect(mocks.portal).toHaveBeenCalledWith(
    SourceAvailabilityReviewModal,
    expect.objectContaining({ taskIds: [60, 59, 58] }),
  );
  expect(mocks.runtime).toHaveBeenCalledWith({ taskIds: [60] }, expect.any(Object));
});

it("exposes the batch LLM continuation action for the fixed selection", async () => {
  show();
  await screen.findByText("Current page task");
  selectAll();
  expect(screen.queryByRole("button", { name: "acquisition.adaptive.batchEntry" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueEntry.open" }));
  expect(mocks.portal).toHaveBeenLastCalledWith(
    expect.anything(),
    expect.objectContaining({ taskIds: [60, 59, 58], onChanged: expect.any(Function) }),
  );
});

it("opens destination revision for the entire fixed filter, not just visible download records", async () => {
  show();
  await screen.findByText("Current page task");
  selectAll();
  fireEvent.click(
    screen.getByRole("button", { name: "acquisition.actionbar.downloadDestination" }),
  );
  expect(mocks.portal).toHaveBeenCalledWith(
    AcquisitionDownloadDestinationModal,
    expect.objectContaining({ taskIds: [60, 59, 58], onChanged: expect.any(Function) }),
  );
});

it("captures every filtered page for delete confirmation and keeps sources limited to checkable IDs", async () => {
  const ids = [60, 59, 58, 57];
  mocks.search.mockResolvedValue(response({ filteredTaskIds: ids, checkableTaskIds: [60] }));
  show();
  await screen.findByText("Current page task");
  selectAll();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.actionbar.delete" }));
  expect(mocks.portal).toHaveBeenCalledWith(
    DeleteAcquisitionTasksModal,
    expect.objectContaining({
      taskIds: [60, 59, 58, 57],
      scopeLabel: expect.stringContaining("acquisition.stage.all"),
      onChanged: expect.any(Function),
    }),
  );
  const props = mocks.portal.mock.calls.at(-1)?.[1];
  ids.push(99);
  expect(props.taskIds).toEqual([60, 59, 58, 57]);
});

it("allows deletion preview without checkable sources and disables only source review", async () => {
  mocks.search.mockResolvedValue(response({ checkableTaskIds: [] }));
  show();
  await screen.findByText("Current page task");
  selectAll();
  expect(actions()).not.toBeDisabled();
  expect(screen.getByRole("button", { name: "acquisition.actionbar.checkSources" })).toBeDisabled();
  expect(screen.getByRole("button", { name: "acquisition.actionbar.delete" })).not.toBeDisabled();
});

it("refreshes source tags when the check of a selected draft finishes", async () => {
  const draft = {
    items: [{ id: 60, title: "Draft acquisition", builderId: "draft", status: Status.Waiting }],
    filteredTaskIds: [60],
    checkableTaskIds: [60],
    attentionCounts: { sourceUnavailable: 0 },
  };
  mocks.search.mockResolvedValue(response(draft));
  show();
  await screen.findByText("Draft acquisition");
  reviewSources();
  const [modal, props] = mocks.portal.mock.lastCall!;
  expect(modal).toBe(SourceAvailabilityReviewModal);
  expect(props.taskIds).toEqual([60]);

  mocks.search.mockResolvedValue(response({ ...draft, attentionCounts: { sourceUnavailable: 1 } }));
  await act(async () => props.onChecked());
  expect(await screen.findByRole("button", { name: "acquisition.attention.tag.sourceUnavailable 1" })).toBeVisible();
});

it("never calls a truncated ID list all results but permits explicitly selecting the visible page", async () => {
  mocks.search.mockResolvedValue(response({ taskIdsTruncated: true }));
  show();
  await screen.findByText("Current page task");
  expect(actions()).toBeDisabled();
  expect(screen.queryByRole("button", { name: "acquisition.selection.allFiltered" })).toBeNull();
  expect(screen.getAllByText("acquisition.selection.truncated").length).toBeGreaterThan(0);
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.selection.page" }));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.actionbar.delete" }));
  expect(mocks.portal).toHaveBeenLastCalledWith(
    DeleteAcquisitionTasksModal,
    expect.objectContaining({ taskIds: [60] }),
  );
});

it("ignores a superseded response so stale task IDs never become batch targets", async () => {
  let finish!: (value: unknown) => void;
  mocks.search.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  show();
  await waitFor(() => expect(mocks.search).toHaveBeenCalledTimes(1));
  fireEvent.change(screen.getByLabelText("acquisition.tasks.search"), { target: { value: "new" } });
  mocks.search.mockResolvedValue(
    response({
      items: [{ id: 90, title: "New result", builderId: "post" }],
      checkableTaskIds: [90],
      filteredTaskIds: [90],
      totalCount: 1,
    }),
  );
  await screen.findByText("New result");
  await act(async () =>
    finish(
      response({
        items: [{ id: 1, title: "Stale result", builderId: "post" }],
        checkableTaskIds: [1],
      }),
    ),
  );
  expect(screen.queryByText("Stale result")).toBeNull();
  reviewSources();
  expect(mocks.portal).toHaveBeenCalledWith(
    SourceAvailabilityReviewModal,
    expect.objectContaining({ taskIds: [90] }),
  );
});

it("keeps selected checkboxes stable and editable while a background refresh is pending", async () => {
  vi.useFakeTimers();
  let view: ReturnType<typeof render>;
  await act(async () => {
    view = render(
      <div data-testid="scroll">
        <MemoryRouter>
          <AcquisitionPage />
        </MemoryRouter>
      </div>,
    );
  });
  const row = screen.getByText("Current page task");
  const taskCheckbox = screen.getByRole("checkbox", { name: "acquisition.selection.task" });
  const pageCheckbox = screen.getByRole("checkbox", { name: "acquisition.selection.page" });
  fireEvent.click(taskCheckbox);
  expect(taskCheckbox).toBeChecked();
  expect(pageCheckbox).toBeChecked();
  let finish!: (value: unknown) => void;
  mocks.search.mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  const scroller = screen.getByTestId("scroll");
  scroller.scrollTop = 320;
  await act(async () => {
    vi.advanceTimersByTime(5000);
  });
  expect(mocks.search).toHaveBeenCalledTimes(2);
  expect(screen.getByText("Current page task")).toBe(row);
  expect(screen.getByRole("checkbox", { name: "acquisition.selection.task" })).toBe(taskCheckbox);
  expect(screen.getByRole("checkbox", { name: "acquisition.selection.page" })).toBe(pageCheckbox);
  expect(taskCheckbox).toBeChecked();
  expect(pageCheckbox).toBeChecked();
  expect(taskCheckbox).toBeEnabled();
  expect(pageCheckbox).toBeEnabled();
  // Local selection is independent of the read-only refresh. Opening the operation
  // still supplies fixed IDs to a new server-side preview before anything executes.
  fireEvent.click(pageCheckbox);
  expect(taskCheckbox).not.toBeChecked();
  fireEvent.click(taskCheckbox);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueEntry.open" }));
  expect(mocks.portal).toHaveBeenLastCalledWith(
    AcquisitionContinueModal,
    expect.objectContaining({ taskIds: [60] }),
  );
  await act(async () => finish(response()));
  expect(screen.getByRole("checkbox", { name: "acquisition.selection.task" })).toBe(taskCheckbox);
  expect(taskCheckbox).toBeChecked();
  expect(taskCheckbox).toBeEnabled();
  expect(scroller.scrollTop).toBe(320);
  expect(mocks.recipes).toHaveBeenCalledTimes(1);
  view!.unmount();
});

it("protects a failed background refresh and reconciles selections only after a valid replacement", async () => {
  vi.useFakeTimers();
  await act(async () => {
    show();
  });
  const taskCheckbox = screen.getByRole("checkbox", { name: "acquisition.selection.task" });
  fireEvent.click(taskCheckbox);
  mocks.search.mockRejectedValueOnce(new Error("offline"));
  await act(async () => {
    vi.advanceTimersByTime(5000);
  });
  expect(taskCheckbox).toBeChecked();
  expect(taskCheckbox).toBeDisabled();
  expect(screen.getByRole("button", { name: "acquisition.continueEntry.open" })).toBeDisabled();
  let finish!: (value: unknown) => void;
  mocks.search.mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  await act(async () => {
    vi.advanceTimersByTime(5000);
  });
  expect(taskCheckbox).toBeChecked();
  expect(taskCheckbox).toBeDisabled();
  await act(async () =>
    finish(
      response({
        items: [{ id: 36, title: "Current replacement", builderId: "post" }],
        filteredTaskIds: [36],
      }),
    ),
  );
  expect(screen.getByText("Current replacement")).toBeVisible();
  expect(screen.getByRole("checkbox", { name: "acquisition.selection.task" })).not.toBeChecked();
  expect(screen.getByRole("button", { name: "acquisition.continueEntry.open" })).toBeDisabled();
});

it("opens retained history by deep link even when it is absent from the page", async () => {
  mocks.get.mockResolvedValue({
    code: 0,
    data: { id: 4, resourceName: "Cancelled history", status: Status.Cancelled },
  });
  show("/acquisitions?taskId=4");
  await waitFor(() =>
    expect(mocks.portal).toHaveBeenCalledWith(
      LegacyTaskDetailModal,
      expect.objectContaining({ taskId: 4 }),
    ),
  );
  expect(mocks.get).toHaveBeenCalledWith(4, expect.objectContaining({ sensitiveResponse: true }));
});

it("shows a read error and does not pretend unknown counts or targets are actionable", async () => {
  mocks.search.mockRejectedValue(new Error("offline"));
  show();
  await screen.findByRole("alert");
  expect(actions()).toBeDisabled();
  expect(screen.getByRole("button", { name: "acquisition.stage.all —" })).toBeDisabled();
});

it("captures every filtered page for diagnosis without restricting to source-checkable tasks", async () => {
  const ids = [60, 59, 58, 57];
  mocks.search.mockResolvedValue(response({ filteredTaskIds: ids, checkableTaskIds: [] }));
  show();
  await screen.findByText("Current page task");
  selectAll();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueEntry.open" }));
  expect(mocks.portal).toHaveBeenCalledWith(
    AcquisitionContinueModal,
    expect.objectContaining({
      taskIds: [60, 59, 58, 57],
      scopeLabel: expect.stringContaining("acquisition.stage.all"),
      onChanged: expect.any(Function),
    }),
  );
  const props = mocks.portal.mock.calls.at(-1)?.[1];
  ids.push(99);
  expect(props.taskIds).toEqual([60, 59, 58, 57]);
});

it.each(["policy", "cleanup"])(
  "captures every filtered page for %s maintenance preview",
  async (mode) => {
    const ids = [60, 59, 58, 57];
    mocks.search.mockResolvedValue(response({ filteredTaskIds: ids, checkableTaskIds: [60] }));
    show();
    await screen.findByText("Current page task");
    selectAll();
    fireEvent.click(
      screen.getByRole("button", {
        name:
          mode === "policy"
            ? "acquisition.actionbar.storagePolicy"
            : "acquisition.actionbar.workspaceCleanup",
      }),
    );
    expect(mocks.portal).toHaveBeenCalledWith(
      AcquisitionStorageMaintenanceModal,
      expect.objectContaining({ mode, taskIds: [60, 59, 58, 57], onChanged: expect.any(Function) }),
    );
    const props = mocks.portal.mock.calls.at(-1)?.[1];
    ids.push(99);
    expect(props.taskIds).toEqual([60, 59, 58, 57]);
  },
);

it("persists the acquisition reserve preference separately from directory and capacity settings", async () => {
  const original = 50 * 1024 ** 3;
  let stored = false;
  mocks.getOptions.mockImplementation(async () => ({
    code: 0,
    data: { useEmergencySpaceReserve: stored, minimumFreeSpaceBytes: original },
  }));
  mocks.setUp.mockImplementation(async ({ useEmergencySpaceReserve }) => {
    stored = useEmergencySpaceReserve;
    return { code: 0 };
  });
  const first = show();
  const choice = await screen.findByRole("checkbox", { name: "acquisition.reserve.global.label" });
  expect(choice).not.toBeChecked();
  fireEvent.click(choice);
  await waitFor(() => expect(choice).toBeChecked());
  expect(mocks.setUp.mock.calls[0][0]).toEqual({ useEmergencySpaceReserve: true });
  first.unmount();
  show();
  const reopened = await screen.findByRole("checkbox", {
    name: "acquisition.reserve.global.label",
  });
  expect(reopened).toBeChecked();
  fireEvent.click(reopened);
  await waitFor(() => expect(reopened).not.toBeChecked());
  expect(mocks.setUp.mock.calls[1][0]).toEqual({ useEmergencySpaceReserve: false });
});

it("combines a persisted download source with stages and tags, resets page and selection, and includes it in bulk scope", async () => {
  localStorage.setItem(
    acquisitionListPreferencesKey(),
    JSON.stringify({
      version: 1,
      stage: "all",
      keyword: "archive",
      sort: "newest",
      downloadSource: "http",
      attentionTags: [],
      facts: [],
    }),
  );
  show();
  await waitFor(() =>
    expect(mocks.search).toHaveBeenCalledWith(
      expect.objectContaining({ downloadSource: "http", keyword: "archive" }),
      expect.anything(),
    ),
  );
  const source = screen.getByRole("button", { name: "downloadCenter.provider.http 24" });
  expect(source).toHaveAttribute("aria-pressed", "true");
  fireEvent.click(stageButton("attention", 30));
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ stage: "attention", downloadSource: "http" }),
      expect.anything(),
    ),
  );
  selectAll();
  fireEvent.click(screen.getByRole("button", { name: "page 1/3" }));
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 2 }),
      expect.anything(),
    ),
  );
  fireEvent.click(screen.getByRole("button", { name: "enum.thirdPartyId.baiduNetdisk 20" }));
  await waitFor(() =>
    expect(mocks.search).toHaveBeenLastCalledWith(
      expect.objectContaining({
        downloadSource: "site:14",
        stage: "attention",
        keyword: "archive",
        page: 1,
      }),
      expect.anything(),
    ),
  );
  expect(
    screen.queryAllByRole("checkbox").every((input) => !(input as HTMLInputElement).checked),
  ).toBe(true);
  expect(JSON.parse(localStorage.getItem(acquisitionListPreferencesKey())!).downloadSource).toBe(
    "site:14",
  );
  fireEvent.click(actions());
  const continuation = mocks.portal.mock.calls.find(
    ([component]) => component === AcquisitionContinueModal,
  )?.[1];
  expect(continuation.scopeLabel).toContain("enum.thirdPartyId.baiduNetdisk");
  fireEvent.click(screen.getByRole("button", { name: "downloader.filter.allSources 60" }));
  await waitFor(() => expect(mocks.search.mock.lastCall?.[0].downloadSource).toBeUndefined());
  expect(mocks.search.mock.lastCall?.[0].keyword).toBe("archive");
});

it("clears the download source when a newly created task resets the list scope", async () => {
  localStorage.setItem(
    acquisitionListPreferencesKey(),
    JSON.stringify({
      version: 1,
      stage: "all",
      keyword: "",
      sort: "newest",
      downloadSource: "bt",
      attentionTags: [],
      facts: [],
    }),
  );
  show();
  await screen.findByText("Current page task");
  fireEvent.click(screen.getByRole("button", { name: "acquisition.start" }));
  const create = mocks.portal.mock.lastCall?.[1];
  act(() => create.onStarted(42));
  await waitFor(() => expect(mocks.search.mock.lastCall?.[0].downloadSource).toBeUndefined());
  expect(screen.getByRole("button", { name: "downloader.filter.allSources 60" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
});

it("keeps all selected empty facets individually removable, including sparse provider counts", async () => {
  localStorage.setItem(acquisitionListPreferencesKey(), JSON.stringify({
    version: 1, stage: "attention", downloadSource: "site:14", capabilityId: "archive", executionState: "running",
    attentionTags: ["sourceUnavailable", "localStorage"], facts: ["downloadedPending"],
  }));
  mocks.search.mockResolvedValue(response({
    items: [], filteredTaskIds: [], checkableTaskIds: [], totalCount: 0,
    stageCounts: { all: 2, running: 2, attention: 0 }, downloadSourceCounts: { all: 0 },
    runtimeCapabilityCounts: {}, runtimeExecutionStateCounts: {}, attentionCounts: { networkIssue: 0 }, factCounts: {},
  }));
  show();
  expect(await screen.findByRole("button", { name: "acquisition.stage.attention 0" })).toHaveAttribute("aria-pressed", "true");
  expect(screen.getByRole("button", { name: "acquisition.stage.all 2" })).toBeEnabled();
  expect(screen.queryByRole("button", { name: /acquisition.stage.completed/ })).toBeNull();
  expect(screen.queryByRole("button", { name: /acquisition.attention.tag.networkIssue/ })).toBeNull();
  expect(screen.queryByRole("button", { name: /acquisition.filters.fact.spaceInsufficient/ })).toBeNull();
  expect(screen.getByRole("button", { name: "enum.thirdPartyId.baiduNetdisk 0" })).toHaveAttribute("aria-pressed", "true");
  expect(screen.getByRole("button", { name: "downloader.filter.allSources 0" })).toBeEnabled();
  for (const name of ["archive 0", "Running 0", "acquisition.filters.fact.downloadedPending 0",
    "acquisition.attention.tag.localStorage 0", "acquisition.attention.tag.sourceUnavailable 0"]) {
    expect(screen.getByRole("button", { name })).toBeEnabled();
  }

  fireEvent.click(screen.getByRole("button", { name: "acquisition.stage.all 2" }));
  await waitFor(() => expect(mocks.search.mock.lastCall![0]).toMatchObject({
    stage: "all", capabilityId: "archive", executionState: "running", downloadSource: "site:14",
    attentionTags: ["localStorage", "sourceUnavailable"], facts: ["downloadedPending"],
  }));
  mocks.search.mockImplementation(() => new Promise(() => {}));
  fireEvent.click(await screen.findByRole("button", { name: "archive 0" }));
  await waitFor(() => expect(mocks.search.mock.lastCall![0].capabilityId).toBeUndefined());
  const otherTag = screen.getByRole("button", { name: "acquisition.attention.tag.localStorage —" });
  fireEvent.click(otherTag);
  expect(screen.queryByRole("button", { name: /acquisition.attention.tag.localStorage/ })).toBeNull();
  expect(screen.getByRole("button", { name: "acquisition.attention.tag.sourceUnavailable —" })).toBeEnabled();
  await waitFor(() => expect(mocks.search.mock.lastCall![0]).toMatchObject({
    attentionTags: ["sourceUnavailable"], facts: ["downloadedPending"], executionState: "running",
  }));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.filters.clear" }));
  await waitFor(() => expect(mocks.search.mock.lastCall![0]).toMatchObject({ attentionTags: [], facts: [] }));
});

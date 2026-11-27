import { Children, useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import AcquisitionSettingsModal from "../components/AcquisitionSettingsModal";
import { defaultStoragePolicy } from "@/components/Acquire/StoragePolicyFields";
vi.mock("@/components/FileSystemSelector/components/StorageCapacitySummary", () => ({
  default: ({path}: any) => <span data-testid="storage-capacity" data-path={path || ""} />,
}));
const api = vi.hoisted(() => ({
  get: vi.fn(),
  setup: vi.fn(),
  put: vi.fn(),
  selectedPath: "/new/folder",
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: {
      getAcquisitionOptions: api.get,
      setUpAcquisition: api.setup,
      putAcquisitionOptions: api.put,
    },
  },
}));
vi.mock("@/components/FileSystemSelector", () => ({
  FileSystemSelectorButton: ({ fileSystemSelectorProps: props, ...rest }: any) => (
    <button
      aria-label={rest["aria-label"]}
      disabled={rest.isDisabled}
      onClick={() =>
        props.onSelected({ path: props.targetType === "folder" ? api.selectedPath : "/wrong/file" })
      }
    >
      {props.defaultSelectedPath || rest.children}
    </button>
  ),
}));
vi.mock("@/components/FileSystemSelector/components/Button", () => ({
  default: ({ fileSystemSelectorProps: props, ...rest }: any) => (
    <button
      aria-label={rest["aria-label"]}
      disabled={rest.isDisabled}
      onClick={() =>
        props.onSelected({ path: props.targetType === "folder" ? api.selectedPath : "/wrong/file" })
      }
    >
      {props.defaultSelectedPath || rest.children}
    </button>
  ),
}));
vi.mock("@/components/SourceAvailability/SourcePriorityConfiguration", () => ({
  default: function MockSourcePriorityConfiguration({ onSavingChange }: any) {
    const [value, setValue] = useState("original priority");
    return (
      <div>
        <label>
          priority draft
          <input value={value} onChange={(e) => setValue(e.target.value)} />
        </label>
        <button onClick={() => onSavingChange(true)}>begin priority save</button>
        <button onClick={() => onSavingChange(false)}>finish priority save</button>
      </div>
    );
  },
}));
vi.mock("@/components/bakaui", () => ({
  Accordion: ({ children }: any) => <div>{children}</div>,
  AccordionItem: ({ children }: any) => <div>{children}</div>,
  Modal: ({ children, footer, visible }: any) =>
    visible ? (
      <div role="dialog">
        {children}
        {footer}
      </div>
    ) : null,
  Button: ({ children, onPress, isDisabled, isLoading, ...props }: any) => (
    <button
      disabled={isDisabled || isLoading}
      onClick={onPress}
      aria-pressed={props["aria-pressed"]}
    >
      {children}
    </button>
  ),
  Checkbox: ({ children, isSelected, isDisabled, onValueChange }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(e) => onValueChange(e.target.checked)}
      />
      {children}
    </label>
  ),
  Select: ({ children, dataSource, label, selectedKeys, onSelectionChange, isDisabled }: any) => (
    <label>
      {label}
      <select
        value={[...selectedKeys][0]}
        disabled={isDisabled}
        onChange={(e) => onSelectionChange(new Set([e.target.value]))}
      >
        {children ??
          dataSource?.map((item: any) => (
            <option key={item.value} value={item.value}>
              {item.label}
            </option>
          ))}
      </select>
    </label>
  ),
  SelectItem: ({ children, textValue }: any) => (
    <option value={String(textValue).split(".").at(-1)}>{children}</option>
  ),
  Spinner: (props: any) => <span role="status" aria-label={props["aria-label"]} />,
  Input: ({ label, value, onValueChange, isInvalid, isDisabled }: any) => (
    <label>
      {label}
      <input
        value={value}
        aria-invalid={isInvalid}
        disabled={isDisabled}
        onChange={(e) => onValueChange(e.target.value)}
      />
    </label>
  ),
  NumberInput: ({ label, value, onValueChange, isInvalid, isDisabled }: any) => (
    <label>
      {label}
      <input
        type="number"
        value={value ?? ""}
        aria-invalid={isInvalid}
        disabled={isDisabled}
        onChange={(e) => onValueChange(e.target.valueAsNumber)}
      />
    </label>
  ),
  Tab: () => null,
  Tabs: ({
    children,
    selectedKey,
    onSelectionChange,
    destroyInactiveTabPanel,
    isDisabled,
  }: any) => (
    <div data-testid="tabs" data-keep-mounted={String(destroyInactiveTabPanel === false)}>
      {Children.toArray(children).map((tab: any) => (
        <button
          disabled={isDisabled}
          key={tab.key}
          onClick={() => onSelectionChange(tab.key.replace(/^\.\$/, ""))}
        >
          {tab.props.title}
        </button>
      ))}
      {Children.toArray(children).map((tab: any) => (
        <div key={tab.key} hidden={tab.key.replace(/^\.\$/, "") !== selectedKey}>
          {tab.props.children}
        </div>
      ))}
    </div>
  ),
}));
const options = {
  inboxDirectory: "/downloads",
  libraryRootDirectory: "/library",
  directoryTemplate: "{Date}/{Title}",
  autoPurchaseLimit: 17.5,
  deliveryRootDirectory: "/delivery",
  deliveryDirectoryTemplate: "{Title} [A{TaskId}]",
  deliveryPartDirectoryTemplate: "{PartTitle} [{PartId}]",
  processingWorkspaceDirectory: "/processing",
  preferredDriveKinds: [4, 2],
  sourceProviderPriority: ["baidu", "http"],
  concurrency: 7,
  deleteArchiveAfterExtraction: false,
  tryRecentPasswords: false,
  recentPasswordCandidateCount: 3,
  recipeByLeadKind: { 1: "custom" },
};
const saveButton = () =>
  screen.getByRole("button", { name: "acquisition.settings.saveDirectories" });
beforeEach(() => {
  vi.clearAllMocks();
  api.selectedPath = "/new/folder";
  api.get.mockReset().mockResolvedValue({ code: 0, data: options });
  api.setup.mockReset().mockResolvedValue({ code: 0, data: {} });
});
afterEach(cleanup);
it("defaults to twenty parallel acquisition tasks and saves only the edited concurrency", async () => {
  api.get.mockResolvedValue({ code: 0, data: {
    ...options, concurrency: undefined, deliveryDirectoryTemplate: undefined, deliveryPartDirectoryTemplate: undefined,
    processingWorkspaceDirectory: undefined, storagePolicy: { ...defaultStoragePolicy, processingWorkspaceDirectory: "/saved-work" },
  } });
  render(<AcquisitionSettingsModal />);
  const input = await screen.findByLabelText("acquisition.settings.concurrency.label");
  expect(input).toHaveValue(20);
  expect(saveButton()).toBeDisabled();
  for (const value of ["0", "101", "1.5"]) {
    fireEvent.change(input, { target: { value } });
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(saveButton()).toBeDisabled();
  }
  for (const concurrency of [100, 1, 20]) {
    fireEvent.change(input, { target: { value: String(concurrency) } });
    fireEvent.click(saveButton());
    await screen.findByText("acquisition.settings.saved");
    expect(api.setup).toHaveBeenLastCalledWith({ concurrency }, { showErrorToast: false });
    expect(saveButton()).toBeDisabled();
  }
  expect(api.put).not.toHaveBeenCalled();
});

it("preserves configured concurrency when saving another setting", async () => {
  render(<AcquisitionSettingsModal />);
  expect(await screen.findByLabelText("acquisition.settings.concurrency.label")).toHaveValue(7);
  fireEvent.change(screen.getByLabelText("acquisition.storage.minimumFreeGiB.label"), { target: { value: "120" } });
  fireEvent.click(saveButton());
  await screen.findByText("acquisition.settings.saved");
  expect(api.setup).toHaveBeenCalledExactlyOnceWith({ minimumFreeSpaceBytes: 120 * 1024 ** 3 }, { showErrorToast: false });
  expect(saveButton()).toBeDisabled();
  expect(screen.getByLabelText("acquisition.settings.concurrency.label")).toHaveValue(7);
  expect(api.put).not.toHaveBeenCalled();
});

it("shows the default reserve for legacy options without marking untouched settings dirty", async () => {
  render(<AcquisitionSettingsModal />);
  expect(await screen.findByLabelText("acquisition.storage.minimumFreeGiB.label")).toHaveValue(100);
  expect(saveButton()).toBeDisabled();
});

it("opts into retaining superseded deliveries from legacy settings and can turn it off again", async () => {
  render(<AcquisitionSettingsModal />);
  const retention = await screen.findByRole("checkbox", {
    name: "acquisition.delivery.keepSuperseded.label",
  });
  expect(retention).not.toBeChecked();
  expect(saveButton()).toBeDisabled();
  for (const keepSupersededDeliveryFiles of [true, false]) {
    fireEvent.click(retention);
    fireEvent.click(saveButton());
    await screen.findByText("acquisition.settings.saved");
    expect(saveButton()).toBeDisabled();
    expect(api.setup).toHaveBeenLastCalledWith(
      { keepSupersededDeliveryFiles },
      { showErrorToast: false },
    );
  }
});

it("restores a previously customized acquisition policy without changing task paths", async () => {
  api.get.mockResolvedValue({ code: 0, data: { ...options, deliveryContentLayout: {
    version: 1, removeGeneratedDirectories: false, collapseSingleRootDirectory: true,
  } } });
  render(<AcquisitionSettingsModal />);
  const reset = await screen.findByRole("button", { name: "artifact.deliveryLayout.restoreDefault" });
  expect(saveButton()).toBeDisabled();
  fireEvent.click(reset);
  fireEvent.click(saveButton());
  await waitFor(() =>
    expect(api.setup).toHaveBeenCalledExactlyOnceWith(
      {
        deliveryContentLayout: {
          version: 1,
          removeGeneratedDirectories: true,
          collapseSingleRootDirectory: false,
        },
      },
      { showErrorToast: false },
    ),
  );
  await waitFor(() => expect(saveButton()).toBeDisabled());
  expect(api.put).not.toHaveBeenCalled();
});

it("does not send a null layout from an old options response while changing unrelated settings", async () => {
  api.get.mockResolvedValue({ code: 0, data: { ...options, deliveryContentLayout: null } });
  render(<AcquisitionSettingsModal />);
  expect(await screen.findByText("artifact.deliveryLayout.acquisitionDefault")).toBeInTheDocument();
  expect(screen.queryByRole("checkbox", { name: "artifact.deliveryLayout.removeGenerated" })).not.toBeInTheDocument();
  expect(screen.queryByRole("checkbox", { name: "artifact.deliveryLayout.collapseSingleRoot" })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("acquisition.storage.minimumFreeGiB.label"), {
    target: { value: "110" },
  });
  fireEvent.click(saveButton());
  await waitFor(() =>
    expect(api.setup).toHaveBeenCalledExactlyOnceWith(
      { minimumFreeSpaceBytes: 110 * 1024 ** 3 },
      { showErrorToast: false },
    ),
  );
});

it("saves only the chosen runtime reserve in bytes without changing task paths or other settings", async () => {
  render(<AcquisitionSettingsModal />);
  fireEvent.change(await screen.findByLabelText("acquisition.storage.minimumFreeGiB.label"), {
    target: { value: "125.5" },
  });
  fireEvent.click(saveButton());
  await waitFor(() => expect(api.setup).toHaveBeenCalledExactlyOnceWith(
    { minimumFreeSpaceBytes: 125.5 * 1024 ** 3 }, { showErrorToast: false },
  ));
  await waitFor(() => expect(saveButton()).toBeDisabled());
  expect(api.put).not.toHaveBeenCalled();
});

it("preserves an explicit zero reserve and can restore the default without changing anything else", async () => {
  api.get.mockResolvedValue({ code: 0, data: { ...options, minimumFreeSpaceBytes: 0 } });
  render(<AcquisitionSettingsModal />);
  const input = await screen.findByLabelText("acquisition.storage.minimumFreeGiB.label");
  expect(input).toHaveValue(0);
  expect(saveButton()).toBeDisabled();
  fireEvent.change(input, { target: { value: "100" } });
  fireEvent.click(saveButton());
  await waitFor(() => expect(api.setup).toHaveBeenCalledExactlyOnceWith(
    { minimumFreeSpaceBytes: 100 * 1024 ** 3 }, { showErrorToast: false },
  ));
});

it.each(["", "-1", "999999999999999999"])("rejects an invalid reserve without saving: %s", async (value) => {
  render(<AcquisitionSettingsModal />);
  const input = await screen.findByLabelText("acquisition.storage.minimumFreeGiB.label");
  fireEvent.change(input, { target: { value } });
  expect(input).toHaveAttribute("aria-invalid", "true");
  expect(saveButton()).toBeDisabled();
  expect(api.setup).not.toHaveBeenCalled();
});

it("sets and clears acquisition download storage without changing the processing or delivery roots", async () => {
  api.get.mockResolvedValue({ code: 0, data: { ...options, downloadRootDirectory: "/old-work" } });
  render(<AcquisitionSettingsModal />);
  await screen.findByText("/old-work");
  fireEvent.click(screen.getByRole("button", { name: "acquisition.storage.chooseDownloadRoot" }));
  fireEvent.click(saveButton());
  await waitFor(() =>
    expect(api.setup).toHaveBeenCalledWith(
      { downloadRootDirectory: "/new/folder" },
      { showErrorToast: false },
    ),
  );
  await waitFor(() => expect(saveButton()).toBeDisabled());
  fireEvent.click(screen.getByRole("button", { name: "acquisition.storage.inheritDownloadRoot" }));
  fireEvent.click(saveButton());
  await waitFor(() =>
    expect(api.setup).toHaveBeenLastCalledWith(
      { downloadRootDirectory: "" },
      { showErrorToast: false },
    ),
  );
});
it("loads existing values without rewriting defaults and keeps source preference editing inline", async () => {
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  expect(screen.getByText("/downloads")).toBeVisible();
  expect(screen.queryByText("/library")).toBeNull();
  expect(screen.queryByLabelText("acquisition.setup.template.label")).toBeNull();
  expect(screen.queryByRole("button", { name: "acquisition.setup.library.browse" })).toBeNull();
  expect(screen.getByLabelText("acquisition.setup.limit.label")).toHaveValue(17.5);
  expect(saveButton()).toBeDisabled();
  expect(screen.getByTestId("tabs")).toHaveAttribute("data-keep-mounted", "true");
  expect(screen.queryByText("acquisition.setup.drives.label")).toBeNull();
  expect(api.setup).not.toHaveBeenCalled();
  expect(api.put).not.toHaveBeenCalled();
});
it("keeps unsaved directory and inline priority drafts when switching tabs", async () => {
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  fireEvent.change(screen.getByLabelText("acquisition.delivery.taskTemplate"), {
    target: { value: "Games/{Title} [A{TaskId}]" },
  });
  fireEvent.click(screen.getByRole("button", { name: "acquisition.settings.tabs.sources" }));
  fireEvent.change(screen.getByLabelText("priority draft"), {
    target: { value: "edited priority" },
  });
  fireEvent.click(screen.getByRole("button", { name: "acquisition.settings.tabs.directories" }));
  expect(screen.getByLabelText("acquisition.delivery.taskTemplate")).toHaveValue("Games/{Title} [A{TaskId}]");
  expect(screen.getByLabelText("priority draft")).toHaveValue("edited priority");
  expect(api.setup).not.toHaveBeenCalled();
});
it("opens the shared source settings directly when requested", async () => {
  render(<AcquisitionSettingsModal initialTab="sources" />);
  await waitFor(() => expect(screen.getByLabelText("priority draft")).toBeVisible());
  expect(screen.queryByRole("button", { name: "acquisition.settings.saveDirectories" })).toBeNull();
  expect(api.setup).not.toHaveBeenCalled();
});
it("uses the setup patch for selected folders and preserves unrelated options by omitting them", async () => {
  const onDone = vi.fn();
  render(<AcquisitionSettingsModal onDone={onDone} />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.setup.inbox.browse" }));
  fireEvent.click(saveButton());
  await waitFor(() =>
    expect(api.setup).toHaveBeenCalledWith(
      { inboxDirectory: "/new/folder" },
      { showErrorToast: false },
    ),
  );
  await screen.findByText("acquisition.settings.saved");
  expect(api.put).not.toHaveBeenCalled();
  expect(onDone).toHaveBeenCalledTimes(1);
  expect(screen.getByRole("dialog")).toBeVisible();
  expect(saveButton()).toBeDisabled();
});
it("disables saving on load failure and retries without saving guessed defaults", async () => {
  api.get.mockRejectedValueOnce(new Error("offline"));
  render(<AcquisitionSettingsModal />);
  await screen.findByText("acquisition.settings.loadFailed");
  expect(saveButton()).toBeDisabled();
  expect(screen.queryByLabelText("acquisition.setup.limit.label")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "common.action.retry" }));
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  expect(api.setup).not.toHaveBeenCalled();
});
it("rejects an unsuccessful options response even when it contains data", async () => {
  api.get.mockResolvedValue({ code: 500, data: options });
  render(<AcquisitionSettingsModal />);
  await screen.findByText("acquisition.settings.loadFailed");
  expect(saveButton()).toBeDisabled();
  expect(api.setup).not.toHaveBeenCalled();
});
it("blocks invalid relative paths and preserves edits when saving fails", async () => {
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  fireEvent.change(screen.getByLabelText("acquisition.delivery.taskTemplate"), {
    target: { value: "../Games" },
  });
  expect(saveButton()).toBeDisabled();
  fireEvent.change(screen.getByLabelText("acquisition.delivery.taskTemplate"), {
    target: { value: "Games/{Title} [A{TaskId}]" },
  });
  api.setup.mockResolvedValueOnce({ code: 400 });
  fireEvent.click(saveButton());
  await screen.findByText("acquisition.settings.saveFailed");
  expect(screen.getByLabelText("acquisition.delivery.taskTemplate")).toHaveValue("Games/{Title} [A{TaskId}]");
  expect(saveButton()).toBeEnabled();
  fireEvent.click(saveButton());
  await screen.findByText("acquisition.settings.saved");
});
it("does not allow overlapping saves or change settings when closing", async () => {
  let finish!: (value: unknown) => void;
  api.setup.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  fireEvent.change(screen.getByLabelText("acquisition.setup.limit.label"), {
    target: { value: "20" },
  });
  fireEvent.click(saveButton());
  fireEvent.click(saveButton());
  expect(api.setup).toHaveBeenCalledTimes(1);
  await act(async () => finish({ code: 0 }));
  fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(api.setup).toHaveBeenCalledTimes(1);
});
it("does not clear an existing directory when the selector returns no usable path", async () => {
  api.selectedPath = " ";
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.setup.inbox.browse" }));
  expect(screen.getByText("/downloads")).toBeVisible();
  expect(saveButton()).toBeDisabled();
  fireEvent.change(screen.getByLabelText("acquisition.delivery.taskTemplate"), {
    target: { value: "Games/{Title} [A{TaskId}]" },
  });
  fireEvent.click(saveButton());
  await waitFor(() =>
    expect(api.setup).toHaveBeenCalledWith(
      expect.objectContaining({ deliveryDirectoryTemplate: "Games/{Title} [A{TaskId}]" }),
      expect.any(Object),
    ),
  );
});
it("prevents saving separate tabs concurrently while preserving both drafts", async () => {
  let finish!: (value: unknown) => void;
  api.setup.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  fireEvent.change(screen.getByLabelText("acquisition.setup.limit.label"), {
    target: { value: "20" },
  });
  fireEvent.click(saveButton());
  expect(screen.getByRole("button", { name: "acquisition.settings.tabs.sources" })).toBeDisabled();
  await act(async () => finish({ code: 0 }));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.settings.tabs.sources" }));
  fireEvent.click(screen.getByRole("button", { name: "begin priority save" }));
  expect(
    screen.getByRole("button", { name: "acquisition.settings.tabs.directories" }),
  ).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "finish priority save" }));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.settings.tabs.directories" }));
  expect(screen.getByLabelText("acquisition.setup.limit.label")).toHaveValue(20);
  expect(api.setup).toHaveBeenCalledTimes(1);
});

it("allows custom delivery defaults and explicitly clearing the root without changing download storage", async () => {
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.delivery.inheritRoot" }));
  fireEvent.change(screen.getByLabelText("acquisition.delivery.taskTemplate"), {
    target: { value: "Games/{Title} [A{TaskId}]" },
  });
  fireEvent.click(saveButton());
  await waitFor(() =>
    expect(api.setup).toHaveBeenCalledWith(
      expect.objectContaining({
        deliveryRootDirectory: "",
        deliveryDirectoryTemplate: "Games/{Title} [A{TaskId}]",
      }),
      expect.any(Object),
    ),
  );
  expect(api.setup.mock.calls[0][0]).not.toHaveProperty("libraryRootDirectory");
  expect(api.setup.mock.calls[0][0]).not.toHaveProperty("inboxDirectory");
  expect(api.put).not.toHaveBeenCalled();
});
it("blocks delivery templates that omit stable identity or escape the root", async () => {
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  for (const value of ["{Title}", "../{TaskId}", "/{TaskId}", "{Unknown}/{TaskId}"]) {
    fireEvent.change(screen.getByLabelText("acquisition.delivery.taskTemplate"), {
      target: { value },
    });
    expect(saveButton()).toBeDisabled();
  }
  expect(api.setup).not.toHaveBeenCalled();
});

it("saves a selected processing workspace without changing download or delivery settings", async () => {
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  expect(screen.getByText("/processing")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.workspace.chooseDirectory" }));
  fireEvent.click(saveButton());
  await waitFor(() =>
    expect(api.setup).toHaveBeenCalledWith(
      {
        processingWorkspaceDirectory: "/new/folder",
        storagePolicy: { ...defaultStoragePolicy, processingWorkspaceDirectory: "/new/folder" },
      },
      { showErrorToast: false },
    ),
  );
  await screen.findByText("acquisition.settings.saved");
  expect(screen.getByText("/new/folder")).toBeVisible();
  expect(saveButton()).toBeDisabled();
});

it("explicitly clears the workspace override to use input storage", async () => {
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.workspace.useInputStorage" }));
  fireEvent.click(saveButton());
  await waitFor(() =>
    expect(api.setup).toHaveBeenCalledWith(
      { processingWorkspaceDirectory: "", storagePolicy: defaultStoragePolicy },
      { showErrorToast: false },
    ),
  );
  await screen.findByText("acquisition.settings.saved");
  expect(saveButton()).toBeDisabled();
});

it("does not send a workspace override when loading older options without one", async () => {
  api.get.mockResolvedValue({
    code: 0,
    data: { ...options, processingWorkspaceDirectory: undefined },
  });
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  expect(saveButton()).toBeDisabled();
  fireEvent.change(screen.getByLabelText("acquisition.setup.limit.label"), {
    target: { value: "20" },
  });
  fireEvent.click(saveButton());
  await waitFor(() =>
    expect(api.setup).toHaveBeenCalledWith({ autoPurchaseLimit: 20 }, { showErrorToast: false }),
  );
});

it("saves policy edits without overwriting unrelated settings or changing frozen tasks", async () => {
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue("{Title} [D{OperationId}]");
  fireEvent.change(screen.getByLabelText("acquisition.storage.downloadTemplate"), {
    target: { value: "{Provider}/{Title} [D{OperationId}]" },
  });
  fireEvent.click(
    screen.getByRole("checkbox", { name: "acquisition.storage.cleanupIntermediates" }),
  );
  fireEvent.click(saveButton());
  await waitFor(() =>
    expect(api.setup).toHaveBeenCalledWith(
      {
        storagePolicy: {
          ...defaultStoragePolicy,
          processingWorkspaceDirectory: "/processing",
          downloadDirectoryTemplate: "{Provider}/{Title} [D{OperationId}]",
          cleanupIntermediates: false,
        },
      },
      { showErrorToast: false },
    ),
  );
  expect(api.put).not.toHaveBeenCalled();
});

it("blocks policy templates that omit stable operation or nested attempt identity", async () => {
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue("{Title} [D{OperationId}]");
  fireEvent.change(screen.getByLabelText("acquisition.storage.processingTemplate"), {
    target: { value: "{Title}/{OperationId}" },
  });
  expect(saveButton()).toBeDisabled();
  expect(api.setup).not.toHaveBeenCalled();
});


it("shows reusable capacity summaries for the selected download, processing and delivery volumes", async () => {
  api.get.mockResolvedValue({ code: 0, data: { ...options, downloadRootDirectory: "/downloads-root" } });
  render(<AcquisitionSettingsModal />);
  await screen.findByDisplayValue(options.deliveryDirectoryTemplate);
  expect(screen.getAllByTestId("storage-capacity").map(node => node.getAttribute("data-path")))
    .toEqual(["/downloads-root", "/processing", "/delivery"]);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.workspace.useInputStorage" }));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.delivery.inheritRoot" }));
  expect(screen.getAllByTestId("storage-capacity").map(node => node.getAttribute("data-path")))
    .toEqual(["/downloads-root", "/downloads-root", "/downloads-root/Acquired"]);
  expect(api.setup).not.toHaveBeenCalled();
});

it("does not rewrite or validate hidden legacy archive settings when modern defaults change", async () => {
  api.get.mockResolvedValue({ code: 0, data: { ...options, directoryTemplate: "../historical-invalid", libraryRootDirectory: "/old-library" } });
  render(<AcquisitionSettingsModal />);
  const reserve = await screen.findByLabelText("acquisition.storage.minimumFreeGiB.label");
  fireEvent.change(reserve, {target: {value: "110"}});
  fireEvent.click(saveButton());
  await waitFor(() => expect(api.setup).toHaveBeenCalledExactlyOnceWith(
    { minimumFreeSpaceBytes: 110 * 1024 ** 3 }, { showErrorToast: false },
  ));
  expect(screen.queryByText("/old-library")).toBeNull();
  expect(screen.queryByDisplayValue("../historical-invalid")).toBeNull();
});

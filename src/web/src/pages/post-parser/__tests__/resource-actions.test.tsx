import type { ReactNode } from "react";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AddToAcquisitionModal from "../components/AddToAcquisitionModal";
import LocalProcessingModal from "../components/LocalProcessingModal";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";

import { PostParseTarget, PostParserSource } from "@/sdk/constants";

const api = vi.hoisted(() => ({
  import: vi.fn(),
  workflows: vi.fn(),
  runWorkflow: vi.fn(),
  manualWorkflow: vi.fn(),
  download: vi.fn(),
  portal: vi.fn(),
  navigate: vi.fn(),
  openUrl: vi.fn(),
  browser: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    postParser: { importPostParserTaskToAcquisition: api.import, createPostAcquisitionPlan: api.download },
    workflow: { searchWorkflows: api.workflows, runWorkflowManually: api.manualWorkflow },
    downloadIntents: {
      startPostLocalProcessing: api.runWorkflow,
      submitPostDownloadIntent: vi.fn(),
    },
    gui: { openUrlInDefaultBrowser: api.openUrl },
  },
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => api.navigate }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("@/components/FileSystemSelector/components/Modal", () => ({ default: () => null }));
vi.mock("@/components/FileSystemSelector/components/Panel", () => ({ default: () => null }));
vi.mock("@/components/Workflow/displayNames", () => ({
  activityDisplayName: (_: unknown, kind: string) => kind,
}));
vi.mock("@/components/Workflow/Activities", () => ({
  getWorkflowActivityUI: () => ({ parseConfig: JSON.parse, Summary: () => null }),
}));
vi.mock("@/components/ThirdPartyIcon", () => ({
  default: () => <span aria-hidden>site-icon</span>,
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children }: any) => <a href={href}>{children}</a>,
}));

vi.mock("@/components/Workflow/WorkflowBrowserModal", () => ({
  default: (props: any) => {
    api.browser(props);
    return (
      <div role="dialog" aria-label="related-workflow-browser">
        <button onClick={props.onClose}>close-workflows</button>
        <button onClick={props.onChanged}>workflows-changed</button>
      </div>
    );
  },
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { number?: number }) =>
      options?.number == null ? key : `${key} ${options.number}`,
  }),
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  toast: { success: vi.fn(), danger: vi.fn() },
  Modal: ({
    children,
    title,
    footer,
    visible,
  }: {
    children: ReactNode;
    title?: ReactNode;
    footer?: ReactNode;
    visible?: boolean;
  }) =>
    visible === false ? null : (
      <div role="dialog">
        <h2>{title}</h2>
        {children}
        {footer}
      </div>
    ),
}));

const firstUrl = "https://pan.example/first";
const otherUrl = "https://pan.example/other";
const extraction = {
  requirement: "required",
  steps: [{ id: "unpack", op: "extractArchive", input: "$download", password: "archive-secret" }],
  evidence: ["Extract the downloaded file with archive-secret."],
};
const task: PostParserTask = {
  id: 14,
  source: PostParserSource.SoulPlus,
  link: "https://post.example/14",
  title: "A post",
  revision: 3,
  targets: [PostParseTarget.DownloadInfo],
  results: {
    DownloadInfo: {
      resources: [
        null,
        { link: firstUrl, code: "code123" },
        { link: firstUrl, password: "archive-secret", extraction },
        "invalid old entry",
        { link: otherUrl, extraction: { requirement: "notRequired", steps: [], evidence: [] } },
      ],
    },
  },
};
const previewUrl = "https://preview.example/screenshots";
const mirrorUrl = "https://mega.example/full-version";
const mirrorExtraction = {
  requirement: "required",
  steps: [
    { id: "rename", op: "renameExtension", input: "$download", extension: ".zip" },
    { id: "extract", op: "extractArchive", input: "rename", password: "mirror-password" },
  ],
  evidence: ["Rename the MEGA file to .zip and extract it using mirror-password."],
};
const groupedTask: PostParserTask = {
  ...task,
  results: {
    DownloadInfo: {
      title: "The entire post",
      groups: [
        { id: "preview", title: "Preview images", kind: "preview", evidence: [] },
        { id: "main", title: "The full resource", kind: "main", evidence: [] },
      ],
      resources: [
        { link: previewUrl, groupId: "preview" },
        { link: firstUrl, code: "code123", groupId: "main" },
        { link: firstUrl, extraction, groupId: "main" },
        { link: mirrorUrl, extraction: mirrorExtraction, groupId: "main" },
      ],
    },
  },
};
const show = (content: ReactNode) =>
  render(<HeroUIProvider disableAnimation>{content}</HeroUIProvider>);

async function openFolderPicker(button: HTMLElement) {
  const calls = api.portal.mock.calls.length;
  fireEvent.click(button);
  await waitFor(() => expect(api.portal).toHaveBeenCalledTimes(calls + 1));
}

beforeEach(() => {
  vi.clearAllMocks();
  api.import.mockResolvedValue({ code: 0, data: { resourceId: 77 } });
  api.workflows.mockResolvedValue({
    code: 0,
    data: [
      {
        id: 99,
        name: "Local plan",
        triggerKind: "fs.processingPlan",
        enabled: true,
        activities: [{ id: 1, order: 0, kind: "acquisition.unpack", configJson: "{}" }],
      },
    ],
  });
  api.runWorkflow.mockResolvedValue({ code: 0, data: { id: 201 } });
});
afterEach(cleanup);

describe("deduplicated post resource actions", () => {
  it("chooses folders and keeps each completed-files switch on its own row without starting downloads", async () => {
    show(<LocalProcessingModal task={task} />);
    await waitFor(() => expect(api.workflows).toHaveBeenCalled());
    const directories = () =>
      screen.getAllByRole("button", { name: /workflow.processing.chooseDirectory/ });
    const pickers = () =>
      screen.getAllByRole("button", { name: /workflow.processing.chooseDirectory/ });
    for (const [index, toggle] of screen.getAllByRole("switch").entries()) {
      const row = toggle.closest("label")!.parentElement!;
      expect(row).toHaveClass("block");
      expect(row.previousElementSibling).toBe(directories()[index]);
      expect(row).not.toContainElement(directories()[index]);
    }
    expect(screen.queryByRole("textbox")).toBeNull();
    await openFolderPicker(pickers()[0]);
    expect(api.portal).toHaveBeenLastCalledWith(
      FileSystemSelectorModal,
      expect.objectContaining({ targetType: "folder", multiple: false, startPath: undefined }),
    );
    act(() => api.portal.mock.lastCall![1].onSelected({ path: "/server/picked-first" }));
    expect(directories()[0]).toHaveTextContent("/server/picked-first");
    expect(directories()[0]).toHaveAttribute("title", "/server/picked-first");
    expect(directories()[1]).toHaveTextContent("workflow.processing.chooseDirectory");
    expect(screen.queryByText("workflow.processing.binding.noDirectory")).toBeNull();
    expect(document.querySelector("code")).toBeNull();
    await openFolderPicker(pickers()[1]);
    act(() => api.portal.mock.lastCall![1].onSelected({ path: "/server/picked-other" }));
    expect(directories()[1]).toHaveTextContent("/server/picked-other");
    await openFolderPicker(pickers()[0]);
    expect(api.portal.mock.lastCall![1]).toEqual(
      expect.objectContaining({
        startPath: "/server/picked-first",
        defaultSelectedPath: "/server/picked-first",
      }),
    );
    expect(screen.getByText("workflow.processing.binding.folderOnly")).toBeVisible();
    expect(screen.getByText("workflow.processing.binding.groupRules")).toBeVisible();
    for (const checkbox of screen.getAllByRole("checkbox")) expect(checkbox).not.toBeChecked();
    expect(api.runWorkflow).not.toHaveBeenCalled();
    expect(api.download).not.toHaveBeenCalled();
  });

  it("locks folder selection while starting and for a completed binding, including late picker callbacks", async () => {
    let finish!: (response: { code: number; data: { id: number } }) => void;
    api.runWorkflow.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    show(<LocalProcessingModal task={task} />);
    await waitFor(() => expect(api.workflows).toHaveBeenCalled());
    const directories = () =>
      screen.getAllByRole("button", { name: /workflow.processing.chooseDirectory/ });
    const pickers = () =>
      screen.getAllByRole("button", { name: /workflow.processing.chooseDirectory/ });
    await openFolderPicker(pickers()[0]);
    act(() => api.portal.mock.lastCall![1].onSelected({ path: "/server/first" }));
    fireEvent.click(screen.getAllByRole("checkbox")[0]);
    await openFolderPicker(pickers()[0]);
    const lateSelection = api.portal.mock.lastCall![1].onSelected;
    fireEvent.click(screen.getByRole("button", { name: "workflow.processing.startSelected" }));
    await waitFor(() => expect(api.runWorkflow).toHaveBeenCalledTimes(1));
    for (const picker of pickers()) expect(picker).toBeDisabled();
    act(() => lateSelection({ path: "/server/late-during-start" }));
    expect(directories()[0]).toHaveTextContent("/server/first");
    await act(async () => finish({ code: 0, data: { id: 201 } }));
    expect(pickers()[0]).toBeDisabled();
    expect(pickers()[1]).toBeEnabled();
    act(() => lateSelection({ path: "/server/late-after-start" }));
    expect(directories()[0]).toHaveTextContent("/server/first");
    expect(api.download).not.toHaveBeenCalled();
  });

  it("opens the shared compatible workflow browser without navigation and preserves directory drafts", async () => {
    show(<LocalProcessingModal task={task} />);
    await waitFor(() => expect(api.workflows).toHaveBeenCalled());
    await openFolderPicker(
      screen.getAllByRole("button", { name: /workflow.processing.chooseDirectory/ })[0],
    );
    act(() => api.portal.mock.lastCall![1].onSelected({ path: "/server/draft" }));
    fireEvent.click(screen.getByRole("button", { name: "workflow.processing.viewWorkflows" }));
    expect(api.browser.mock.lastCall![0]).toEqual(
      expect.objectContaining({ visible: true, triggerKind: "fs.processingPlan" }),
    );
    expect(screen.getAllByRole("dialog")).toHaveLength(2);
    expect(api.navigate).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "workflows-changed" }));
    await waitFor(() => expect(api.workflows).toHaveBeenCalledTimes(2));
    fireEvent.click(screen.getByRole("button", { name: "close-workflows" }));
    expect(screen.getAllByRole("dialog")).toHaveLength(1);
    expect(
      screen.getAllByRole("button", { name: /workflow.processing.chooseDirectory/ })[0],
    ).toHaveTextContent("/server/draft");
    expect(api.runWorkflow).not.toHaveBeenCalled();
    expect(api.download).not.toHaveBeenCalled();
  });

  it("shows complementary duplicates once and submits every original index with the preview revision", async () => {
    show(<AddToAcquisitionModal task={task} />);
    const choices = screen.getAllByRole("checkbox");

    expect(choices).toHaveLength(2);
    expect(choices[0]).toHaveAccessibleName("postParser.acquisition.selectLink 1");
    expect(choices[1]).toHaveAccessibleName("postParser.acquisition.selectLink 2");
    expect(choices[0]).toBeChecked();
    expect(choices[1]).not.toBeChecked();
    expect(screen.getByRole("button", { name: "postParser.action.copyCode" })).toHaveTextContent(
      "code123",
    );
    expect(
      screen.getByRole("button", { name: "postParser.extraction.extractArchive" }),
    ).toBeVisible();

    fireEvent.click(screen.getByRole("button", { name: "postParser.action.addToAcquisition" }));
    await waitFor(() =>
      expect(api.import).toHaveBeenCalledExactlyOnceWith(14, {
        title: "A post",
        resourceIndices: [1, 2],
        revision: 3,
      }),
    );
  });

  it("selects the original independent resource after malformed entries and duplicate groups", async () => {
    show(<AddToAcquisitionModal task={task} />);
    const choices = screen.getAllByRole("checkbox");

    fireEvent.click(choices[0]);
    fireEvent.click(choices[1]);
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.addToAcquisition" }));
    await waitFor(() =>
      expect(api.import).toHaveBeenCalledExactlyOnceWith(14, {
        title: "A post",
        resourceIndices: [4],
        revision: 3,
      }),
    );
  });

  it("starts one local run per selected URL with stable original indices for server-side plan resolution", async () => {
    const { container } = show(<LocalProcessingModal task={task} />);

    await waitFor(() => expect(api.workflows).toHaveBeenCalled());
    const sections = Array.from(container.querySelectorAll("section"));
    const first = sections.filter((section) => section.textContent?.includes(firstUrl));
    const other = sections.find((section) => section.textContent?.includes(otherUrl));

    expect(first).toHaveLength(1);
    expect(other).toBeDefined();
    for (const [section, directory] of [
      [first[0], "/downloads/first"],
      [other!, "/downloads/other"],
    ] as const) {
      fireEvent.click(within(section).getByRole("checkbox"));
      await openFolderPicker(
        within(section).getByRole("button", { name: /workflow.processing.chooseDirectory/ }),
      );
      act(() => api.portal.mock.lastCall![1].onSelected({ path: directory }));
    }
    const start = screen.getByRole("button", { name: "workflow.processing.startSelected" });

    await waitFor(() => expect(start).toBeEnabled());
    fireEvent.click(start);
    await waitFor(() => expect(api.runWorkflow).toHaveBeenCalledTimes(2));
    expect(api.runWorkflow.mock.calls[0]).toEqual([
      {
        taskId: 14,
        revision: 3,
        resourceIndex: 1,
        workflowDefinitionId: 99,
        files: [],
        alreadyProcessed: false,
      },
      { directory: "/downloads/first" },
    ]);
    expect(api.runWorkflow.mock.calls[1]).toEqual([
      {
        taskId: 14,
        revision: 3,
        resourceIndex: 4,
        workflowDefinitionId: 99,
        files: [],
        alreadyProcessed: false,
      },
      { directory: "/downloads/other" },
    ]);
    expect(start).toBeDisabled();
    expect(api.download).not.toHaveBeenCalled();
    expect(api.manualWorkflow).not.toHaveBeenCalled();
  });
});

describe("content-grouped post resource actions", () => {
  it("resolves trimmed group references inside legacy wrappers without compacting submitted indices", async () => {
    const legacyGroupedTask = {
      ...task,
      results: {
        DownloadInfo: {
          data: {
            groups: [{ id: " full ", title: " Full resource ", kind: "main", evidence: [] }],
            resources: [
              { link: previewUrl, groupId: "missing-group" },
              { link: firstUrl, groupId: "full", code: "code123" },
              { link: firstUrl, groupId: " full ", extraction },
              { link: otherUrl },
            ],
          },
        },
      },
    };

    show(<AddToAcquisitionModal task={legacyGroupedTask} />);
    const main = screen.getByRole("group", { name: "Full resource" });
    const unknown = screen.getByRole("group", { name: "postParser.groups.ungrouped" });

    expect(within(main).getAllByRole("checkbox")).toHaveLength(1);
    expect(within(main).getByRole("checkbox")).toBeChecked();
    expect(within(unknown).getAllByRole("checkbox")).toHaveLength(2);
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.addToAcquisition" }));
    await waitFor(() =>
      expect(api.import).toHaveBeenCalledExactlyOnceWith(14, {
        title: "Full resource",
        resourceIndices: [1, 2],
        revision: 3,
      }),
    );
  });

  it("defaults to one main source, suggests its group title, and imports the original duplicate indices", async () => {
    show(<AddToAcquisitionModal task={groupedTask} />);
    const main = screen.getByRole("group", { name: "The full resource" });
    const preview = screen.getByRole("group", { name: "Preview images" });
    const mainChoices = within(main).getAllByRole("checkbox");

    expect(mainChoices).toHaveLength(2);
    expect(mainChoices[0]).toBeChecked();
    expect(mainChoices[1]).not.toBeChecked();
    expect(within(preview).getByRole("checkbox")).not.toBeChecked();
    expect(screen.getByLabelText("postParser.acquisition.title")).toHaveValue("The full resource");
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.addToAcquisition" }));
    await waitFor(() =>
      expect(api.import).toHaveBeenCalledExactlyOnceWith(14, {
        title: "The full resource",
        resourceIndices: [1, 2],
        revision: 3,
      }),
    );
  });

  it("allows cross-group selection with a confirmation hint and keeps an edited resource name", async () => {
    show(<AddToAcquisitionModal task={groupedTask} />);
    const preview = screen.getByRole("group", { name: "Preview images" });

    fireEvent.change(screen.getByLabelText("postParser.acquisition.title"), {
      target: { value: "My resource" },
    });
    fireEvent.click(within(preview).getByRole("checkbox"));
    expect(screen.getByText("postParser.acquisition.crossGroupHint")).toBeVisible();
    expect(screen.getByLabelText("postParser.acquisition.title")).toHaveValue("My resource");
    const save = screen.getByRole("button", { name: "postParser.action.addToAcquisition" });

    expect(save).toBeEnabled();
    fireEvent.click(save);
    await waitFor(() =>
      expect(api.import).toHaveBeenCalledExactlyOnceWith(14, {
        title: "My resource",
        resourceIndices: [0, 1, 2],
        revision: 3,
      }),
    );
  });

  it("does not default-select preview or tool links when no main or unknown source exists", () => {
    const previewOnlyTask = {
      ...task,
      results: {
        DownloadInfo: {
          groups: [
            { id: "preview", title: "Preview", kind: "preview", evidence: [] },
            { id: "tools", title: "Tools", kind: "tool", evidence: [] },
          ],
          resources: [
            { link: previewUrl, groupId: "preview" },
            { link: otherUrl, groupId: "tools" },
          ],
        },
      },
    };

    show(<AddToAcquisitionModal task={previewOnlyTask} />);
    for (const choice of screen.getAllByRole("checkbox")) expect(choice).not.toBeChecked();
    expect(
      screen.getByRole("button", { name: "postParser.action.addToAcquisition" }),
    ).toBeDisabled();
  });

  it("selects an unclassified source instead of a preview when no main content exists", async () => {
    const unknownTask = {
      ...task,
      results: {
        DownloadInfo: {
          groups: [{ id: "preview", title: "Preview", kind: "preview", evidence: [] }],
          resources: [{ link: previewUrl, groupId: "preview" }, { link: otherUrl }],
        },
      },
    };

    show(<AddToAcquisitionModal task={unknownTask} />);
    expect(
      within(screen.getByRole("group", { name: "Preview" })).getByRole("checkbox"),
    ).not.toBeChecked();
    expect(
      within(screen.getByRole("group", { name: "postParser.groups.ungrouped" })).getByRole(
        "checkbox",
      ),
    ).toBeChecked();
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.addToAcquisition" }));
    await waitFor(() => expect(api.import.mock.calls[0]?.[1].resourceIndices).toEqual([1]));
  });

  it("selects one downloaded mirror per content group without automatically selecting ungrouped inputs", async () => {
    show(<LocalProcessingModal task={groupedTask} />);
    await waitFor(() => expect(api.workflows).toHaveBeenCalled());
    const main = screen.getByRole("group", { name: "The full resource" });
    const choices = within(main).getAllByRole("checkbox");

    expect(choices).toHaveLength(2);
    for (const choice of choices) expect(choice).not.toBeChecked();
    expect(screen.getByText("workflow.processing.binding.groupRules")).toBeVisible();
    for (const [url, directory] of [
      [firstUrl, "/downloads/first"],
      [mirrorUrl, "/downloads/mirror"],
    ]) {
      const source = within(main).getByRole("link", { name: url }).closest("section")!;
      const choice = within(source).getByRole("checkbox");

      fireEvent.click(choice);
      await openFolderPicker(
        within(source).getByRole("button", { name: /workflow.processing.chooseDirectory/ }),
      );
      act(() => api.portal.mock.lastCall![1].onSelected({ path: directory }));
    }
    const start = screen.getByRole("button", { name: "workflow.processing.startSelected" });

    await waitFor(() => expect(start).toBeEnabled());
    fireEvent.click(start);
    await waitFor(() => expect(api.runWorkflow).toHaveBeenCalledTimes(1));
    expect(api.runWorkflow.mock.calls[0]).toEqual([
      {
        taskId: 14,
        revision: 3,
        resourceIndex: 3,
        workflowDefinitionId: 99,
        files: [],
        alreadyProcessed: false,
      },
      { directory: "/downloads/mirror" },
    ]);
    expect(
      within(screen.getByRole("group", { name: "Preview images" })).getByRole("checkbox"),
    ).not.toBeChecked();
  });
});

import type { ReactNode } from "react";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { HeroUIProvider } from "@heroui/react";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AddToAcquisitionModal from "../components/AddToAcquisitionModal";
import LocalProcessingModal from "../components/LocalProcessingModal";

import { PostParseTarget, PostParserSource } from "@/sdk/constants";

const api = vi.hoisted(() => ({
  import: vi.fn(),
  workflows: vi.fn(),
  runWorkflow: vi.fn(),
  navigate: vi.fn(),
  openUrl: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    postParser: { importPostParserTaskToAcquisition: api.import },
    workflow: { searchWorkflows: api.workflows, runWorkflowManually: api.runWorkflow },
    gui: { openUrlInDefaultBrowser: api.openUrl },
  },
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => api.navigate }));
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

beforeEach(() => {
  vi.clearAllMocks();
  api.import.mockResolvedValue({ code: 0, data: { resourceId: 77 } });
  api.workflows.mockResolvedValue({ code: 0, data: [{ id: 99, name: "Local plan" }] });
  api.runWorkflow.mockResolvedValue({ code: 0, data: { id: 201 } });
});
afterEach(cleanup);

describe("deduplicated post resource actions", () => {
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

  it("starts one local run per selected URL using the merged plan and stable original binding indices", async () => {
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
      fireEvent.change(within(section).getByLabelText("workflow.processing.directory"), {
        target: { value: directory },
      });
    }
    const start = screen.getByRole("button", { name: "workflow.processing.startSelected" });

    await waitFor(() => expect(start).toBeEnabled());
    fireEvent.click(start);
    await waitFor(() => expect(api.runWorkflow).toHaveBeenCalledTimes(2));
    const firstPayload = JSON.parse(api.runWorkflow.mock.calls[0][1].argsJson);
    const otherPayload = JSON.parse(api.runWorkflow.mock.calls[1][1].argsJson);

    expect(api.runWorkflow.mock.calls[0][0]).toBe(99);
    expect(firstPayload).toMatchObject({ directory: "/downloads/first", bindingId: "post:14:3:1" });
    expect(JSON.parse(firstPayload.extractionPlanJson)).toEqual(extraction);
    expect(otherPayload).toMatchObject({ directory: "/downloads/other", bindingId: "post:14:3:4" });
    expect(start).toBeDisabled();
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

  it("keeps mirror processing plans and original bindings independent without automatically selecting them", async () => {
    show(<LocalProcessingModal task={groupedTask} />);
    await waitFor(() => expect(api.workflows).toHaveBeenCalled());
    const main = screen.getByRole("group", { name: "The full resource" });
    const choices = within(main).getAllByRole("checkbox");

    expect(choices).toHaveLength(2);
    for (const choice of choices) expect(choice).not.toBeChecked();
    expect(screen.getByText("postParser.groups.localProcessingHint")).toBeVisible();
    for (const [url, directory] of [
      [firstUrl, "/downloads/first"],
      [mirrorUrl, "/downloads/mirror"],
    ]) {
      const choice = within(main).getByRole("checkbox", { name: url });
      const source = choice.closest("section")!;

      fireEvent.click(choice);
      fireEvent.change(within(source).getByLabelText("workflow.processing.directory"), {
        target: { value: directory },
      });
    }
    const start = screen.getByRole("button", { name: "workflow.processing.startSelected" });

    await waitFor(() => expect(start).toBeEnabled());
    fireEvent.click(start);
    await waitFor(() => expect(api.runWorkflow).toHaveBeenCalledTimes(2));
    const firstPayload = JSON.parse(api.runWorkflow.mock.calls[0][1].argsJson);
    const mirrorPayload = JSON.parse(api.runWorkflow.mock.calls[1][1].argsJson);

    expect(firstPayload).toMatchObject({ directory: "/downloads/first", bindingId: "post:14:3:1" });
    expect(JSON.parse(firstPayload.extractionPlanJson)).toEqual(extraction);
    expect(mirrorPayload).toMatchObject({
      directory: "/downloads/mirror",
      bindingId: "post:14:3:3",
    });
    expect(JSON.parse(mirrorPayload.extractionPlanJson)).toEqual(mirrorExtraction);
    expect(
      within(screen.getByRole("group", { name: "Preview images" })).getByRole("checkbox"),
    ).not.toBeChecked();
  });
});

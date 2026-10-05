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

import type { ReactNode } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import SourceCorrectionPanel from "../SourceCorrectionPanel";
import type { SourceCorrections } from "../useSourceCorrections";

const api = vi.hoisted(() => ({ correctAccessCode: vi.fn() }));
vi.mock("../api", () => ({ planTaskApi: api }));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isDisabled,
    isLoading,
  }: {
    children: ReactNode;
    onPress: () => void;
    isDisabled?: boolean;
    isLoading?: boolean;
  }) => (
    <button disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({
    label,
    value,
    onValueChange,
    isDisabled,
    description,
  }: {
    label: string;
    value: string;
    onValueChange: (v: string) => void;
    isDisabled?: boolean;
    description?: string;
  }) => (
    <label>
      {label}
      <input
        value={value}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.value)}
      />
      <span>{description}</span>
    </label>
  ),
  Spinner: () => <span>loading</span>,
}));
vi.mock("../../AcquisitionSourceDetails", () => ({
  default: ({ source }: { source: { url: string; accessCode?: string } }) => (
    <p>
      {source.url} / {source.accessCode}
    </p>
  ),
}));
const preview = {
  taskId: 90,
  expectedRevision: 1,
  expectedRunId: 2732,
  checkpointVersion: "checkpoint-a",
  canCorrect: true,
  sources: [
    {
      targetId: "part-a",
      sourceId: "source-a",
      expectedSourceIdentity: "source-version-a",
      url: "https://example.test/share-a",
      providerId: "baidu",
      accessCode: "wrong",
      canCorrect: true,
      history: [],
      currentSource: {
        id: "source-a",
        url: "https://example.test/share-a",
        accessCode: "wrong",
        providerId: "baidu",
        kind: "share",
        resolutionChain: [],
      },
    },
  ],
} as SourceCorrections;
const renderPanel = (changes: Partial<Parameters<typeof SourceCorrectionPanel>[0]> = {}) => {
  const refresh = vi.fn().mockResolvedValue(undefined);
  const applied = vi.fn();
  const busy = vi.fn();
  const props = {
    taskId: 90,
    preview,
    loading: false,
    failed: false,
    disabled: false,
    readOnly: false,
    onRefresh: refresh,
    onApplied: applied,
    onBusyChange: busy,
    ...changes,
  };
  return { ...render(<SourceCorrectionPanel {...props} />), props, refresh, applied, busy };
};
beforeEach(() => {
  api.correctAccessCode
    .mockReset()
    .mockResolvedValue({ taskId: 90, workflowRunId: 2732, accepted: true });
});

it("does not submit on opening and corrects a nonempty wrong code using the exact source checkpoint", async () => {
  const { refresh, applied } = renderPanel();
  expect(api.correctAccessCode).not.toHaveBeenCalled();
  const input = screen.getByRole("textbox");
  expect(input).toHaveValue("wrong");
  expect(
    screen.getByRole("button", { name: "acquisition.sourceCorrection.saveAndContinue" }),
  ).toBeDisabled();
  fireEvent.change(input, { target: { value: "correct" } });
  fireEvent.click(
    screen.getByRole("button", { name: "acquisition.sourceCorrection.saveAndContinue" }),
  );
  await waitFor(() =>
    expect(api.correctAccessCode).toHaveBeenCalledExactlyOnceWith(90, {
      expectedRevision: 1,
      expectedRunId: 2732,
      checkpointVersion: "checkpoint-a",
      correctionId: expect.stringMatching(/^[0-9a-f]{32}$/),
      targetId: "part-a",
      sourceId: "source-a",
      expectedSourceIdentity: "source-version-a",
      newAccessCode: "correct",
    }),
  );
  expect(await screen.findByRole("status")).toHaveTextContent(
    "acquisition.sourceCorrection.accepted",
  );
  expect(refresh).toHaveBeenCalledOnce();
  expect(applied).toHaveBeenCalledOnce();
  expect(
    screen.getByRole("button", { name: "acquisition.sourceCorrection.saveAndContinue" }),
  ).toBeDisabled();
});

it("never resubmits an ambiguous request after refreshing the same checkpoint", async () => {
  api.correctAccessCode.mockRejectedValue(new Error("Connection lost"));
  const { refresh } = renderPanel();
  fireEvent.change(screen.getByRole("textbox"), { target: { value: "correct" } });
  fireEvent.click(
    screen.getByRole("button", { name: "acquisition.sourceCorrection.saveAndContinue" }),
  );
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "acquisition.sourceCorrection.unknown",
  );
  fireEvent.click(screen.getByRole("button", { name: "acquisition.sourceCorrection.refresh" }));
  await waitFor(() => expect(refresh).toHaveBeenCalledTimes(2));
  expect(api.correctAccessCode).toHaveBeenCalledTimes(1);
  expect(
    screen.getByRole("button", { name: "acquisition.sourceCorrection.saveAndContinue" }),
  ).toBeDisabled();
});

it("leaves acquired sources read-only and submits only the selected eligible source", async () => {
  renderPanel({
    preview: {
      ...preview,
      sources: [
        {
          ...preview.sources[0],
          targetId: "part-finished",
          canCorrect: false,
          reasonCode: "SOURCE_CORRECTION_ALREADY_ACQUIRED",
          currentSource: {
            ...preview.sources[0].currentSource,
            url: "https://example.test/finished",
          },
        },
        preview.sources[0],
      ],
    },
  });
  expect(screen.getByText("acquisition.sourceCorrection.reason.ALREADY_ACQUIRED")).toBeVisible();
  expect(screen.getAllByRole("textbox")).toHaveLength(1);
  fireEvent.change(screen.getByRole("textbox"), { target: { value: "" } });
  fireEvent.click(
    screen.getByRole("button", { name: "acquisition.sourceCorrection.saveAndContinue" }),
  );
  await waitFor(() =>
    expect(api.correctAccessCode).toHaveBeenCalledWith(
      90,
      expect.objectContaining({ targetId: "part-a", newAccessCode: undefined }),
    ),
  );
});

it("leaves non-actionable source information to diagnostics without offering edits on active or deleted tasks", () => {
  const current = {
    ...preview,
    canCorrect: false,
    reasonCode: "SOURCE_CORRECTION_TASK_ACTIVE",
    sources: [
      {
        ...preview.sources[0],
        canCorrect: false,
        currentSource: { ...preview.sources[0].currentSource, accessCode: "fixed" },
      },
    ],
  };
  const view = renderPanel({ preview: current });
  expect(screen.queryByText(/fixed/)).toBeNull();
  expect(screen.queryByRole("textbox")).toBeNull();
  view.rerender(<SourceCorrectionPanel {...view.props} preview={preview} readOnly />);
  expect(screen.queryByRole("textbox")).toBeNull();
  expect(api.correctAccessCode).not.toHaveBeenCalled();
});

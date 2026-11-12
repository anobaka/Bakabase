import type { SourceRevisionOptions, SourceRevisionPreview } from "../sourceRevisionApi";

import { Children, type ReactElement, type ReactNode } from "react";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";

import SourceRevisionModal from "../SourceRevisionModal";
import { sourceRevisionReasonCodes, sourceRevisionReasonKey } from "../sourceRevisionReasons";

import cn from "@/locales/cn/pages/acquisition.json";
import en from "@/locales/en/pages/acquisition.json";

const api = vi.hoisted(() => ({ options: vi.fn(), preview: vi.fn(), apply: vi.fn() }));

vi.mock("../sourceRevisionApi", () => ({ sourceRevisionApi: api }));
vi.mock("../../MethodPreparation", () => ({
  default: ({ onRefresh, accountSelectionDisabled }: any) => (
    <button data-account-locked={accountSelectionDisabled} onClick={onRefresh}>
      configure-and-check-again
    </button>
  ),
}));
vi.mock("@/components/ThirdPartyIcon", () => ({ default: () => <svg aria-hidden /> }));
vi.mock("@heroui/react", () => ({ SelectItem: () => null }));
vi.mock("@/components/bakaui", () => ({
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  Button: ({
    children,
    onPress,
    isDisabled,
    isLoading,
  }: {
    children: ReactNode;
    onPress?: () => void;
    isDisabled?: boolean;
    isLoading?: boolean;
  }) => (
    <button disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Modal: ({
    visible,
    title,
    children,
    footer,
  }: {
    visible: boolean;
    title: string;
    children: ReactNode;
    footer: ReactNode;
  }) =>
    visible ? (
      <div aria-label={title} role="dialog">
        {children}
        <footer>{footer}</footer>
      </div>
    ) : null,
  Spinner: () => <span role="progressbar" />,
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Select: ({
    label,
    children,
    selectedKeys,
    onSelectionChange,
    isDisabled,
  }: {
    label: string;
    children: ReactNode;
    selectedKeys: string[];
    onSelectionChange: (keys: Set<string>) => void;
    isDisabled?: boolean;
  }) => (
    <select
      aria-label={label}
      disabled={isDisabled}
      value={selectedKeys[0] ?? ""}
      onChange={(event) =>
        onSelectionChange(new Set(event.target.value ? [event.target.value] : []))
      }
    >
      <option value="" />
      {Children.map(children, (child) => {
        const item = child as ReactElement<{ textValue: string }>;

        return <option value={String(item.key)}>{item.props.textValue}</option>;
      })}
    </select>
  ),
}));
vi.mock("../../AcquisitionSourceDetails", () => ({
  default: ({ source, health }: { source: { url: string }; health?: { status: string } }) => (
    <div>
      {source.url}
      {health && <span>{health.status}</span>}
    </div>
  ),
}));
vi.mock("@/components/ProcessingPlanRevisions/PlanSummary", () => ({
  default: ({ plan }: { plan?: { steps: { op: string }[] } }) => (
    <div>{plan?.steps.map((step) => step.op).join(",") || "no processing"}</div>
  ),
}));

const original = {
  id: "old",
  url: "https://mypikpak.com/s/source",
  kind: "cloud",
  providerId: "pikpak",
};
const mirror = {
  id: "mirror",
  url: "https://pan.baidu.com/s/mirror",
  kind: "cloud",
  providerId: "baidu",
};
const second = {
  id: "second",
  url: "https://mega.nz/file/other",
  kind: "cloud",
  providerId: "mega",
};
const options = (): SourceRevisionOptions => ({
  taskId: 42,
  expectedRevision: 3,
  expectedRunId: 75,
  canRevise: true,
  currentSource: original,
  candidates: [
    {
      id: "c1",
      partId: "main",
      source: mirror,
      sourceRefs: [],
      evidence: "Confirmed same content",
      evidenceVersion: "evidence-1",
    },
    {
      id: "c2",
      partId: "main",
      source: second,
      sourceRefs: [],
      evidence: "Another confirmed mirror",
      evidenceVersion: "evidence-2",
    },
  ],
});
const preview = (overrides: Partial<SourceRevisionPreview> = {}): SourceRevisionPreview => ({
  taskId: 42,
  expectedRevision: 3,
  expectedRunId: 75,
  partId: "main",
  previewToken: "fixed-token",
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
  canApply: true,
  currentSource: original,
  proposedSource: mirror,
  currentProcessing: {
    requirement: "required",
    steps: [{ id: "extract", op: "extractArchive", input: "download" }],
    evidence: [],
  },
  proposedProcessing: { requirement: "notRequired", steps: [], evidence: [] },
  processingChanged: true,
  evidence: "Confirmed same content",
  preflight: { mode: "files", candidates: [], requirements: [], canSubmit: true },
  ...overrides,
});
const choose = (id = "c1") =>
  fireEvent.change(screen.getByRole("combobox"), {
    target: { value: JSON.stringify(["main", id]) },
  });
const check = () =>
  fireEvent.click(screen.getByRole("button", { name: "acquisition.sourceRevision.preview" }));
const applyButton = () => screen.getByRole("button", { name: "acquisition.sourceRevision.apply" });

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: Error) => void;
  const promise = new Promise<T>((yes, no) => {
    resolve = yes;
    reject = no;
  });

  return { promise, resolve, reject };
}

beforeEach(() => {
  vi.clearAllMocks();
  api.options.mockResolvedValue(options());
  api.preview.mockResolvedValue(preview());
  api.apply.mockResolvedValue({ taskId: 42, planRevision: 4, previousRunId: 75, applied: true });
});
afterEach(() => vi.useRealTimers());

it("opens read-only and displays why a running task cannot change sources", async () => {
  api.options.mockResolvedValue({
    ...options(),
    canRevise: false,
    reasonCode: "ACQUISITION_TASK_ACTIVE",
  });
  render(<SourceRevisionModal taskId={42} />);
  expect(
    await screen.findByText("acquisition.sourceRevision.reason.ACQUISITION_TASK_ACTIVE"),
  ).toBeVisible();
  choose();
  expect(screen.getByText(original.url)).toBeVisible();
  expect(screen.getByText(mirror.url)).toBeVisible();
  expect(screen.getByRole("button", { name: "acquisition.sourceRevision.preview" })).toBeDisabled();
  expect(applyButton()).toBeDisabled();
  expect(api.preview).not.toHaveBeenCalled();
  expect(api.apply).not.toHaveBeenCalled();
});

it("requires an explicit frozen preview and applies its token once without starting a new run", async () => {
  const applying = deferred<{
    taskId: number;
    planRevision: number;
    previousRunId: number;
    applied: boolean;
  }>();

  api.apply.mockReturnValue(applying.promise);
  const changed = vi.fn();

  render(<SourceRevisionModal taskId={42} onChanged={changed} />);
  await screen.findByRole("combobox");
  expect(applyButton()).toBeDisabled();
  choose();
  expect(api.preview).not.toHaveBeenCalled();
  check();
  await waitFor(() => expect(applyButton()).toBeEnabled());
  expect(api.preview).toHaveBeenCalledExactlyOnceWith(
    42,
    {
      expectedRevision: 3,
      expectedRunId: 75,
      partId: "main",
      candidateId: "c1",
    },
    expect.any(AbortSignal),
  );
  expect(screen.getByText("acquisition.sourceRevision.processingChanged")).toBeVisible();
  expect(screen.getByText("extractArchive")).toBeVisible();
  fireEvent.click(applyButton());
  fireEvent.click(applyButton());
  expect(api.apply).toHaveBeenCalledExactlyOnceWith(42, "fixed-token");
  expect(screen.getByRole("button", { name: "acquisition.plan.close" })).toBeDisabled();
  await act(async () =>
    applying.resolve({ taskId: 42, planRevision: 4, previousRunId: 75, applied: true }),
  );
  expect(changed).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole("dialog")).toBeNull();
});

it("rechecks a blocked source after in-place account preparation and only applies the new preview", async () => {
  api.preview
    .mockResolvedValueOnce(
      preview({
        canApply: false,
        reasonCode: "SOURCE_REVISION_SOURCE_NOT_READY",
        preflight: {
          mode: "files",
          canSubmit: false,
          requirements: [
            {
              code: "TERABOX_ACCOUNT_REQUIRED",
              kind: "account",
              message: "Connect",
              accountProvider: "terabox",
            },
          ],
          candidates: [
            {
              candidate: { id: mirror.id, url: mirror.url, kind: "share" },
              methods: [],
              requirements: [],
              state: "needsSetup",
              canSelect: false,
            },
          ],
        },
      }),
    )
    .mockResolvedValueOnce(preview({ previewToken: "after-authorization" }));
  render(<SourceRevisionModal taskId={42} />);
  await screen.findByRole("combobox");
  choose();
  check();
  const setup = await screen.findByText("configure-and-check-again");
  expect(applyButton()).toBeDisabled();
  expect(setup).toHaveAttribute("data-account-locked", "true");
  fireEvent.click(setup);
  await waitFor(() => expect(applyButton()).toBeEnabled());
  expect(api.preview).toHaveBeenCalledTimes(2);
  fireEvent.click(applyButton());
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith(42, "after-authorization"));
});

it("discards a pending preview when another candidate is selected", async () => {
  const first = deferred<SourceRevisionPreview>();
  const next = deferred<SourceRevisionPreview>();

  api.preview.mockReturnValueOnce(first.promise).mockReturnValueOnce(next.promise);
  render(<SourceRevisionModal taskId={42} />);
  await screen.findByRole("combobox");
  choose();
  check();
  choose("c2");
  expect(applyButton()).toBeDisabled();
  check();
  await act(async () =>
    next.resolve(preview({ previewToken: "second-token", proposedSource: second })),
  );
  expect(applyButton()).toBeEnabled();
  await act(async () => first.resolve(preview()));
  expect(screen.queryByText(mirror.url)).toBeNull();
  fireEvent.click(applyButton());
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith(42, "second-token"));
});

it("does not display an old task response after the task changes", async () => {
  const old = deferred<SourceRevisionOptions>();

  api.options.mockReturnValueOnce(old.promise).mockResolvedValueOnce({
    ...options(),
    taskId: 43,
    canRevise: false,
    candidates: [],
    reasonCode: "SOURCE_REVISION_NO_MIRRORS",
  });
  const view = render(<SourceRevisionModal taskId={42} />);

  view.rerender(<SourceRevisionModal taskId={43} />);
  await screen.findByText("acquisition.sourceRevision.noCandidates");
  await act(async () => old.resolve(options()));
  expect(screen.queryByRole("combobox")).toBeNull();
  expect(applyButton()).toBeDisabled();
  expect(api.preview).not.toHaveBeenCalled();
});

it("expires a preview without automatically rechecking or applying", async () => {
  vi.useFakeTimers();
  api.preview.mockResolvedValue(preview({ expiresAt: new Date(Date.now() + 5000).toISOString() }));
  render(<SourceRevisionModal taskId={42} />);
  await act(async () => {});
  choose();
  check();
  await act(async () => {});
  expect(applyButton()).toBeEnabled();
  await act(async () => vi.advanceTimersByTimeAsync(5001));
  expect(screen.getByText("acquisition.sourceRevision.expired")).toBeVisible();
  expect(applyButton()).toBeDisabled();
  expect(api.preview).toHaveBeenCalledTimes(1);
  expect(api.apply).not.toHaveBeenCalled();
});

it.each([
  new Error("network error: private-token-canary"),
  new Error("SOURCE_REVISION_OUTCOME_UNKNOWN"),
])(
  "locks an unknown apply outcome until the user refreshes instead of retrying",
  async (failure) => {
    api.apply.mockRejectedValue(failure);
    render(<SourceRevisionModal taskId={42} />);
    await screen.findByRole("combobox");
    choose();
    check();
    await waitFor(() => expect(applyButton()).toBeEnabled());
    fireEvent.click(applyButton());
    await screen.findByRole("alert");
    expect(document.body).not.toHaveTextContent("private-token-canary");
    expect(applyButton()).toBeDisabled();
    expect(screen.getByRole("combobox")).toBeDisabled();
    expect(
      screen.getByRole("button", { name: "acquisition.sourceRevision.preview" }),
    ).toBeDisabled();
    expect(api.apply).toHaveBeenCalledTimes(1);
    expect(api.preview).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole("button", { name: "acquisition.sourceRevision.refresh" }));
    await waitFor(() => expect(api.options).toHaveBeenCalledTimes(2));
    expect(applyButton()).toBeDisabled();
    expect(api.apply).toHaveBeenCalledTimes(1);
  },
);

it("keeps a blocked remote-operation preview non-executable and hides unknown raw requirements", async () => {
  api.preview.mockResolvedValue(
    preview({
      canApply: false,
      reasonCode: "PIKPAK_RESTORE_REVIEW_REQUIRED",
      preflight: {
        mode: "files",
        canSubmit: false,
        candidates: [],
        requirements: [
          { code: "UNDOCUMENTED_SECRET_ERROR", kind: "source", message: "private-token-canary" },
        ],
      },
    }),
  );
  render(<SourceRevisionModal taskId={42} />);
  await screen.findByRole("combobox");
  choose();
  check();
  expect(
    await screen.findByText("acquisition.sourceRevision.reason.PIKPAK_RESTORE_REVIEW_REQUIRED"),
  ).toBeVisible();
  expect(applyButton()).toBeDisabled();
  expect(screen.getByText("acquisition.sourceRevision.requirementUnknown")).toBeVisible();
  expect(document.body).not.toHaveTextContent("UNDOCUMENTED_SECRET_ERROR");
  expect(document.body).not.toHaveTextContent("private-token-canary");
  expect(api.apply).not.toHaveBeenCalled();
});

it("translates every known replacement reason in both languages and uses a safe fallback", () => {
  for (const code of sourceRevisionReasonCodes) {
    const key = sourceRevisionReasonKey(code);

    expect((cn as Record<string, string>)[key]).toBeTruthy();
    expect((en as Record<string, string>)[key]).toBeTruthy();
  }
  expect(sourceRevisionReasonKey("new-provider-error-with-private-data")).toBe(
    "acquisition.sourceRevision.reason.unknown",
  );
});

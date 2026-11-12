import type { ReactNode } from "react";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import SourceProcessingPlanPanel from "../SourceProcessingPlanPanel";

const mocks = vi.hoisted(() => ({ query: vi.fn() }));
vi.mock("../api", () => ({ querySourceProcessingPlan: mocks.query }));
vi.mock("@/components/bakaui", () => ({
  Accordion: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AccordionItem: ({ children, title }: { children: ReactNode; title: string }) => (
    <section aria-label={title}>{children}</section>
  ),
  Button: ({
    children,
    onPress,
    isDisabled,
    isLoading,
    ...props
  }: {
    children: ReactNode;
    onPress: () => void;
    isDisabled?: boolean;
    isLoading?: boolean;
  }) => (
    <button {...props} disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Spinner: ({ "aria-label": label }: { "aria-label"?: string }) => (
    <span role="status" aria-label={label} />
  ),
}));
const source = {
  module: "postParser",
  entityKind: "task",
  entityId: "41",
  revision: "1",
  resultKey: "0",
};
const originalPlan = { requirement: "required", steps: [], evidence: [] };
const revisedPlan = {
  requirement: "required",
  steps: [
    { id: "unpack", op: "extractArchive", input: "download", password: "private-password-canary" },
  ],
  evidence: [],
};
const revision = {
  id: "revision-id",
  revision: 1,
  sourceHash: "source-hash",
  originalPlan,
  plan: revisedPlan,
  evidence: [{ stepId: "unpack", quote: "Update the archive tool before extracting." }],
  reason: "Restore the extraction instruction stated in the post.",
  createdAt: "2026-10-21T14:00:00Z",
};
const view = {
  source,
  sourceHash: "source-hash",
  revision: 1,
  originalPlan,
  effectivePlan: revisedPlan,
  effectiveRevision: 1,
  history: [revision],
  hasStaleRevisions: false,
};
beforeEach(() => {
  vi.clearAllMocks();
  mocks.query.mockResolvedValue(view);
});
afterEach(() => vi.unstubAllGlobals());

function observeVisibility() {
  const observers: {
    callback: IntersectionObserverCallback;
    observer: IntersectionObserver;
  }[] = [];
  class Observer implements IntersectionObserver {
    readonly root = null;
    readonly rootMargin: string;
    readonly thresholds = [0];
    observe = vi.fn();
    unobserve = vi.fn();
    disconnect = vi.fn();
    takeRecords = () => [];
    constructor(callback: IntersectionObserverCallback, options?: IntersectionObserverInit) {
      this.rootMargin = options?.rootMargin ?? "0px";
      observers.push({ callback, observer: this });
    }
  }
  vi.stubGlobal("IntersectionObserver", Observer);
  return {
    observers,
    notify: (index = 0, isIntersecting = true) => {
      const { callback, observer } = observers[index];
      act(() => callback([{ isIntersecting } as IntersectionObserverEntry], observer));
    },
  };
}

it("defers offscreen source queries and loads revisions once when the panel becomes visible", async () => {
  const visibility = observeVisibility();
  const { rerender } = render(<SourceProcessingPlanPanel source={source} deferUntilVisible />);
  expect(mocks.query).not.toHaveBeenCalled();
  expect(visibility.observers[0].observer.rootMargin).toBe("600px");
  visibility.notify(0, false);
  expect(mocks.query).not.toHaveBeenCalled();
  visibility.notify();
  await screen.findByText("processingRevision.revised");
  expect(mocks.query).toHaveBeenCalledExactlyOnceWith(source);
  rerender(<SourceProcessingPlanPanel source={{ ...source }} deferUntilVisible />);
  visibility.notify();
  expect(mocks.query).toHaveBeenCalledTimes(1);
  expect(visibility.observers[0].observer.disconnect).toHaveBeenCalled();
});

it("animates a deferred panel only while an actual visible query is loading", async () => {
  const visibility = observeVisibility();
  let finish!: (value: typeof view) => void;
  mocks.query.mockImplementationOnce(() => new Promise<typeof view>((resolve) => (finish = resolve)));
  render(<SourceProcessingPlanPanel source={source} deferUntilVisible />);
  expect(screen.queryByRole("status", { name: "processingRevision.loading" })).toBeNull();
  visibility.notify();
  expect(screen.getByRole("status", { name: "processingRevision.loading" })).toBeVisible();
  await act(async () => finish(view));
  expect(screen.queryByRole("status", { name: "processingRevision.loading" })).toBeNull();
});

it("loads an explicitly opened deferred panel before a visibility callback", async () => {
  const visibility = observeVisibility();
  render(<SourceProcessingPlanPanel source={source} deferUntilVisible />);
  expect(mocks.query).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "processingRevision.inspect" }));
  expect(await screen.findByText("processingRevision.effective")).toBeVisible();
  visibility.notify();
  expect(mocks.query).toHaveBeenCalledExactlyOnceWith(source);
});

it("loads deferred panels eagerly when IntersectionObserver is unavailable", async () => {
  vi.stubGlobal("IntersectionObserver", undefined);
  render(<SourceProcessingPlanPanel source={source} deferUntilVisible />);
  await screen.findByText("processingRevision.revised");
  expect(mocks.query).toHaveBeenCalledExactlyOnceWith(source);
});

it("queries only the latest source identity if it changes before the deferred panel is visible", async () => {
  const visibility = observeVisibility();
  const { rerender } = render(<SourceProcessingPlanPanel source={source} deferUntilVisible />);
  const latestSource = { ...source, revision: "2" };
  rerender(<SourceProcessingPlanPanel source={latestSource} deferUntilVisible />);
  expect(mocks.query).not.toHaveBeenCalled();
  visibility.notify();
  await screen.findByText("processingRevision.revised");
  expect(mocks.query).toHaveBeenCalledExactlyOnceWith(latestSource);
});

it("reloads a visible panel when the source changes and ignores its obsolete request", async () => {
  const visibility = observeVisibility();
  let finishOriginal!: (value: typeof view) => void;
  let finishLatest!: (value: typeof view) => void;
  mocks.query
    .mockImplementationOnce(() => new Promise<typeof view>((resolve) => (finishOriginal = resolve)))
    .mockImplementationOnce(() => new Promise<typeof view>((resolve) => (finishLatest = resolve)));
  const { rerender } = render(<SourceProcessingPlanPanel source={source} deferUntilVisible />);
  visibility.notify();
  expect(mocks.query).toHaveBeenCalledTimes(1);
  const latestSource = { ...source, revision: "2" };
  rerender(<SourceProcessingPlanPanel source={latestSource} deferUntilVisible />);
  expect(mocks.query).toHaveBeenLastCalledWith(latestSource);
  fireEvent.click(screen.getByRole("button", { name: "processingRevision.inspect" }));
  const latestReason = "Evidence for the latest saved source";
  await act(async () =>
    finishLatest({
      ...view,
      source: latestSource,
      history: [{ ...revision, reason: latestReason }],
    }),
  );
  expect(screen.getAllByText(latestReason).length).toBeGreaterThan(0);
  await act(async () => finishOriginal(view));
  expect(screen.queryAllByText(revision.reason)).toHaveLength(0);
  expect(screen.getAllByText(latestReason).length).toBeGreaterThan(0);
  expect(mocks.query).toHaveBeenCalledTimes(2);
});

it("shows original and effective instructions with evidence without changing a ready task automatically", async () => {
  const apply = vi.fn().mockResolvedValue(undefined);
  render(<SourceProcessingPlanPanel source={source} currentPlan={originalPlan} onApply={apply} />);
  await screen.findByText("processingRevision.revised");
  expect(mocks.query).toHaveBeenCalledExactlyOnceWith(source);
  expect(apply).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "processingRevision.inspect" }));
  expect(screen.getByText("processingRevision.original")).toBeVisible();
  expect(screen.getByText("processingRevision.effective")).toBeVisible();
  expect(screen.getAllByText(revision.evidence[0].quote).length).toBeGreaterThan(0);
  expect(screen.getAllByText(revision.reason).length).toBeGreaterThan(0);
  expect(document.body).not.toHaveTextContent("private-password-canary");
  fireEvent.click(screen.getByRole("button", { name: "processingRevision.apply" }));
  await waitFor(() => expect(apply).toHaveBeenCalledExactlyOnceWith("revision-id"));
});

it("preserves a frozen task and makes clear that source revisions do not update its run", async () => {
  const apply = vi.fn();
  render(
    <SourceProcessingPlanPanel source={source} currentPlan={originalPlan} frozen onApply={apply} />,
  );
  await screen.findByText("processingRevision.revised");
  fireEvent.click(screen.getByRole("button", { name: "processingRevision.inspect" }));
  expect(screen.getByText("processingRevision.frozenHint")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "processingRevision.apply" }),
  ).not.toBeInTheDocument();
  expect(apply).not.toHaveBeenCalled();
});

it("shows stale history but never offers it as an effective revision", async () => {
  mocks.query.mockResolvedValue({
    ...view,
    sourceHash: "changed-source",
    effectivePlan: originalPlan,
    effectiveRevision: 0,
    hasStaleRevisions: true,
  });
  render(<SourceProcessingPlanPanel source={source} onApply={vi.fn()} />);
  await waitFor(() => expect(mocks.query).toHaveBeenCalled());
  fireEvent.click(screen.getByRole("button", { name: "processingRevision.inspect" }));
  expect(await screen.findByText("processingRevision.stale")).toBeVisible();
  expect(screen.getByText("processingRevision.outdated")).toBeVisible();
  expect(screen.queryByText("processingRevision.revised")).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "processingRevision.apply" }),
  ).not.toBeInTheDocument();
});

it("does not reapply the revision already recorded on this task", async () => {
  render(
    <SourceProcessingPlanPanel
      source={source}
      currentPlan={revisedPlan}
      appliedRevisionId="revision-id"
      onApply={vi.fn()}
    />,
  );
  await screen.findByText("processingRevision.applied");
  fireEvent.click(screen.getByRole("button", { name: "processingRevision.inspect" }));
  expect(
    screen.queryByRole("button", { name: "processingRevision.apply" }),
  ).not.toBeInTheDocument();
  expect(screen.getByText("processingRevision.appliedHint")).toBeVisible();
});

it("retains audit evidence after the source changes without offering a stale effective plan", async () => {
  mocks.query.mockResolvedValue({
    ...view,
    sourceAvailable: false,
    sourceProblem: "PROCESSING_PLAN_SOURCE_CHANGED",
    sourceHash: "",
    effectiveRevision: 0,
    effectivePlan: null,
    hasStaleRevisions: true,
  });
  render(
    <SourceProcessingPlanPanel
      source={source}
      frozen
      currentPlan={originalPlan}
      onApply={vi.fn()}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "processingRevision.inspect" }));
  expect(
    await screen.findByText("processingRevision.problem.PROCESSING_PLAN_SOURCE_CHANGED"),
  ).toBeVisible();
  expect(screen.getByText(revision.evidence[0].quote)).toBeVisible();
  expect(screen.getByText("processingRevision.noEffectiveSource")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "processingRevision.apply" }),
  ).not.toBeInTheDocument();
});

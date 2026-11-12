import type { ComponentProps } from "react";
import type { PostContentSnapshot, PostParserTask } from "@/core/models/PostParserTask";
import type { ContentReadinessView } from "../contentReadiness";

import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ContentReadinessReviewModal from "../components/ContentReadinessReviewModal";
import ActualModal from "@/components/bakaui/components/Modal";
import cn from "@/locales/cn/pages/postParser.json";
import { PostParseTarget, WorkflowRunStatus } from "@/sdk/constants";

const api = vi.hoisted(() => ({
  get: vi.fn(),
  refresh: vi.fn(),
  confirm: vi.fn(),
  revoke: vi.fn(),
  purchase: vi.fn(),
  purchaseSelection: vi.fn(),
  reparse: vi.fn(),
  changed: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    postParser: {
      getPostParserContentReadiness: api.get,
      refreshPostParserContentReadiness: api.refresh,
      confirmPostParserContentReadiness: api.confirm,
      revokePostParserContentReadiness: api.revoke,
      purchasePostParserTaskContent: api.purchase,
      confirmPostParserTaskPurchases: api.purchaseSelection,
      reParsePostParserTask: api.reparse,
    },
  },
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Modal: (props: ComponentProps<typeof ActualModal>) => <ActualModal {...props} />,
}));
function translate(key: string, values: Record<string, unknown> = {}) {
  return ((cn as Record<string, string>)[key] ?? key).replace(/\{\{(\w+)\}\}/g, (_, name) =>
    String(values[name] ?? ""),
  );
}
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: translate }) }));
vi.mock("../components/PostDetails", () => ({
  default: ({ task }: { task: PostParserTask }) => <p>{task.contentSnapshot?.mainHtml}</p>,
}));
vi.mock("../components/DownloadInfoResultRenderer", () => ({
  default: ({ data }: { data: { title?: string } }) => <p>{data.title}</p>,
}));

const label = (key: string, values: Record<string, unknown> = {}) =>
  translate(`postParser.contentReadiness.${key}`, values);
const freshContent = (): PostContentSnapshot => ({
  mainHtml: "fresh saved post body",
  currencyCode: "SP",
  currencyName: "SP币",
  locks: [
    {
      blockId: "fresh:block-a",
      url: "https://example.test/purchase/shared",
      priceText: "5",
      contextText: "fresh unpaid block A",
      isBought: false,
    },
    {
      blockId: "fresh:block-b",
      url: "https://example.test/purchase/shared",
      priceText: "5",
      contextText: "fresh unpaid block B",
      isBought: false,
    },
    { blockId: "already-paid", priceText: "10", contextText: "paid content", isBought: true },
  ],
});
const task = (updates: Partial<PostParserTask> = {}): PostParserTask => ({
  id: 41,
  source: 0,
  revision: 3,
  workflowRunId: 30,
  workflowStatus: WorkflowRunStatus.Waiting,
  link: "https://example.test/post/41",
  targets: [PostParseTarget.DownloadInfo],
  contentSnapshot: {
    mainHtml: "old row post body",
    locks: [
      { id: "old-row-url-id", price: 99, contextText: "old row unpaid block", isBought: false },
    ],
  },
  ...updates,
});
const view = (updates: Partial<ContentReadinessView> = {}): ContentReadinessView => ({
  taskId: 41,
  revision: 8,
  workflowRunId: 80,
  contentSnapshot: freshContent(),
  source: "SoulPlus",
  contentFingerprint: "fresh-content-fingerprint",
  decisionVersion: 4,
  status: "needsUnlock",
  hasStaleDecision: false,
  canConfirm: true,
  canRevoke: false,
  canContinue: true,
  canRefresh: false,
  parsedResultMayBeStale: false,
  history: [],
  ...updates,
});
const legacyView = (updates: Partial<ContentReadinessView> = {}): ContentReadinessView =>
  view({
    revision: 3,
    workflowRunId: 30,
    contentFingerprint: "a".repeat(64),
    contentSnapshot: {
      ...freshContent(),
      mainHtml: "legacy saved post body",
      locks: freshContent().locks.map((lock) => ({ ...lock, blockId: undefined })),
    },
    canConfirm: false,
    canContinue: false,
    canRefresh: true,
    ...updates,
  });
const refreshedView = (updates: Partial<ContentReadinessView> = {}): ContentReadinessView =>
  view({
    revision: 3,
    workflowRunId: 30,
    contentFingerprint: "b".repeat(64),
    canRefresh: true,
    parsedResultMayBeStale: true,
    ...updates,
  });
type ReadinessResponse = { code: number; data?: ContentReadinessView; message?: string };
const response = (data = view()): ReadinessResponse => ({ code: 0, data });
const deferred = () => {
  let resolve!: (value: ReadinessResponse) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<ReadinessResponse>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
};
const show = (props: Partial<ComponentProps<typeof ContentReadinessReviewModal>> = {}) =>
  render(
    <HeroUIProvider disableAnimation>
      <ContentReadinessReviewModal task={task()} onChanged={api.changed} {...props} />
    </HeroUIProvider>,
  );
const confirmButton = () =>
  screen.getByRole("button", {
    name: (name) => name === label("confirm") || name === label("confirmAndContinue"),
  });
const reviewedCheckbox = () =>
  screen.getByRole("checkbox", { name: label("reviewed", { count: 2 }) });
const refreshButton = () => screen.getByRole("button", { name: "刷新正文后审核" });
const acknowledge = async () => {
  await waitFor(() => expect(reviewedCheckbox()).toBeEnabled());
  const user = userEvent.setup();
  await user.click(reviewedCheckbox());
  return user;
};
const expectNoPurchasesOrReparse = () => {
  expect(api.purchase).not.toHaveBeenCalled();
  expect(api.purchaseSelection).not.toHaveBeenCalled();
  expect(api.reparse).not.toHaveBeenCalled();
};

beforeEach(() => {
  api.get.mockReset().mockResolvedValue(response());
  api.refresh.mockReset().mockResolvedValue(response());
  api.confirm.mockReset().mockResolvedValue({ code: 0 });
  api.revoke.mockReset().mockResolvedValue({ code: 0 });
  api.purchase.mockReset();
  api.purchaseSelection.mockReset();
  api.reparse.mockReset();
  api.changed.mockReset();
});
afterEach(cleanup);

it("keeps evidence and audit details available without exposing a second confirmation flow", async () => {
  api.get.mockResolvedValue(response(view({ canRevoke: true, activeDecisionId: "old-decision" })));
  show({ auditOnly: true });
  expect(await screen.findByRole("button", { name: label("revoke") })).toBeVisible();
  expect(screen.getByText(label("existingContent"))).toBeVisible();
  expect(screen.queryByRole("checkbox")).toBeNull();
  expect(screen.queryByRole("button", { name: label("confirmAndContinue") })).toBeNull();
  expect(screen.queryByRole("button", { name: label("confirm") })).toBeNull();
  expect(api.confirm).not.toHaveBeenCalled();
  expectNoPurchasesOrReparse();
});

describe("refreshing legacy content before review", () => {
  it("explains missing block IDs even when confirmation is unavailable and offers the explicit read-only refresh", async () => {
    api.get.mockResolvedValue(response(legacyView()));
    show();
    expect(await screen.findByText(label("blockIdsUnavailable"))).toBeVisible();
    expect(refreshButton()).toBeEnabled();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(screen.queryByRole("button", { name: label("confirmAndContinue") })).toBeNull();
    expect(screen.queryByRole("button", { name: label("confirm") })).toBeNull();
    expect(api.refresh).not.toHaveBeenCalled();
    expect(api.confirm).not.toHaveBeenCalled();
    expectNoPurchasesOrReparse();
  });

  it("binds refresh and the subsequent explicit review to their respective fixed snapshots", async () => {
    const refreshed = refreshedView();
    const before = structuredClone(refreshed.contentSnapshot);
    api.get.mockResolvedValue(response(legacyView()));
    api.refresh.mockResolvedValue(response(refreshed));
    show({
      task: task({
        results: { DownloadInfo: { title: "parsed result from the old body", resources: [] } },
      }),
    });
    await screen.findByText(label("blockIdsUnavailable"));
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: label("existingContent") }));
    expect(screen.getByText("legacy saved post body")).toBeVisible();
    expect(screen.getByText("parsed result from the old body")).toBeVisible();
    await user.click(refreshButton());
    await waitFor(() => expect(reviewedCheckbox()).toBeEnabled());
    expect(api.refresh).toHaveBeenCalledOnce();
    expect(api.refresh.mock.calls[0][0]).toBe(41);
    expect(api.refresh.mock.calls[0][1]).toEqual({
      revision: 3,
      workflowRunId: 30,
      expectedContentFingerprint: "a".repeat(64),
      expectedDecisionVersion: 4,
    });
    expect(api.refresh.mock.calls[0][2]).toEqual({
      sensitiveResponse: true,
      showErrorToast: false,
    });
    expect(api.get).toHaveBeenCalledOnce();
    expect(api.changed).toHaveBeenCalledExactlyOnceWith({ action: "refresh", continued: false });
    expect(screen.queryByText("legacy saved post body")).toBeNull();
    expect(screen.getByText("fresh saved post body")).toBeVisible();
    expect(screen.queryByText("parsed result from the old body")).toBeNull();
    expect(screen.getByText(label("oldResults"))).toBeVisible();
    expect(reviewedCheckbox()).not.toBeChecked();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(confirmButton()).toBeDisabled();
    const blockDetails = screen.getByRole("region", {
      name: translate("postParser.lockedContent.title"),
    });
    expect(
      reviewedCheckbox().compareDocumentPosition(blockDetails) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(api.confirm).not.toHaveBeenCalled();
    expect(api.revoke).not.toHaveBeenCalled();
    expectNoPurchasesOrReparse();

    await acknowledge();
    expect(confirmButton()).toHaveTextContent("无需购买，继续解析");
    await user.click(confirmButton());
    await waitFor(() => expect(api.confirm).toHaveBeenCalledOnce());
    expect(api.confirm).toHaveBeenCalledWith(41, {
      revision: 3,
      workflowRunId: 30,
      expectedContentFingerprint: "b".repeat(64),
      expectedDecisionVersion: 4,
      notRequiredBlockIds: ["fresh:block-a", "fresh:block-b"],
      continueParsing: true,
    });
    expect(refreshed.contentSnapshot).toEqual(before);
    expect(refreshed.contentSnapshot?.locks.map((lock) => lock.isBought)).toEqual([
      false,
      false,
      true,
    ]);
    expectNoPurchasesOrReparse();
  });

  it("sends one refresh on repeated clicks and locks dismissal while that read is pending", async () => {
    const pending = deferred();
    api.get.mockResolvedValue(response(legacyView()));
    api.refresh.mockReturnValue(pending.promise);
    show();
    await screen.findByText(label("blockIdsUnavailable"));
    const refresh = refreshButton();
    act(() => {
      refresh.click();
      refresh.click();
    });
    expect(api.refresh).toHaveBeenCalledOnce();
    expect(refresh).toBeDisabled();
    expect(screen.getByRole("button", { name: label("close") })).toBeDisabled();
    await userEvent.setup().keyboard("{Escape}");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(api.confirm).not.toHaveBeenCalled();
    await act(async () => pending.resolve(response(refreshedView())));
    await waitFor(() => expect(reviewedCheckbox()).toBeEnabled());
    expect(api.changed).toHaveBeenCalledExactlyOnceWith({ action: "refresh", continued: false });
    expectNoPurchasesOrReparse();
  });

  it("explains that fully unlocked refreshed content needs no manual confirmation or automatic continuation", async () => {
    api.get.mockResolvedValue(response(legacyView()));
    api.refresh.mockResolvedValue(
      response(
        refreshedView({
          status: "sufficient",
          canConfirm: false,
          canContinue: false,
          contentSnapshot: {
            ...freshContent(),
            locks: freshContent().locks.map((lock) => ({ ...lock, isBought: true })),
          },
        }),
      ),
    );
    show();
    await screen.findByText(label("blockIdsUnavailable"));
    await userEvent.setup().click(refreshButton());
    expect(await screen.findByRole("status")).toHaveTextContent(label("noUnlockRequired"));
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(screen.queryByRole("button", { name: label("confirmAndContinue") })).toBeNull();
    expect(screen.queryByRole("button", { name: label("confirm") })).toBeNull();
    expect(api.changed).toHaveBeenCalledExactlyOnceWith({ action: "refresh", continued: false });
    expect(api.confirm).not.toHaveBeenCalled();
    expect(api.revoke).not.toHaveBeenCalled();
    expectNoPurchasesOrReparse();
  });

  it("requires a new explicit review after refresh even if the server reports an unchanged fingerprint", async () => {
    const previous = legacyView();
    api.get.mockResolvedValue(response(previous));
    api.refresh.mockResolvedValue(
      response(refreshedView({ contentFingerprint: previous.contentFingerprint })),
    );
    show();
    await screen.findByText(label("blockIdsUnavailable"));
    await userEvent.setup().click(refreshButton());
    await waitFor(() => expect(api.changed).toHaveBeenCalledOnce());
    expect(reviewedCheckbox()).not.toBeChecked();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(confirmButton()).toBeDisabled();
    expect(api.confirm).not.toHaveBeenCalled();
    expectNoPurchasesOrReparse();
  });

  it("does not launch a competing GET retry in the same frame as a source refresh", async () => {
    const pending = deferred();
    api.get.mockResolvedValue(response(legacyView()));
    api.refresh
      .mockRejectedValueOnce(new Error("Initial source read failed."))
      .mockReturnValueOnce(pending.promise);
    show();
    await screen.findByText(label("blockIdsUnavailable"));
    await userEvent.setup().click(refreshButton());
    await screen.findByRole("alert");
    const retry = screen.getByRole("button", { name: label("reloadState") });
    const refresh = refreshButton();
    act(() => {
      refresh.click();
      retry.click();
    });
    expect(api.refresh).toHaveBeenCalledTimes(2);
    expect(api.get).toHaveBeenCalledOnce();
    await act(async () => pending.resolve(response(refreshedView())));
    await waitFor(() => expect(reviewedCheckbox()).toBeEnabled());
    expect(screen.getByRole("button", { name: label("close") })).toBeEnabled();
    expect(api.changed).toHaveBeenCalledExactlyOnceWith({ action: "refresh", continued: false });
    expect(api.confirm).not.toHaveBeenCalled();
    expectNoPurchasesOrReparse();
  });

  it.each(["rejected request", "unsuccessful response"] as const)(
    "retains the old body without allowing confirmation after a refresh %s",
    async (failure) => {
      api.get.mockResolvedValue(response(legacyView()));
      if (failure === "rejected request")
        api.refresh.mockRejectedValueOnce(new Error("Source read failed."));
      else api.refresh.mockResolvedValueOnce({ code: 409, message: "Source read failed." });
      show();
      await screen.findByText(label("blockIdsUnavailable"));
      const user = userEvent.setup();
      await user.click(screen.getByRole("button", { name: label("existingContent") }));
      await user.click(refreshButton());
      expect(await screen.findByRole("alert")).toHaveTextContent("Source read failed.");
      expect(screen.getByText("legacy saved post body")).toBeVisible();
      expect(screen.queryByText("fresh saved post body")).toBeNull();
      expect(refreshButton()).toBeEnabled();
      expect(screen.queryByRole("textbox")).toBeNull();
      expect(screen.queryByRole("checkbox")).toBeNull();
      expect(screen.queryByRole("button", { name: label("confirmAndContinue") })).toBeNull();
      expect(api.get).toHaveBeenCalledOnce();
      expect(api.refresh).toHaveBeenCalledOnce();
      expect(api.confirm).not.toHaveBeenCalled();
      expect(api.changed).not.toHaveBeenCalled();
      expectNoPurchasesOrReparse();
    },
  );

  it("disables refresh when the server has not granted it, while still explaining missing IDs", async () => {
    api.get.mockResolvedValue(response(legacyView({ canRefresh: false })));
    show();
    expect(await screen.findByText(label("blockIdsUnavailable"))).toBeVisible();
    expect(refreshButton()).toBeDisabled();
    expect(screen.getByText(label("refreshUnavailable"))).toBeVisible();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(api.refresh).not.toHaveBeenCalled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("rejects a refresh result belonging to a different task", async () => {
    api.get.mockResolvedValue(response(legacyView()));
    api.refresh.mockResolvedValue(response(refreshedView({ taskId: 42 })));
    show();
    await screen.findByText(label("blockIdsUnavailable"));
    await userEvent.setup().click(refreshButton());
    expect(await screen.findByRole("alert")).toBeVisible();
    await userEvent.setup().click(screen.getByRole("button", { name: label("existingContent") }));
    expect(screen.getByText("legacy saved post body")).toBeVisible();
    expect(screen.queryByText("fresh saved post body")).toBeNull();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(api.changed).not.toHaveBeenCalled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it.each(["success", "failure"] as const)(
    "ignores a late refresh %s after unmount without notifying or starting work",
    async (outcome) => {
      const pending = deferred();
      api.get.mockResolvedValue(response(legacyView()));
      api.refresh.mockReturnValue(pending.promise);
      const modal = show();
      await screen.findByText(label("blockIdsUnavailable"));
      await userEvent.setup().click(refreshButton());
      modal.unmount();
      await act(async () => {
        if (outcome === "success") pending.resolve(response(refreshedView()));
        else pending.reject(new Error("Late source-read error."));
      });
      expect(screen.queryByRole("dialog")).toBeNull();
      expect(api.refresh).toHaveBeenCalledOnce();
      expect(api.get).toHaveBeenCalledOnce();
      expect(api.changed).not.toHaveBeenCalled();
      expect(api.confirm).not.toHaveBeenCalled();
      expect(api.revoke).not.toHaveBeenCalled();
      expectNoPurchasesOrReparse();
    },
  );
});

describe("explicit content sufficiency review", () => {
  it("requires only the explicit review acknowledgement and submits no user reason", async () => {
    show();
    await waitFor(() => expect(reviewedCheckbox()).toBeEnabled());
    const user = userEvent.setup();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(confirmButton()).toBeDisabled();
    await user.click(reviewedCheckbox());
    expect(confirmButton()).toBeEnabled();
    await user.click(reviewedCheckbox());
    expect(confirmButton()).toBeDisabled();
    expect(api.confirm).not.toHaveBeenCalled();
    await user.click(reviewedCheckbox());
    await user.click(confirmButton());
    await waitFor(() => expect(api.confirm).toHaveBeenCalledOnce());
    expect(api.confirm.mock.calls[0][1]).not.toHaveProperty("reason");
    expectNoPurchasesOrReparse();
  });

  it("focuses cancellation by default so Enter closes without confirming or purchasing", async () => {
    show();
    const cancel = await screen.findByRole("button", { name: label("close") });
    await waitFor(() => expect(cancel).toHaveFocus());
    await userEvent.setup().keyboard("{Enter}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.confirm).not.toHaveBeenCalled();
    expect(api.revoke).not.toHaveBeenCalled();
    expectNoPurchasesOrReparse();
  });

  it.each([
    { revision: 8, workflowRunId: 80 },
    { revision: 3, workflowRunId: 80 },
  ])(
    "uses the fresh GET snapshot and fixed CAS fields instead of stale row data: %j",
    async (current) => {
      const saved = view(current);
      const originalContent = structuredClone(saved.contentSnapshot);
      api.get.mockResolvedValue(response(saved));
      show({
        task: task({
          results: { DownloadInfo: { title: "stale row download result", resources: [] } },
        }),
      });
      await waitFor(() => expect(reviewedCheckbox()).toBeEnabled());
      expect(api.get).toHaveBeenCalledExactlyOnceWith(
        41,
        expect.objectContaining({ sensitiveResponse: true, showErrorToast: false }),
      );
      expect(screen.getByText("fresh unpaid block A")).toBeVisible();
      expect(screen.getByText("fresh unpaid block B")).toBeVisible();
      expect(screen.queryByText("old row unpaid block")).toBeNull();
      const user = userEvent.setup();
      await user.click(screen.getByRole("button", { name: label("existingContent") }));
      expect(screen.getByText("fresh saved post body")).toBeVisible();
      expect(screen.queryByText("old row post body")).toBeNull();
      expect(screen.queryByText("stale row download result")).toBeNull();
      expect(screen.getByText(label("oldResults"))).toBeVisible();
      await acknowledge();
      await user.click(confirmButton());
      await waitFor(() => expect(api.confirm).toHaveBeenCalledOnce());
      expect(api.confirm).toHaveBeenCalledWith(41, {
        revision: current.revision,
        workflowRunId: current.workflowRunId,
        expectedContentFingerprint: "fresh-content-fingerprint",
        expectedDecisionVersion: 4,
        notRequiredBlockIds: ["fresh:block-a", "fresh:block-b"],
        continueParsing: true,
      });
      await waitFor(() => expect(api.changed).toHaveBeenCalledOnce());
      expect(saved.contentSnapshot).toEqual(originalContent);
      expect(saved.contentSnapshot?.locks.map((lock) => lock.isBought)).toEqual([
        false,
        false,
        true,
      ]);
      expectNoPurchasesOrReparse();
    },
  );

  it("allows a Waiting task's default continuation to be turned off explicitly", async () => {
    show();
    const user = await acknowledge();
    const continuation = screen.getByRole("checkbox", { name: label("continue") });
    expect(continuation).toBeChecked();
    await user.click(continuation);
    expect(continuation).not.toBeChecked();
    expect(confirmButton()).toHaveTextContent(label("confirm"));
    await user.click(confirmButton());
    await waitFor(() => expect(api.confirm).toHaveBeenCalledOnce());
    expect(api.confirm.mock.calls[0][1].continueParsing).toBe(false);
    expectNoPurchasesOrReparse();
  });

  it.each([WorkflowRunStatus.Success, WorkflowRunStatus.Failed, WorkflowRunStatus.Pending])(
    "does not offer or implicitly enable continuation for workflow status %s",
    async (workflowStatus) => {
      api.get.mockResolvedValue(response(view({ canContinue: false })));
      show({ task: task({ workflowStatus }) });
      const user = await acknowledge();
      expect(screen.queryByRole("checkbox", { name: label("continue") })).toBeNull();
      await user.click(confirmButton());
      await waitFor(() => expect(api.confirm).toHaveBeenCalledOnce());
      expect(api.confirm.mock.calls[0][1].continueParsing).toBe(false);
      expectNoPurchasesOrReparse();
    },
  );

  it.each([false, undefined] as const)(
    "does not continue a row marked Waiting when the current view's permission is %s",
    async (canContinue) => {
      api.get.mockResolvedValue(response(view({ canContinue })));
      show({ task: task({ workflowStatus: WorkflowRunStatus.Waiting }) });
      const user = await acknowledge();
      expect(screen.queryByRole("checkbox", { name: label("continue") })).toBeNull();
      expect(confirmButton()).toHaveTextContent(label("confirm"));
      await user.click(confirmButton());
      await waitFor(() => expect(api.confirm).toHaveBeenCalledOnce());
      expect(api.confirm.mock.calls[0][1].continueParsing).toBe(false);
      expectNoPurchasesOrReparse();
    },
  );

  it("can revoke only the displayed active decision without rerunning or purchasing anything", async () => {
    const active = view({
      status: "sufficient",
      basis: "manual",
      activeDecisionId: "active-decision-123",
      canConfirm: false,
      canRevoke: true,
    });
    api.get.mockResolvedValue(response(active));
    show();
    const revoke = await screen.findByRole("button", { name: label("revoke") });
    expect(screen.queryByRole("textbox")).toBeNull();
    await userEvent.setup().click(revoke);
    await waitFor(() => expect(api.revoke).toHaveBeenCalledOnce());
    expect(api.revoke).toHaveBeenCalledWith(41, {
      revision: 8,
      workflowRunId: 80,
      expectedContentFingerprint: "fresh-content-fingerprint",
      expectedDecisionVersion: 4,
      decisionId: "active-decision-123",
    });
    expect(api.confirm).not.toHaveBeenCalled();
    expectNoPurchasesOrReparse();
  });

  it.each(["confirm", "revoke"] as const)(
    "preserves historical notes and labels an empty %s history entry as a localized operation record",
    async (action) => {
      const historicalNote = "Historical note written before the reason form was removed.";
      const record = {
        id: "legacy-confirmation",
        version: 1,
        action: "confirm" as const,
        contentFingerprint: "fresh-content-fingerprint",
        sourceRevision: 8,
        workflowRunId: 80,
        blockIds: ["fresh:block-a", "fresh:block-b"],
        reason: historicalNote,
        createdAt: "2026-10-23T10:00:00Z",
      };
      api.get.mockResolvedValue(
        response(
          view({
            history: [
              record,
              { ...record, id: "operation-without-reason", version: 2, action, reason: "" },
            ],
          }),
        ),
      );
      show();
      const history = await screen.findByRole("button", { name: label("history") });
      await userEvent.setup().click(history);
      expect(screen.getByText(historicalNote)).toBeVisible();
      const operation = screen.getByText(label(`history.default.${action}`));
      expect(operation).toBeVisible();
      expect(operation).toHaveTextContent(/^操作记录：/);
      expect(screen.queryByRole("textbox")).toBeNull();
      expect(screen.queryByText("确认理由")).toBeNull();
      expect(api.confirm).not.toHaveBeenCalled();
      expect(api.revoke).not.toHaveBeenCalled();
      expectNoPurchasesOrReparse();
    },
  );

  it("does not treat a stale historical decision as permission to confirm", async () => {
    api.get.mockResolvedValue(
      response(view({ hasStaleDecision: true, canConfirm: false, canRevoke: false })),
    );
    show();
    expect(await screen.findByText(label("stale"))).toBeVisible();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(screen.queryByRole("button", { name: label("confirmAndContinue") })).toBeNull();
    expect(api.confirm).not.toHaveBeenCalled();
    expectNoPurchasesOrReparse();
  });

  it("still permits a new explicit decision for a fresh snapshot after an older decision became stale", async () => {
    api.get.mockResolvedValue(response(view({ hasStaleDecision: true })));
    show();
    const user = await acknowledge();
    expect(screen.getByText(label("stale"))).toBeVisible();
    expect(confirmButton()).toBeEnabled();
    await user.click(confirmButton());
    await waitFor(() => expect(api.confirm).toHaveBeenCalledOnce());
    expect(api.confirm.mock.calls[0][1].expectedContentFingerprint).toBe(
      "fresh-content-fingerprint",
    );
  });

  it("refuses confirmation when any unpurchased page block lacks its own stable ID", async () => {
    const current = freshContent();
    current.locks[1].blockId = null;
    api.get.mockResolvedValue(response(view({ contentSnapshot: current })));
    show();
    expect(await screen.findByText(label("blockIdsUnavailable"))).toBeVisible();
    expect(screen.queryByRole("button", { name: label("confirmAndContinue") })).toBeNull();
    expect(screen.queryByRole("button", { name: label("confirm") })).toBeNull();
    expect(screen.queryByRole("checkbox", { name: label("reviewed", { count: 1 }) })).toBeNull();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it.each([
    { revision: 8, workflowRunId: 30 },
    { revision: 3, workflowRunId: 80 },
  ])("rejects a stale row snapshot without a fresh GET snapshot: %j", async (cas) => {
    const old = task();
    api.get.mockResolvedValue(response(view({ contentSnapshot: null, ...cas })));
    show({
      task: task({
        contentSnapshot: { ...old.contentSnapshot!, locks: freshContent().locks },
      }),
    });
    expect(await screen.findByText(label("snapshotChanged"))).toBeVisible();
    expect(confirmButton()).toBeDisabled();
    expect(reviewedCheckbox()).toBeDisabled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("cannot confirm a view that has no current content fingerprint", async () => {
    api.get.mockResolvedValue(response(view({ contentFingerprint: null })));
    show();
    await acknowledge();
    expect(confirmButton()).toBeDisabled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("accepts only one pending confirmation and locks dismissal until it finishes", async () => {
    const pending = deferred();
    api.confirm.mockReturnValue(pending.promise);
    show();
    const user = await acknowledge();
    const confirm = confirmButton();
    act(() => {
      confirm.click();
      confirm.click();
    });
    expect(api.confirm).toHaveBeenCalledOnce();
    expect(confirm).toBeDisabled();
    expect(screen.getByRole("button", { name: label("close") })).toBeDisabled();
    expect(reviewedCheckbox()).toBeDisabled();
    expect(screen.queryByRole("textbox")).toBeNull();
    await user.keyboard("{Escape}");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    await act(async () => pending.resolve(response()));
    await waitFor(() => expect(api.changed).toHaveBeenCalledOnce());
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expectNoPurchasesOrReparse();
  });

  it("preserves the checked review when the API rejects confirmation", async () => {
    api.confirm.mockRejectedValueOnce(
      new Error("The content decision changed. Reload the snapshot."),
    );
    show();
    const user = await acknowledge();
    await user.click(confirmButton());
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The content decision changed. Reload the snapshot.",
    );
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(reviewedCheckbox()).toBeChecked();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.getByRole("button", { name: label("close") })).toBeEnabled();
    expect(api.confirm).toHaveBeenCalledOnce();
    expect(api.changed).not.toHaveBeenCalled();
  });

  it("requires a new acknowledgement after refreshing to a changed content fingerprint", async () => {
    const replacement = deferred();
    const next = view({ contentFingerprint: "changed-content-fingerprint", decisionVersion: 5 });
    api.get
      .mockResolvedValueOnce(response())
      .mockReturnValueOnce(replacement.promise)
      .mockResolvedValue(response(next));
    api.confirm.mockRejectedValueOnce(new Error("The saved content changed."));
    show();
    const user = await acknowledge();
    await user.click(confirmButton());
    await screen.findByRole("alert");
    const retry = screen.getByRole("button", { name: label("reloadState") });
    const oldConfirm = confirmButton();
    act(() => {
      retry.click();
      oldConfirm.click();
    });
    expect(api.get).toHaveBeenCalledTimes(2);
    expect(api.confirm).toHaveBeenCalledOnce();
    expect(confirmButton()).toBeDisabled();
    await act(async () => replacement.resolve(response(next)));
    expect(reviewedCheckbox()).not.toBeChecked();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(confirmButton()).toBeDisabled();
    await acknowledge();
    await user.click(confirmButton());
    await waitFor(() => expect(api.confirm).toHaveBeenCalledTimes(2));
    expect(api.confirm.mock.calls[1][1]).toEqual(
      expect.objectContaining({
        expectedContentFingerprint: "changed-content-fingerprint",
        expectedDecisionVersion: 5,
      }),
    );
  });

  it("keeps an old view read-only after GET refresh fails until a current view is accepted again", async () => {
    api.get
      .mockResolvedValueOnce(response())
      .mockRejectedValueOnce(new Error("Reload is temporarily unavailable."))
      .mockResolvedValue(response());
    api.confirm.mockRejectedValueOnce(new Error("Please reload before retrying this decision."));
    show();
    const user = await acknowledge();
    await user.click(confirmButton());
    await screen.findByRole("alert");
    expect(confirmButton()).toBeEnabled();
    await user.click(screen.getByRole("button", { name: label("reloadState") }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Reload is temporarily unavailable.",
    );
    expect(reviewedCheckbox()).toBeChecked();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.getByText("fresh unpaid block A")).toBeVisible();
    expect(confirmButton()).toBeDisabled();
    await user.click(confirmButton());
    expect(api.confirm).toHaveBeenCalledOnce();
    await user.click(screen.getByRole("button", { name: label("reloadState") }));
    await waitFor(() => expect(confirmButton()).toBeEnabled());
    expect(api.get).toHaveBeenCalledTimes(3);
    expect(reviewedCheckbox()).toBeChecked();
    expect(api.confirm).toHaveBeenCalledOnce();
    expectNoPurchasesOrReparse();
  });

  it("shows a failed GET without enabling confirmation and can retry it", async () => {
    api.get.mockRejectedValueOnce(new Error("Current content is unavailable."));
    show();
    expect(await screen.findByRole("alert")).toHaveTextContent("Current content is unavailable.");
    expect(screen.queryByRole("checkbox")).toBeNull();
    await userEvent.setup().click(screen.getByRole("button", { name: label("reloadState") }));
    await waitFor(() => expect(reviewedCheckbox()).toBeEnabled());
    expect(api.get).toHaveBeenCalledTimes(2);
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("rejects a GET response for another task rather than showing its confirmation", async () => {
    api.get.mockResolvedValue(response(view({ taskId: 42 })));
    show();
    expect(await screen.findByRole("alert")).toHaveTextContent(label("loadFailed"));
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it.each(["success", "failure"] as const)(
    "ignores a late GET %s after the modal is unmounted",
    async (outcome) => {
      const pending = deferred();
      api.get.mockReturnValue(pending.promise);
      const modal = show();
      await screen.findByRole("dialog");
      modal.unmount();
      await act(async () => {
        if (outcome === "success") pending.resolve(response());
        else pending.reject(new Error("Late read failure"));
      });
      expect(screen.queryByRole("dialog")).toBeNull();
      expect(api.get).toHaveBeenCalledOnce();
      expect(api.changed).not.toHaveBeenCalled();
      expect(api.confirm).not.toHaveBeenCalled();
      expect(api.revoke).not.toHaveBeenCalled();
      expectNoPurchasesOrReparse();
    },
  );
});

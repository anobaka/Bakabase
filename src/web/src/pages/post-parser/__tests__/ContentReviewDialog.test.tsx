import type {
  ContentReviewPreview,
  ContentReviewTaskSelection,
} from "../components/ContentPurchaseReviewModal";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import ContentReviewDialog from "../components/ContentReviewDialog";
import ContentReadinessReviewModal from "../components/ContentReadinessReviewModal";

const api = vi.hoisted(() => ({
  preview: vi.fn(),
  confirm: vi.fn(),
  portal: vi.fn(),
  props: undefined as any,
  translate: (key: string) => key,
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: api.translate }) }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    postParser: {
      previewPostParserContentReview: api.preview,
      confirmPostParserContentReview: api.confirm,
    },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("../components/ContentPurchaseReviewModal", () => ({
  default: (props: any) => {
    api.props = props;
    return (
      <div data-review>
        {props.loading ? "Loading" : props.error || props.snapshot?.tasks[0]?.title}
      </div>
    );
  },
}));
vi.mock("../components/ContentReadinessReviewModal", () => ({ default: () => null }));

const preview = (taskId = 14): ContentReviewPreview => ({
  scope: { status: "all", taskIds: [taskId] },
  matchedCount: 1,
  autoBuyThreshold: 10,
  minimumRemainingCoins: 1000,
  tasks: [
    {
      taskId,
      title: `Post ${taskId}`,
      link: `https://example.com/post/${taskId}`,
      source: 1,
      revision: 3,
      workflowRunId: 55,
      decisionVersion: 2,
      contentFingerprint: "a".repeat(64),
      quoteHash: "quote",
      canSubmit: true,
      alreadyUnlocked: true,
      targets: [],
    },
  ],
});
const selection = (next = preview()): ContentReviewTaskSelection[] =>
  next.tasks.map((task) => ({
    taskId: task.taskId,
    revision: task.revision,
    workflowRunId: task.workflowRunId!,
    decisionVersion: task.decisionVersion,
    contentFingerprint: task.contentFingerprint!,
    quoteHash: task.quoteHash,
    targets: [],
  }));
const result = {
  requestId: "server",
  requestedCount: 1,
  queuedCount: 1,
  unchangedCount: 0,
  skippedCount: 0,
  failedCount: 0,
  items: [{ taskId: 14, status: "queued" }],
};
let container: HTMLDivElement;
let root: Root;
const show = async (scope = preview().scope, extra: Record<string, unknown> = {}) => {
  await act(async () => root.render(<ContentReviewDialog scope={scope} {...extra} />));
};

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  api.preview.mockReset().mockResolvedValue({ code: 0, data: preview() });
  api.confirm.mockReset().mockResolvedValue({ code: 0, data: result });
  api.portal.mockReset();
  api.props = undefined;
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe("unified paid-content review controller", () => {
  it("previews without spending and confirms only the immutable approved manifest with a durable request ID", async () => {
    const changed = vi.fn();
    await show(undefined, { onChanged: changed });
    expect(api.preview).toHaveBeenCalledExactlyOnceWith(
      preview().scope,
      expect.objectContaining({ sensitiveResponse: true }),
    );
    expect(api.confirm).not.toHaveBeenCalled();
    const tasks = selection();
    await act(async () => {
      await expect(api.props.onSubmit({ tasks })).resolves.toEqual(result);
    });
    expect(api.confirm).toHaveBeenCalledExactlyOnceWith(
      { scope: preview().scope, tasks, requestId: expect.stringMatching(/^[a-f\d-]{36}$/i) },
      expect.objectContaining({ showErrorToast: false }),
    );
    expect(changed).toHaveBeenCalledExactlyOnceWith(result);
    await expect(api.props.onSubmit({ tasks })).rejects.toThrow(
      "postParser.contentPurchaseReview.refreshRequired",
    );
    expect(api.confirm).toHaveBeenCalledTimes(1);
  });

  it("treats a transport failure as an unknown outcome and never automatically repeats the purchase", async () => {
    api.confirm.mockRejectedValueOnce(new Error("connection lost"));
    const changed = vi.fn();
    await show(undefined, { onChanged: changed });
    await expect(api.props.onSubmit({ tasks: selection() })).rejects.toThrow(
      "postParser.contentPurchaseReview.submissionUnknown",
    );
    expect(changed).toHaveBeenCalledExactlyOnceWith();
    await expect(api.props.onSubmit({ tasks: selection() })).rejects.toThrow(
      "postParser.contentPurchaseReview.refreshRequired",
    );
    expect(api.confirm).toHaveBeenCalledTimes(1);
    const previousKey = api.props.snapshotKey;
    await act(async () => {
      await api.props.onRefresh();
    });
    expect(api.props.snapshotKey).not.toBe(previousKey);
    await act(async () => {
      await api.props.onSubmit({ tasks: selection() });
    });
    expect(api.confirm).toHaveBeenCalledTimes(2);
    expect(api.confirm.mock.calls[0][0].requestId).not.toBe(api.confirm.mock.calls[1][0].requestId);
  });

  it("rejects a preview for a broader scope instead of silently authorizing more tasks", async () => {
    api.preview.mockResolvedValue({ code: 0, data: { ...preview(), scope: { status: "all" } } });
    await show();
    expect(api.props.snapshot).toBeUndefined();
    expect(api.props.error).toBe("postParser.contentPurchaseReview.reviewFailed");
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("does not apply a newer task revision to an old waiting workflow", async () => {
    await show(undefined, { expectedIdentity: { taskId: 14, revision: 2, workflowRunId: 55 } });
    expect(api.props.snapshot).toBeUndefined();
    expect(api.props.error).toBe("postParser.review.currentContentChanged");
  });

  it("discards a delayed preview after its scope changes", async () => {
    let finish!: (value: unknown) => void;
    api.preview
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finish = resolve;
          }),
      )
      .mockResolvedValueOnce({ code: 0, data: preview(15) });
    await show();
    await show(preview(15).scope);
    const accepted = api.props.snapshotKey;
    await act(async () => {
      finish({ code: 0, data: preview() });
    });
    expect(api.props.snapshot.tasks[0].taskId).toBe(15);
    expect(api.props.snapshotKey).toBe(accepted);
  });

  it("clears an accepted approval when refreshing fails", async () => {
    await show();
    api.preview.mockRejectedValueOnce(new Error("offline"));
    await act(async () => {
      await api.props.onRefresh();
    });
    expect(api.props.snapshot).toBeUndefined();
    expect(api.props.error).toBe("offline");
    await expect(api.props.onSubmit({ tasks: selection() })).rejects.toThrow();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("opens source-aware evidence and audit details without a second confirmation entry", async () => {
    await show();
    api.props.onInspectTask(preview().tasks[0]);
    expect(api.portal).toHaveBeenCalledWith(
      ContentReadinessReviewModal,
      expect.objectContaining({
        auditOnly: true,
        task: expect.objectContaining({ id: 14, source: 1, revision: 3, workflowRunId: 55 }),
      }),
    );
  });

  it("closing an in-flight read prevents it from producing an actionable preview", async () => {
    let finish!: (value: unknown) => void;
    api.preview.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const close = vi.fn();
    await show(undefined, { onClose: close });
    await act(async () => api.props.onClose());
    await act(async () => finish({ code: 0, data: preview() }));
    expect(container.querySelector("[data-review]")).toBeNull();
    expect(close).toHaveBeenCalledOnce();
    expect(api.confirm).not.toHaveBeenCalled();
  });
});

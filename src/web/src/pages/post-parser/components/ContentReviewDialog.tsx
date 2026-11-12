import type { DestroyableProps } from "@/components/bakaui/types";
import type {
  ContentReviewPreview,
  ContentReviewResult,
  ContentReviewScope,
  ContentReviewTask,
  ContentReviewTaskSelection,
} from "./ContentPurchaseReviewModal";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { v4 as uuid } from "uuid";

import ContentPurchaseReviewModal from "./ContentPurchaseReviewModal";
import ContentReadinessReviewModal from "./ContentReadinessReviewModal";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";

interface Props extends DestroyableProps {
  scope: ContentReviewScope;
  expectedIdentity?: { taskId: number; revision: number; workflowRunId: number };
  onClose?: () => void;
  onChanged?: (result?: ContentReviewResult) => void;
}

const scopeKey = (scope: ContentReviewScope) =>
  JSON.stringify([
    scope.keyword?.trim() ?? "",
    scope.status ?? "all",
    scope.reasonCode ?? "",
    [...(scope.taskIds ?? [])].sort((left, right) => left - right),
  ]);

/** One accepted server preview is the authority for both task and target selection. */
export default function ContentReviewDialog({
  scope,
  expectedIdentity,
  onClose,
  onDestroyed,
  onChanged,
}: Props) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [preview, setPreview] = useState<ContentReviewPreview>();
  const [snapshotKey, setSnapshotKey] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [open, setOpen] = useState(true);
  const current = useRef({ scope, key: scopeKey(scope), expectedIdentity });
  current.current = { scope, key: scopeKey(scope), expectedIdentity };
  const mounted = useRef(true);
  const sequence = useRef(0);
  const submitted = useRef<string>();
  const mutating = useRef(false);

  const load = useCallback(async () => {
    if (mutating.current) return;
    const request = ++sequence.current;
    const captured = current.current;
    setLoading(true);
    setError(undefined);
    // A failed refresh cannot leave an older approval actionable.
    setPreview(undefined);
    try {
      const response = await BApi.postParser.previewPostParserContentReview(captured.scope, {
        sensitiveResponse: true,
        showErrorToast: false,
      });
      if (!mounted.current || request !== sequence.current || captured.key !== current.current.key)
        return;
      const next = response.data;
      if (
        response.code ||
        !next ||
        !Array.isArray(next.tasks) ||
        scopeKey(next.scope) !== captured.key ||
        next.matchedCount !== next.tasks.length ||
        new Set(next.tasks.map((task) => task.taskId)).size !== next.tasks.length
      )
        throw new Error(response.message || t("postParser.contentPurchaseReview.reviewFailed"));
      if (
        captured.expectedIdentity &&
        (next.tasks.length !== 1 ||
          next.tasks.some(
            (task) =>
              task.taskId !== captured.expectedIdentity!.taskId ||
              task.revision !== captured.expectedIdentity!.revision ||
              task.workflowRunId !== captured.expectedIdentity!.workflowRunId,
          ))
      )
        throw new Error(t("postParser.review.currentContentChanged"));
      setPreview(next);
      setSnapshotKey(`${captured.key}:${request}`);
    } catch (failure) {
      if (mounted.current && request === sequence.current)
        setError(
          failure instanceof Error
            ? failure.message
            : t("postParser.contentPurchaseReview.reviewFailed"),
        );
    } finally {
      if (mounted.current && request === sequence.current) setLoading(false);
    }
  }, [t]);

  const query = JSON.stringify([scopeKey(scope), expectedIdentity]);
  useEffect(() => {
    mounted.current = true;
    void load();
    return () => {
      mounted.current = false;
      sequence.current++;
    };
  }, [query, load]);

  const submit = async ({
    tasks,
  }: {
    tasks: ContentReviewTaskSelection[];
  }): Promise<ContentReviewResult> => {
    if (
      !mounted.current ||
      loading ||
      !preview ||
      mutating.current ||
      submitted.current === snapshotKey ||
      scopeKey(preview.scope) !== current.current.key ||
      (current.current.expectedIdentity &&
        preview.tasks.some(
          (task) =>
            task.taskId !== current.current.expectedIdentity!.taskId ||
            task.revision !== current.current.expectedIdentity!.revision ||
            task.workflowRunId !== current.current.expectedIdentity!.workflowRunId,
        ))
    )
      throw new Error(t("postParser.contentPurchaseReview.refreshRequired"));
    submitted.current = snapshotKey;
    mutating.current = true;
    try {
      const response = await BApi.postParser.confirmPostParserContentReview(
        {
          scope: preview.scope,
          requestId: uuid(),
          tasks,
        },
        { sensitiveResponse: true, showErrorToast: false },
      );
      if (response.code || !response.data)
        throw new Error(response.message || t("postParser.contentPurchaseReview.reviewFailed"));
      if (mounted.current) onChanged?.(response.data);
      return response.data;
    } catch (failure) {
      if (mounted.current) onChanged?.();
      throw new Error(
        `${t("postParser.contentPurchaseReview.submissionUnknown")}${failure instanceof Error ? ` ${failure.message}` : ""}`,
      );
    } finally {
      // An error is not evidence that nothing happened. The same snapshot may
      // never be submitted twice; a fresh preview requires another explicit review.
      mutating.current = false;
    }
  };

  const inspect = (task: ContentReviewTask) =>
    createPortal(ContentReadinessReviewModal, {
      auditOnly: true,
      task: {
        id: task.taskId,
        source: task.source!,
        title: task.title ?? undefined,
        link: task.link ?? "",
        revision: task.revision,
        workflowRunId: task.workflowRunId ?? undefined,
        targets: [],
      },
      onChanged: () => {
        onChanged?.();
        if (mounted.current) void load();
      },
    });

  if (!open) return null;
  return (
    <ContentPurchaseReviewModal
      snapshot={preview}
      snapshotKey={snapshotKey}
      loading={loading}
      error={error}
      onRefresh={load}
      onSubmit={submit}
      onInspectTask={inspect}
      onClose={() => {
        if (mutating.current) return;
        mounted.current = false;
        sequence.current++;
        setOpen(false);
        onClose?.();
        onDestroyed?.();
      }}
    />
  );
}

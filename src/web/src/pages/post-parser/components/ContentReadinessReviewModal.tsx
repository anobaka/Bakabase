import type { PostParserTask } from "@/core/models/PostParserTask";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { ContentReadinessView } from "../contentReadiness";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import dayjs from "dayjs";
import {
  Accordion,
  AccordionItem,
  Button,
  Checkbox,
  Chip,
  Modal,
  Spinner,
} from "@/components/bakaui";
import { PostParserSource } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import PostDetails from "./PostDetails";
import LockedContentDetails from "./LockedContentDetails";
import DownloadInfoResultRenderer from "./DownloadInfoResultRenderer";
import { getDownloadInfo } from "../results";
import { remainingContentLocks } from "../contentReadiness";

interface Props extends DestroyableProps {
  task: PostParserTask;
  auditOnly?: boolean;
  onChanged?: (change: { action: "confirm" | "revoke" | "refresh"; continued: boolean }) => void;
}

/** Persists a scoped content decision. It never changes a paid block's IsBought state. */
export default function ContentReadinessReviewModal({
  task,
  auditOnly = false,
  onChanged,
  onDestroyed,
}: Props) {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [view, setView] = useState<ContentReadinessView>();
  const [loading, setLoading] = useState(true);
  const [viewFresh, setViewFresh] = useState(false);
  const [error, setError] = useState<string>();
  const [reviewed, setReviewed] = useState(false);
  const [continueParsing, setContinueParsing] = useState(false);
  const [working, setWorking] = useState<"confirm" | "revoke" | "refresh">();
  const mounted = useRef(true);
  const sequence = useRef(0);
  const mutation = useRef(false);
  const loadingView = useRef(true);
  const reviewedSnapshot = useRef<string>();
  const cancel = useRef<HTMLButtonElement>(null);

  const acceptView = useCallback((next: ContentReadinessView, forceReview = false) => {
    const snapshotKey = JSON.stringify([
      next.revision,
      next.workflowRunId,
      next.contentFingerprint,
      next.decisionVersion,
    ]);
    if (forceReview || reviewedSnapshot.current !== snapshotKey) {
      setReviewed(false);
      setContinueParsing(next.canContinue === true);
      reviewedSnapshot.current = snapshotKey;
    }
    if (next.canContinue !== true) setContinueParsing(false);
    setView(next);
    setViewFresh(true);
  }, []);

  const load = useCallback(async () => {
    const request = ++sequence.current;
    loadingView.current = true;
    if (mounted.current) {
      setLoading(true);
      setViewFresh(false);
      setError(undefined);
    }
    try {
      const response = await BApi.postParser.getPostParserContentReadiness(task.id, {
        sensitiveResponse: true,
        showErrorToast: false,
      });
      if (!mounted.current || request !== sequence.current) return;
      if (response.code || !response.data || response.data.taskId !== task.id)
        throw new Error(response.message || t("postParser.contentReadiness.loadFailed"));
      acceptView(response.data as ContentReadinessView);
    } catch (failure) {
      if (mounted.current && request === sequence.current)
        setError(
          failure instanceof Error ? failure.message : t("postParser.contentReadiness.loadFailed"),
        );
    } finally {
      if (mounted.current && request === sequence.current) {
        loadingView.current = false;
        setLoading(false);
      }
    }
  }, [acceptView, task.id, t]);
  useEffect(() => {
    mounted.current = true;
    void load();
    const frame = window.requestAnimationFrame(() => cancel.current?.focus());
    return () => {
      mounted.current = false;
      sequence.current++;
      window.cancelAnimationFrame(frame);
    };
  }, [load]);

  const content = view?.contentSnapshot ?? task.contentSnapshot;
  const source = view?.source === "SoulPlus" ? PostParserSource.SoulPlus : task.source;
  const currentSnapshot =
    !!view?.contentSnapshot ||
    (!!view &&
      view.revision === task.revision &&
      (view.workflowRunId ?? null) === (task.workflowRunId ?? null));
  const locks = remainingContentLocks(content);
  const blockIds = [...new Set(locks.flatMap((lock) => (lock.blockId ? [lock.blockId] : [])))];
  const identified = locks.length > 0 && locks.every((lock) => !!lock.blockId);
  const missingBlockIds = locks.length > 0 && !identified;
  const showReview = !auditOnly && !!view?.canConfirm && identified;
  const ready =
    !!view && viewFresh && !loading && !working && currentSnapshot && !!view.contentFingerprint;
  const canConfirm = ready && view.canConfirm && identified && reviewed;
  const currentTask = { ...task, contentSnapshot: content, source };
  const sameRun =
    !!view &&
    view.revision === task.revision &&
    (view.workflowRunId ?? null) === (task.workflowRunId ?? null);
  const result = sameRun && !view?.parsedResultMayBeStale ? getDownloadInfo(task) : undefined;
  const close = () => {
    if (!mutation.current) setVisible(false);
  };
  const refreshContent = async () => {
    if (
      mutation.current ||
      loadingView.current ||
      !mounted.current ||
      !view?.canRefresh ||
      !view.contentFingerprint ||
      !currentSnapshot
    )
      return;
    mutation.current = true;
    const request = ++sequence.current;
    setWorking("refresh");
    setViewFresh(false);
    setReviewed(false);
    setError(undefined);
    try {
      const response = await BApi.postParser.refreshPostParserContentReadiness(
        task.id,
        {
          revision: view.revision,
          workflowRunId: view.workflowRunId ?? undefined,
          expectedContentFingerprint: view.contentFingerprint,
          expectedDecisionVersion: view.decisionVersion,
        },
        { sensitiveResponse: true, showErrorToast: false },
      );
      if (!mounted.current || request !== sequence.current) return;
      if (response.code || !response.data || response.data.taskId !== task.id)
        throw new Error(response.message || t("postParser.contentReadiness.refreshFailed"));
      acceptView(response.data as ContentReadinessView, true);
      onChanged?.({ action: "refresh", continued: false });
    } catch (failure) {
      if (mounted.current && request === sequence.current)
        setError(
          failure instanceof Error
            ? failure.message
            : t("postParser.contentReadiness.refreshFailed"),
        );
    } finally {
      mutation.current = false;
      if (mounted.current) setWorking(undefined);
    }
  };
  const act = async (kind: "confirm" | "revoke") => {
    if (mutation.current || loadingView.current || !mounted.current || !view || !ready) return;
    if (
      (kind === "confirm" && !canConfirm) ||
      (kind === "revoke" && (!view.canRevoke || !view.activeDecisionId))
    )
      return;
    mutation.current = true;
    setWorking(kind);
    setError(undefined);
    const cas = {
      revision: view.revision,
      workflowRunId: view.workflowRunId ?? undefined,
      expectedContentFingerprint: view.contentFingerprint!,
      expectedDecisionVersion: view.decisionVersion,
    };
    const shouldContinue = continueParsing && view.canContinue === true;
    try {
      const response =
        kind === "confirm"
          ? await BApi.postParser.confirmPostParserContentReadiness(task.id, {
              ...cas,
              notRequiredBlockIds: blockIds,
              continueParsing: shouldContinue,
            })
          : await BApi.postParser.revokePostParserContentReadiness(task.id, {
              ...cas,
              decisionId: view.activeDecisionId!,
            });
      if (response.code) throw new Error(response.message || t("postParser.result.failed"));
      if (!mounted.current) return;
      setReviewed(false);
      onChanged?.({ action: kind, continued: kind === "confirm" && shouldContinue });
      await load();
    } catch (failure) {
      if (mounted.current)
        setError(failure instanceof Error ? failure.message : t("postParser.result.failed"));
    } finally {
      mutation.current = false;
      if (mounted.current) setWorking(undefined);
    }
  };

  return (
    <Modal
      visible={visible}
      title={t(
        auditOnly
          ? "postParser.contentPurchaseReview.auditTitle"
          : "postParser.contentReadiness.title",
        { id: task.id },
      )}
      size="3xl"
      classNames={{
        base: "max-h-[calc(100dvh-2rem)]",
        body: "min-h-0 overflow-y-auto",
        footer: "flex-wrap gap-2 border-t border-default-200",
      }}
      hideCloseButton={!!working}
      isDismissable={!working}
      isKeyboardDismissDisabled={!!working}
      onClose={close}
      onDestroyed={onDestroyed}
      footer={
        <>
          <Button ref={cancel} variant="flat" isDisabled={!!working} onPress={close}>
            {t("postParser.contentReadiness.close")}
          </Button>
          {view?.canRevoke && (
            <Button
              color="warning"
              variant="flat"
              className="h-auto min-w-0 whitespace-normal py-2"
              isDisabled={!ready}
              isLoading={working === "revoke"}
              onPress={() => void act("revoke")}
            >
              {t("postParser.contentReadiness.revoke")}
            </Button>
          )}
          {showReview && (
            <Button
              color="primary"
              className="h-auto min-w-0 whitespace-normal py-2"
              isDisabled={!canConfirm}
              isLoading={working === "confirm"}
              onPress={() => void act("confirm")}
            >
              {t(
                continueParsing && view?.canContinue === true
                  ? "postParser.contentReadiness.confirmAndContinue"
                  : "postParser.contentReadiness.confirm",
              )}
            </Button>
          )}
        </>
      }
    >
      <div className="space-y-4 text-sm leading-relaxed">
        <p>
          {t(
            auditOnly
              ? "postParser.contentPurchaseReview.auditDescription"
              : "postParser.contentReadiness.description",
          )}
        </p>
        {loading && (
          <div role="status" className="flex items-center gap-2">
            <Spinner size="sm" />
            {t("postParser.contentReadiness.loading")}
          </div>
        )}
        {error && (
          <div role="alert" className="flex flex-wrap items-center gap-2 text-danger">
            <p className="break-words">{error}</p>
            <Button
              size="sm"
              variant="flat"
              isDisabled={!!working}
              onPress={() => {
                if (!mutation.current) void load();
              }}
            >
              {t("postParser.contentReadiness.reloadState")}
            </Button>
          </div>
        )}
        {view && (
          <>
            <div className="flex flex-wrap items-center gap-2">
              <Chip
                color={view.status === "sufficient" ? "success" : "warning"}
                size="sm"
                variant="flat"
              >
                {t(`postParser.contentReadiness.status.${view.status}`)}
              </Chip>
              {view.basis && (
                <span className="text-xs text-default-500">
                  {t(`postParser.contentReadiness.basis.${view.basis}`)}
                </span>
              )}
            </div>
            {view.hasStaleDecision && (
              <p className="text-warning-700">{t("postParser.contentReadiness.stale")}</p>
            )}
            {view.status === "sufficient" && locks.length === 0 && (
              <p role="status" className="rounded-lg bg-success/10 p-3">
                {t("postParser.contentReadiness.noUnlockRequired")}
              </p>
            )}
            {!currentSnapshot && (
              <p role="alert" className="text-warning-700">
                {t("postParser.contentReadiness.snapshotChanged")}
              </p>
            )}
            {missingBlockIds && (
              <section
                aria-label={t("postParser.contentReadiness.refreshRequiredTitle")}
                className="space-y-3 rounded-lg border border-warning/30 bg-warning/10 p-3"
              >
                <h3 className="font-medium">
                  {t("postParser.contentReadiness.refreshRequiredTitle")}
                </h3>
                <p>{t("postParser.contentReadiness.blockIdsUnavailable")}</p>
                <p className="text-xs text-default-500">
                  {t("postParser.contentReadiness.refreshHint")}
                </p>
                <Button
                  color="primary"
                  className="h-auto max-w-full whitespace-normal py-2"
                  isLoading={working === "refresh"}
                  isDisabled={
                    !view.canRefresh ||
                    !view.contentFingerprint ||
                    !currentSnapshot ||
                    loading ||
                    !!working
                  }
                  onPress={() => void refreshContent()}
                >
                  {t("postParser.contentReadiness.refreshContent")}
                </Button>
                {!view.canRefresh && (
                  <p className="text-xs text-default-500">
                    {t("postParser.contentReadiness.refreshUnavailable")}
                  </p>
                )}
              </section>
            )}
            <Accordion variant="light">
              <AccordionItem
                key="existing"
                title={t("postParser.contentReadiness.existingContent")}
              >
                <div className="space-y-4">
                  <PostDetails task={currentTask} />
                  {result ? (
                    <DownloadInfoResultRenderer data={result} />
                  ) : (
                    (!sameRun || view.parsedResultMayBeStale) && (
                      <p className="text-xs text-default-500">
                        {t("postParser.contentReadiness.oldResults")}
                      </p>
                    )
                  )}
                </div>
              </AccordionItem>
            </Accordion>
            {showReview && (
              <section
                aria-label={t("postParser.contentReadiness.reviewTitle")}
                className="space-y-3 rounded-lg border border-primary/20 bg-primary/5 p-3"
              >
                <h3 className="font-medium">{t("postParser.contentReadiness.reviewTitle")}</h3>
                <p className="text-xs text-default-500">
                  {t("postParser.contentReadiness.reviewInstructions")}
                </p>
                <p>{t("postParser.contentReadiness.scope", { count: blockIds.length })}</p>
                <Checkbox
                  isSelected={reviewed}
                  isDisabled={loading || !!working || !currentSnapshot || !identified}
                  onValueChange={setReviewed}
                >
                  {t("postParser.contentReadiness.reviewed", { count: blockIds.length })}
                </Checkbox>
                {view.canContinue === true && (
                  <Checkbox
                    isSelected={continueParsing}
                    onValueChange={setContinueParsing}
                    isDisabled={loading || !!working}
                  >
                    {t("postParser.contentReadiness.continue")}
                  </Checkbox>
                )}
              </section>
            )}
            {content && <LockedContentDetails content={content} source={source} compact />}
            {view.canRevoke && (
              <p className="text-xs text-default-500">
                {t("postParser.contentReadiness.revokeHint")}
              </p>
            )}
            {!!view.history.length && (
              <Accordion variant="light">
                <AccordionItem key="history" title={t("postParser.contentReadiness.history")}>
                  <ol className="space-y-3">
                    {view.history.map((item) => (
                      <li key={item.id} className="space-y-1 border-b border-default-200 pb-2">
                        <p className="text-xs text-default-500">
                          {dayjs(item.createdAt).format("YYYY-MM-DD HH:mm:ss")} ·{" "}
                          {t(`postParser.contentReadiness.history.${item.action}`)}
                        </p>
                        <p className="whitespace-pre-wrap break-words">
                          {item.reason?.trim()
                            ? item.reason
                            : t(`postParser.contentReadiness.history.default.${item.action}`)}
                        </p>
                        <p className="text-xs text-default-500">
                          {t("postParser.contentReadiness.historyScope", {
                            version: item.version,
                            count: item.blockIds.length,
                          })}
                        </p>
                      </li>
                    ))}
                  </ol>
                </AccordionItem>
              </Accordion>
            )}
          </>
        )}
      </div>
    </Modal>
  );
}

("use client");

import type { ReactNode } from "react";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineCloudDownload,
  AiOutlineClockCircle,
  AiOutlineCopy,
  AiOutlineDelete,
  AiOutlineDownload,
  AiOutlineFileText,
  AiOutlineHistory,
  AiOutlineFolderOpen,
  AiOutlinePlayCircle,
  AiOutlinePlus,
  AiOutlineQuestionCircle,
  AiOutlineReload,
  AiOutlineSetting,
  AiOutlineUnlock,
} from "react-icons/ai";
import * as XLSX from "xlsx";
import dayjs from "dayjs";

import AddTasksModal from "./components/AddTasksModal";
import AddToAcquisitionModal from "./components/AddToAcquisitionModal";
import ConfigurationModal from "./components/ConfigurationModal";
import DownloadInfoResultRenderer from "./components/DownloadInfoResultRenderer";
import TaskList from "./components/TaskList";
import Tooltip from "./components/PostParserTooltip";
import TaskSearch from "./components/TaskSearch";
import PostDetails, { AvailabilityDetails, EvidencePopover } from "./components/PostDetails";
import LocalProcessingModal from "./components/LocalProcessingModal";
import {
  buildExportRows,
  copyParserText,
  downloadInstructions,
  getDownloadInfo,
  getTargetResult,
} from "./results";
import { buildTaskSearchText } from "./search";

import { Alert, Button, Chip, Modal, Spinner, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import TampermonkeyInstallButton from "@/components/ThirdPartyConfig/base/TampermonkeyInstallButton";
import WorkflowRunsDrawer from "@/components/Workflow/WorkflowRunsDrawer";
import WorkflowIntegrationHint from "@/components/Workflow/WorkflowIntegrationHint";
import BApi from "@/sdk/BApi";
import {
  BTaskStatus,
  PostParserSource,
  PostParseTarget,
  PostParseTargetLabel,
  ThirdPartyId,
  WorkflowRunStatus,
} from "@/sdk/constants";
import { useSoulPlusOptionsStore, useThirdPartyOptionsStore } from "@/stores/options";
import { usePostParserTasksStore } from "@/stores/postParserTasks";
import { useBTasksStore } from "@/stores/bTasks";

const failedStatuses = [
  WorkflowRunStatus.Failed,
  WorkflowRunStatus.Cancelled,
  WorkflowRunStatus.Interrupted,
];
const retryStatuses = [...failedStatuses, WorkflowRunStatus.Waiting];
const hasResults = (task: PostParserTask) => Object.keys(task.results ?? {}).length > 0;
const processingTaskStatuses = new Set([
  BTaskStatus.NotStarted,
  BTaskStatus.Running,
  BTaskStatus.Pausing,
  BTaskStatus.Paused,
  BTaskStatus.Resuming,
  BTaskStatus.Cancelling,
]);

const ActionGroup = ({ stage, children }: { stage: string; children: ReactNode }) => {
  const { t } = useTranslation();

  return (
    <div className="flex items-start gap-1" data-operation-stage={stage}>
      <span className="w-7 shrink-0 pt-1.5 text-[10px] leading-4 text-default-400">
        {t(`postParser.operations.${stage}`)}
      </span>
      <div className="flex min-w-0 flex-1 flex-wrap items-center gap-0.5">{children}</div>
    </div>
  );
};

const PostParserPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const automaticallyParsing = useThirdPartyOptionsStore(
    (state) => state.data.automaticallyParsingPosts,
  );
  const autoBuyThreshold = useSoulPlusOptionsStore((state) => state.data.autoBuyThreshold);
  const minimumRemainingCoins = useSoulPlusOptionsStore(
    (state) => state.data.minimumRemainingCoins,
  );
  const storedTasks = usePostParserTasksStore((state) => state.tasks);
  const tasks = useMemo(() => storedTasks.filter((task) => !task.isDeleted), [storedTasks]);
  const setTasks = usePostParserTasksStore((state) => state.setTasks);
  const [keyword, setKeyword] = useState("");
  const search = keyword.trim().toLocaleLowerCase();
  const searchIndex = useMemo(
    () => tasks.map((task) => ({ task, text: buildTaskSearchText(task) })),
    [tasks],
  );
  const shownTasks = useMemo(
    () =>
      search
        ? searchIndex.filter(({ text }) => text.includes(search)).map(({ task }) => task)
        : tasks,
    [search, searchIndex, tasks],
  );
  const busyRef = useRef(false);
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();
  const [runs, setRuns] = useState<PostParserTask | "all">();
  const runsTaskId = runs === "all" ? undefined : runs?.id;
  const loadRuns = useCallback(
    ({ pageIndex, pageSize }: { pageIndex: number; pageSize: number }) =>
      BApi.postParser.searchPostParserWorkflowRuns({ taskId: runsTaskId, pageIndex, pageSize }),
    [runsTaskId],
  );
  const bTasks = useBTasksStore((state) => state.tasks);
  const activeRunTasks = useMemo(
    () =>
      new Map(
        bTasks
          .filter((task) => processingTaskStatuses.has(task.status))
          .map((task) => [task.id, task]),
      ),
    [bTasks],
  );
  const dispatching = bTasks.some(
    (task) =>
      task.id === "ParseAllPosts" &&
      ![BTaskStatus.Completed, BTaskStatus.Cancelled, BTaskStatus.Error].includes(task.status),
  );
  const isProcessing = (task: PostParserTask) =>
    (!!task.workflowRunId && activeRunTasks.has(`workflow.run.${task.workflowRunId}`)) ||
    ["fetch", "retry", "reparse", "purchase"].some(
      (actionName) => busy === `${actionName}-${task.id}`,
    );
  const running = tasks.some(isProcessing);
  const hasStartableTasks = tasks.some((task) => {
    if (isProcessing(task)) return false;
    if (task.workflowRunId)
      return task.workflowStatus == null
        ? !!task.error
        : failedStatuses.includes(task.workflowStatus);

    return (
      !!task.error ||
      (task.parsingState != null && task.parsingState !== "complete") ||
      !task.results ||
      task.targets.some(
        (target) =>
          !Object.prototype.hasOwnProperty.call(task.results, target) &&
          !Object.prototype.hasOwnProperty.call(task.results, PostParseTargetLabel[target]),
      )
    );
  });

  const refresh = useCallback(async () => {
    const response = await BApi.postParser.getAllPostParserTasks();

    if (response.code) throw new Error(response.message || t<string>("postParser.result.failed"));
    setTasks((response.data ?? []) as PostParserTask[]);
  }, [setTasks, t]);

  useEffect(() => {
    let active = true;
    let requesting = false;
    const load = async () => {
      if (requesting) return;
      requesting = true;
      try {
        const response = await BApi.postParser.getAllPostParserTasks();

        if (active && !response.code) setTasks((response.data ?? []) as PostParserTask[]);
      } catch {
        // The shared API reports failures; SignalR remains the primary update path.
      } finally {
        requesting = false;
      }
    };

    void load();
    const timer = running ? window.setInterval(() => void load(), 2000) : undefined;

    return () => {
      active = false;
      if (timer != null) window.clearInterval(timer);
    };
  }, [running, setTasks, autoBuyThreshold, minimumRemainingCoins]);

  const action = async (
    key: string,
    invoke: () => Promise<{ code?: number; message?: string }>,
    rethrow = false,
  ) => {
    if (busyRef.current) return;
    busyRef.current = true;
    setBusy(key);
    setError(undefined);
    try {
      const response = await invoke();

      if (response.code) throw new Error(response.message || t<string>("postParser.result.failed"));
      await refresh();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : t<string>("postParser.result.failed"));
      if (rethrow) throw failure;
    } finally {
      busyRef.current = false;
      setBusy(undefined);
    }
  };

  const copy = async (value: string) => {
    try {
      await copyParserText(value);
      toast.success(t<string>("postParser.result.copied"));
    } catch {
      toast.danger(t<string>("postParser.result.copyFailed"));
    }
  };

  const showContent = (task: PostParserTask) =>
    createPortal(Modal, {
      defaultVisible: true,
      title: task.title || t<string>("postParser.action.viewContent"),
      size: "lg",
      children: <PostDetails task={task} />,
      footer: { actions: ["cancel"] },
    });

  const renderResults = (task: PostParserTask) => {
    const availability = task.availability ?? getDownloadInfo(task)?.availability;
    const reason = [...new Set([availability?.reason, task.parsingMessage].filter(Boolean))].join(
      "\n",
    );

    return (
      <div className="flex min-w-0 flex-col gap-2">
        {task.error && (
          <Button
            className="h-auto min-w-0 justify-start whitespace-normal break-words px-0 text-left text-xs"
            color="danger"
            size="sm"
            variant="light"
            onPress={() =>
              createPortal(Modal, {
                defaultVisible: true,
                title: t("postParser.action.failureDetails"),
                size: "lg",
                children: (
                  <pre className="whitespace-pre-wrap break-words text-sm">{task.error}</pre>
                ),
                footer: {
                  actions: ["cancel", "ok"],
                  okProps: { children: t("postParser.action.retry") },
                },
                onOk: () =>
                  action(
                    `retry-${task.id}`,
                    () =>
                      task.workflowRunId &&
                      task.workflowStatus != null &&
                      retryStatuses.includes(task.workflowStatus)
                        ? BApi.postParser.retryPostParserTaskWorkflow(task.id)
                        : BApi.postParser.reParsePostParserTask(task.id),
                    true,
                  ),
              })
            }
          >
            <span className="line-clamp-2" role="alert" title={task.error}>
              {task.error}
            </span>
          </Button>
        )}
        <div className="flex flex-wrap items-center gap-1">
          <AvailabilityDetails value={availability ? { ...availability, reason } : undefined} />
          {!availability && task.parsingMessage && (
            <EvidencePopover label={t("postParser.action.details")} reason={task.parsingMessage} />
          )}
        </div>
        {!hasResults(task) ? (
          <span className="text-sm text-default-400">—</span>
        ) : (
          task.targets.map((target) => {
            const result = getTargetResult(task, target);

            return (
              <div key={target}>
                {result?.error && (
                  <p className="text-xs text-danger" role="alert">
                    {result.error}
                  </p>
                )}
                {result?.data ? (
                  target === PostParseTarget.DownloadInfo ? (
                    <DownloadInfoResultRenderer
                      data={getDownloadInfo(task) ?? {}}
                      showAvailability={false}
                      showTitle={getDownloadInfo(task)?.title !== task.title}
                    />
                  ) : (
                    <Chip size="sm" variant="flat">
                      {t<string>(`PostParseTarget.${PostParseTargetLabel[target]}`)}
                    </Chip>
                  )
                ) : (
                  !result?.error && (
                    <span className="text-xs text-default-400">
                      {t<string>("postParser.label.noResult")}
                    </span>
                  )
                )}
              </div>
            );
          })
        )}
      </div>
    );
  };

  const renderTask = (task: PostParserTask) => {
    const data = getDownloadInfo(task);
    const processing = isProcessing(task);
    const liveTask = task.workflowRunId
      ? activeRunTasks.get(`workflow.run.${task.workflowRunId}`)
      : undefined;
    const queued = liveTask?.status === BTaskStatus.NotStarted;
    const paused = liveTask?.status === BTaskStatus.Paused;
    const stage =
      liveTask?.data && typeof liveTask.data === "object" && "stage" in liveTask.data
        ? liveTask.data.stage
        : undefined;
    const knownStages = [
      "fetching",
      "waitingForAi",
      "checkingAvailability",
      "purchasing",
      "extracting",
      "checkingLinks",
    ];
    const processingLabel = paused
      ? "postParser.stage.paused"
      : queued
        ? "postParser.stage.queued"
        : typeof stage === "string" && knownStages.includes(stage)
          ? `postParser.stage.${stage}`
          : "postParser.label.processing";
    const hasLinks = !!data?.resources?.some((resource) => !!resource.link?.trim());
    const locks = task.contentSnapshot?.locks?.filter((lock) => !lock.isBought) ?? [];
    const needsUnlock =
      locks.length > 0 ||
      task.parsingState === "awaitingPurchase" ||
      task.parsingState === "possiblyExpired";
    const canImport = hasLinks && !needsUnlock && data?.isComplete !== false && !task.error;
    const hasContent = !!(task.text || task.content || task.contentSnapshot);
    const quote = task.purchaseQuote;
    const revision = task.revision;
    const availability = task.availability ?? data?.availability;
    const canOfferUnlock =
      locks.length > 0 &&
      availability?.status === "expired" &&
      !!quote &&
      !!task.workflowRunId &&
      revision != null &&
      Number.isSafeInteger(revision) &&
      revision >= 0;
    const unlockButton = canOfferUnlock ? (
      <Button
        color="warning"
        isDisabled={!!busy || processing || !quote.eligibleLockUrls.length}
        isLoading={busy === `purchase-${task.id}`}
        size="sm"
        startContent={<AiOutlineUnlock aria-hidden />}
        variant="light"
        onPress={() =>
          action(`purchase-${task.id}`, () =>
            BApi.postParser.purchasePostParserTaskContent(task.id, {
              revision,
              lockUrls: quote.eligibleLockUrls,
              maxTotalCost: quote.eligibleTotal,
            }),
          )
        }
      >
        {t(
          quote.eligibleLockUrls.length
            ? "postParser.action.unlockForAmount"
            : "postParser.action.noEligibleUnlock",
          { amount: quote.eligibleTotal },
        )}
      </Button>
    ) : null;
    const state =
      task.workflowStatus === WorkflowRunStatus.Interrupted
        ? "interrupted"
        : task.error
          ? "error"
          : task.parsingState && task.parsingState !== "complete"
            ? task.parsingState
            : locks.length || data?.isComplete === false
              ? "partial"
              : task.workflowStatus === WorkflowRunStatus.Waiting
                ? "awaitingAction"
                : task.parsingState;
    const retryable =
      task.workflowRunId &&
      task.workflowStatus != null &&
      retryStatuses.includes(task.workflowStatus);

    return (
      <>
        <div className="min-w-0" role="cell">
          <span className="text-xs tabular-nums text-default-400">#{task.id}</span>
        </div>
        <div className="min-w-0" role="cell">
          <div className="flex min-w-0 flex-col gap-2">
            <div className="flex min-w-0 flex-wrap items-center gap-1.5">
              {task.source === PostParserSource.SoulPlus && (
                <ThirdPartyIcon thirdPartyId={ThirdPartyId.SoulPlus} />
              )}
              <span className="min-w-0 break-words font-medium line-clamp-3" title={task.title}>
                {task.title || t<string>("postParser.label.untitled")}
              </span>
              <div className="flex shrink-0 items-center gap-1">
                {processing ? (
                  <Chip color="primary" size="sm" variant="flat">
                    <span
                      aria-label={t(processingLabel)}
                      className="inline-flex items-center gap-1"
                      role="status"
                    >
                      {queued || paused ? (
                        <AiOutlineClockCircle aria-hidden />
                      ) : (
                        <Spinner aria-hidden classNames={{ wrapper: "h-3 w-3" }} size="sm" />
                      )}
                      {t(processingLabel)}
                    </span>
                  </Chip>
                ) : state ? (
                  <Chip
                    color={
                      state === "complete" ? "success" : state === "error" ? "danger" : "warning"
                    }
                    size="sm"
                    variant="flat"
                  >
                    {t<string>(`postParser.state.${state}`)}
                  </Chip>
                ) : task.workflowStatus != null ? (
                  <Chip
                    color={
                      task.workflowStatus === WorkflowRunStatus.Success ? "success" : "default"
                    }
                    size="sm"
                    variant="flat"
                  >
                    {t<string>(
                      [
                        WorkflowRunStatus.Pending,
                        WorkflowRunStatus.Running,
                        WorkflowRunStatus.Interrupted,
                      ].includes(task.workflowStatus)
                        ? "postParser.stage.interrupted"
                        : `workflow.runs.status.${WorkflowRunStatus[task.workflowStatus]}`,
                    )}
                  </Chip>
                ) : (
                  <Chip size="sm" variant="flat">
                    {t<string>(
                      task.error
                        ? "postParser.label.error"
                        : hasResults(task)
                          ? "postParser.label.parsed"
                          : "postParser.label.pending",
                    )}
                  </Chip>
                )}
                {task.workflowRunId && (
                  <span className="self-center text-xs tabular-nums text-default-400">
                    {t<string>("postParser.label.run", { id: task.workflowRunId })}
                  </span>
                )}
              </div>
              {task.title && (
                <Button
                  isIconOnly
                  aria-label={t<string>("postParser.action.copyTitle")}
                  className="h-6 min-w-6 w-6 shrink-0"
                  size="sm"
                  variant="light"
                  onPress={() => copy(task.title!)}
                >
                  <AiOutlineCopy aria-hidden />
                </Button>
              )}
            </div>
            {task.link && (
              <div className="flex min-w-0 items-start gap-1">
                <Button
                  className="h-auto min-w-0 justify-start overflow-hidden px-0 py-1"
                  color="primary"
                  size="sm"
                  variant="light"
                  onPress={() => BApi.gui.openUrlInDefaultBrowser({ url: task.link })}
                >
                  <span className="truncate text-left text-xs" title={task.link}>
                    {task.link}
                  </span>
                </Button>
                <Button
                  isIconOnly
                  aria-label={t<string>("postParser.action.copyPostLink")}
                  className="h-6 min-w-6 w-6 shrink-0"
                  size="sm"
                  title={t<string>("postParser.action.copyPostLink")}
                  variant="light"
                  onPress={() => copy(task.link)}
                >
                  <AiOutlineCopy aria-hidden />
                </Button>
              </div>
            )}
            <dl className="flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] text-default-500">
              <div className="flex items-center gap-1 whitespace-nowrap">
                <dt>{t<string>("postParser.label.createdAt")}</dt>
                <dd className="tabular-nums">
                  {task.createdAt ? (
                    <time dateTime={task.createdAt}>
                      {dayjs(task.createdAt).format("YYYY-MM-DD HH:mm:ss")}
                    </time>
                  ) : (
                    "—"
                  )}
                </dd>
              </div>
              <div className="flex items-center gap-1 whitespace-nowrap">
                <dt title={t<string>("postParser.label.completedAtHint")}>
                  {t<string>("postParser.label.completedAt")}
                </dt>
                <dd className="tabular-nums">
                  {task.completedAt ? (
                    <time dateTime={task.completedAt}>
                      {dayjs(task.completedAt).format("YYYY-MM-DD HH:mm:ss")}
                    </time>
                  ) : (
                    "—"
                  )}
                </dd>
              </div>
            </dl>
          </div>
        </div>
        <div className="min-w-0" role="cell">
          {renderResults(task)}
        </div>
        <div className="min-w-0" role="cell">
          <div className="flex flex-col gap-1 [&_button]:h-7 [&_button]:px-1.5 [&_button]:text-xs">
            {(hasContent ||
              canOfferUnlock ||
              (!task.workflowRunId && !hasResults(task) && !task.error)) && (
              <ActionGroup stage="source">
                {hasContent && (
                  <Tooltip content={t("postParser.action.viewContent")}>
                    <Button
                      isIconOnly
                      aria-label={t("postParser.action.viewContent")}
                      className="min-w-7 w-7"
                      size="sm"
                      variant="light"
                      onPress={() => showContent(task)}
                    >
                      <AiOutlineFileText aria-hidden />
                    </Button>
                  </Tooltip>
                )}
                {unlockButton &&
                  (quote!.excludedCount > 0 ? (
                    <Tooltip
                      content={
                        <div className="max-w-xs space-y-1">
                          <p>
                            {t("postParser.purchase.excluded", {
                              count: quote!.excludedCount,
                              amount: quote!.excludedTotal,
                            })}
                          </p>
                          {quote!.unknownPriceCount > 0 && (
                            <p>
                              {t("postParser.purchase.unknownPrices", {
                                count: quote!.unknownPriceCount,
                              })}
                            </p>
                          )}
                        </div>
                      }
                    >
                      {quote!.eligibleLockUrls.length ? (
                        unlockButton
                      ) : (
                        <span
                          aria-disabled="true"
                          className="inline-flex"
                          role="button"
                          tabIndex={0}
                        >
                          {unlockButton}
                        </span>
                      )}
                    </Tooltip>
                  ) : (
                    unlockButton
                  ))}
                {!task.workflowRunId && !hasResults(task) && !task.error && (
                  <Button
                    isDisabled={!!busy || processing}
                    isLoading={busy === `fetch-${task.id}`}
                    size="sm"
                    startContent={<AiOutlinePlayCircle aria-hidden />}
                    variant="light"
                    onPress={() =>
                      action(`fetch-${task.id}`, () =>
                        BApi.postParser.reParsePostParserTask(task.id),
                      )
                    }
                  >
                    {t("postParser.action.fetchPost")}
                  </Button>
                )}
              </ActionGroup>
            )}
            {(hasResults(task) ||
              (!processing && (task.error || task.workflowRunId || task.contentSnapshot))) && (
              <ActionGroup stage="analysis">
                {!processing && (
                  <Button
                    isDisabled={!!busy}
                    isLoading={busy === `retry-${task.id}` || busy === `reparse-${task.id}`}
                    size="sm"
                    startContent={<AiOutlineReload aria-hidden />}
                    variant="light"
                    onPress={() =>
                      retryable && !needsUnlock
                        ? action(`retry-${task.id}`, () =>
                            BApi.postParser.retryPostParserTaskWorkflow(task.id),
                          )
                        : action(`reparse-${task.id}`, () =>
                            BApi.postParser.reParsePostParserTask(task.id),
                          )
                    }
                  >
                    {t(
                      needsUnlock
                        ? "postParser.action.reParse"
                        : retryable
                          ? task.workflowStatus === WorkflowRunStatus.Waiting
                            ? "postParser.action.continueParsing"
                            : "postParser.action.retry"
                          : "postParser.action.reParse",
                    )}
                  </Button>
                )}
                {data && (
                  <Tooltip content={t("postParser.action.exportInstructions")}>
                    <Button
                      isIconOnly
                      aria-label={t("postParser.action.exportInstructions")}
                      className="min-w-7 w-7"
                      size="sm"
                      variant="light"
                      onPress={() => downloadInstructions([task])}
                    >
                      <AiOutlineDownload aria-hidden />
                    </Button>
                  </Tooltip>
                )}
              </ActionGroup>
            )}
            {canImport && (
              <ActionGroup stage="download">
                <Button
                  color="primary"
                  size="sm"
                  startContent={<AiOutlineCloudDownload aria-hidden />}
                  variant="light"
                  onPress={() => createPortal(AddToAcquisitionModal, { task })}
                >
                  {t("postParser.action.addToAcquisition")}
                </Button>
              </ActionGroup>
            )}
            {canImport && (
              <ActionGroup stage="processing">
                <Button
                  size="sm"
                  startContent={<AiOutlineFolderOpen aria-hidden />}
                  variant="light"
                  onPress={() => createPortal(LocalProcessingModal, { task })}
                >
                  {t("postParser.action.processLocal")}
                </Button>
              </ActionGroup>
            )}
            <ActionGroup stage="task">
              <Button
                size="sm"
                startContent={<AiOutlineHistory aria-hidden />}
                variant="light"
                onPress={() => setRuns(task)}
              >
                {t("postParser.action.viewRuns")}
              </Button>
              <Tooltip content={t("postParser.action.delete")}>
                <Button
                  isIconOnly
                  aria-label={t("postParser.action.delete")}
                  className="min-w-7 w-7"
                  color="danger"
                  isDisabled={!!busy}
                  size="sm"
                  variant="light"
                  onPress={() =>
                    action(`delete-${task.id}`, () => BApi.postParser.deletePostParserTask(task.id))
                  }
                >
                  <AiOutlineDelete aria-hidden />
                </Button>
              </Tooltip>
            </ActionGroup>
          </div>
        </div>
      </>
    );
  };

  return (
    <div className="flex h-full min-h-[28rem] min-w-0 flex-col gap-3">
      <div className="flex shrink-0 flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="flex items-center gap-2 text-lg font-semibold">
            <AiOutlineFileText aria-hidden className="text-primary" />
            {t<string>("postParser.page.title")}
          </h1>
          <p className="mt-1 max-w-3xl text-sm leading-relaxed text-default-500">
            {t<string>("postParser.page.description")}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <WorkflowIntegrationHint surface="postParser" />
          <Button
            size="sm"
            startContent={<AiOutlineSetting aria-hidden />}
            variant="flat"
            onPress={() => createPortal(ConfigurationModal, {})}
          >
            {t<string>("postParser.action.configuration")}
          </Button>
          <Button
            color="primary"
            size="sm"
            startContent={<AiOutlinePlus aria-hidden />}
            onPress={() =>
              createPortal(AddTasksModal, { automaticallyParsing: !!automaticallyParsing })
            }
          >
            {t<string>("postParser.action.addTasks")}
          </Button>
        </div>
      </div>
      <div className="flex shrink-0 flex-wrap items-center justify-between gap-3 rounded-xl bg-default-50 p-3">
        <div className="flex min-w-0 flex-[1_1_34rem] flex-wrap items-center gap-3">
          <TaskSearch
            shown={shownTasks.length}
            total={tasks.length}
            value={keyword}
            onChange={setKeyword}
          />
          <Button
            className="shrink-0"
            color="primary"
            isDisabled={!hasStartableTasks || !!busy || dispatching}
            isLoading={busy === "start" || dispatching}
            size="sm"
            startContent={<AiOutlinePlayCircle aria-hidden className="text-base" />}
            variant="flat"
            onPress={() => action("start", () => BApi.postParser.startAllPostParserTasks())}
          >
            {t<string>(dispatching ? "postParser.action.queueing" : "postParser.action.start")}
          </Button>
        </div>
        <div className="flex flex-wrap items-center gap-1">
          <Button
            isDisabled={tasks.length === 0}
            size="sm"
            variant="light"
            onPress={() => downloadInstructions(tasks)}
          >
            {t("postParser.action.exportInstructions")}
          </Button>
          <Button
            isDisabled={tasks.length === 0}
            size="sm"
            startContent={<AiOutlineDownload aria-hidden />}
            variant="light"
            onPress={() => {
              const rows = buildExportRows(tasks, (key) => t<string>(`PostParseTarget.${key}`));
              const workbook = XLSX.utils.book_new();

              XLSX.utils.book_append_sheet(workbook, XLSX.utils.json_to_sheet(rows), "PostParser");
              XLSX.writeFile(
                workbook,
                `post-parser-export-${new Date().toISOString().slice(0, 10)}.xlsx`,
              );
            }}
          >
            {t<string>("postParser.action.export")}
          </Button>
          <Button
            size="sm"
            startContent={<AiOutlineHistory aria-hidden />}
            variant="light"
            onPress={() => setRuns("all")}
          >
            {t<string>("postParser.action.viewAllRuns")}
          </Button>
          <Button
            color="danger"
            isDisabled={tasks.length === 0 || !!busy}
            size="sm"
            startContent={<AiOutlineDelete aria-hidden />}
            variant="light"
            onPress={() => action("deleteAll", () => BApi.postParser.deleteAllPostParserTasks())}
          >
            {t<string>("postParser.action.deleteAll")}
          </Button>
          <Button
            size="sm"
            startContent={<AiOutlineQuestionCircle aria-hidden />}
            variant="light"
            onPress={() =>
              createPortal(Modal, {
                defaultVisible: true,
                size: "lg",
                title: t<string>("postParser.action.instructions"),
                children: (
                  <div className="space-y-4">
                    <p className="text-sm leading-relaxed">
                      {t<string>("postParser.tip.parseOnly")}
                    </p>
                    <ol className="list-inside list-decimal space-y-2 text-sm text-default-600">
                      <li>{t<string>("postParser.help.input")}</li>
                      <li>{t<string>("postParser.help.parse")}</li>
                      <li>{t<string>("postParser.help.result")}</li>
                    </ol>
                    <Alert
                      description={t<string>("postParser.tip.aiRequired")}
                      title={t<string>("postParser.label.ai")}
                    />
                    <p className="text-xs text-default-500">
                      {t<string>("postParser.tip.workflow")}
                    </p>
                    <TampermonkeyInstallButton
                      descriptions={[t<string>("thirdPartyIntegration.tip.soulPlusClick")]}
                    />
                  </div>
                ),
                footer: { actions: ["cancel"] },
              })
            }
          >
            {t<string>("postParser.action.instructions")}
          </Button>
        </div>
      </div>
      {error && (
        <p className="shrink-0 text-sm text-danger" role="alert">
          {error}
        </p>
      )}
      {shownTasks.length > 0 ? (
        <TaskList renderTask={renderTask} search={search} tasks={shownTasks} />
      ) : (
        <div className="flex min-h-72 flex-1 flex-col items-center justify-center rounded-xl border border-dashed border-default-200 px-4 py-12 text-center">
          <p className="text-sm text-default-500" role="status">
            {t<string>(tasks.length ? "postParser.search.empty" : "postParser.page.empty")}
          </p>
          {tasks.length > 0 && (
            <Button className="mt-3" size="sm" variant="flat" onPress={() => setKeyword("")}>
              {t<string>("postParser.search.clear")}
            </Button>
          )}
        </div>
      )}
      {runs && (
        <WorkflowRunsDrawer
          isOpen
          loadRuns={loadRuns}
          runSourceKey={runs === "all" ? "postParser:all" : `postParser:task:${runs.id}`}
          triggerKind="postParser.manual"
          workflowName={
            runs === "all"
              ? t<string>("postParser.history.all")
              : t<string>("postParser.history.task", {
                  id: runs.id,
                  title: runs.title || t<string>("postParser.label.untitled"),
                })
          }
          onClose={() => {
            setRuns(undefined);
            void refresh().catch(() => undefined);
          }}
        />
      )}
    </div>
  );
};

export default PostParserPage;

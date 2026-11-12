("use client");

import type { ReactNode } from "react";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineAim,
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
} from "react-icons/ai";
import * as XLSX from "xlsx";
import dayjs from "dayjs";

import AddTasksModal from "./components/AddTasksModal";
import ConfigurationModal from "./components/ConfigurationModal";
import DeleteTasksConfirmationModal from "./components/DeleteTasksConfirmationModal";
import ContentReviewDialog from "./components/ContentReviewDialog";
import type { ContentReviewScope } from "./components/ContentPurchaseReviewModal";
import FilteredTaskActions from "./components/FilteredTaskActions";
import AttentionReasonFilter from "./components/AttentionReasonFilter";
import LockedContentDetails from "./components/LockedContentDetails";
import { hasBlockingContentLocks } from "./contentReadiness";
import { translateKnownMessage } from "./knownMessages";
import { attentionReasonLabel } from "./attentionReasons";
import DownloadInfoResultRenderer from "./components/DownloadInfoResultRenderer";
import TaskList from "./components/TaskList";
import TaskStatusSummary from "./components/TaskStatusSummary";
import Tooltip from "./components/PostParserTooltip";
import TaskSearch from "./components/TaskSearch";
import PostDetails, { AvailabilityDetails, EvidencePopover } from "./components/PostDetails";
import LocalProcessingModal from "./components/LocalProcessingModal";
import PostAcquisitionCatchUpModal from "./components/PostAcquisitionCatchUpModal";
import PostAcquisitionBatchCatchUpModal from "./components/PostAcquisitionBatchCatchUpModal";
import { canAcquirePostResult, canCatchUpPostAcquisition } from "./acquisitionEligibility";
import { ParsingRecordContext, ParsingRecordEmptyState } from "./components/ParsingRecordContext";
import {
  buildExportRows,
  copyParserText,
  downloadInstructions,
  getDownloadInfo,
  getTargetResult,
} from "./results";
import usePostParserTaskPage from "./usePostParserTaskPage";
import usePostTaskDeepLink from "./usePostTaskDeepLink";
import { loadPostParserExportTasks } from "./exportTasks";
import { postAcquireInput, postSourceRef, postProcessingHistoryFilter } from "./acquireAdapter";
import { sourceRefKey, type SourceRef } from "@/components/Acquire/sourceHistory";
import { useSourceRuns } from "@/components/Acquire/useSourceRuns";
import SourceExecutionSummary from "@/components/Acquire/SourceExecutionSummary";
import SourceExecutionHistory from "@/components/Acquire/SourceExecutionHistory";
import { useSourceAcquisitions } from "@/components/Acquire/useSourceAcquisitions";
import SourceAcquisitionSummary from "@/components/Acquire/SourceAcquisitionSummary";
import AcquisitionHandoffNotice from "@/components/Acquire/AcquisitionHandoffNotice";
import { getTaskDisplayStatus, taskStatusGroups, type TaskStatusGroup } from "./taskStatus";

import {
  Alert,
  Button,
  Chip,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Modal,
  Pagination,
  Select,
  Spinner,
  toast,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import TampermonkeyInstallButton from "@/components/ThirdPartyConfig/base/TampermonkeyInstallButton";
import WorkflowRunsDrawer from "@/components/Workflow/WorkflowRunsDrawer";
import CloudDownloadModal from "@/components/CloudDownload/AsyncCloudDownloadModal";
import WorkflowIntegrationHint from "@/components/Workflow/WorkflowIntegrationHint";
import BApi from "@/sdk/BApi";
import { openExternalUrl } from "@/utils/openExternalUrl";
import {
  BTaskStatus,
  PostParserSource,
  PostParseTarget,
  PostParseTargetLabel,
  ThirdPartyId,
  WorkflowRunStatus,
} from "@/sdk/constants";
import { useSoulPlusOptionsStore, useThirdPartyOptionsStore } from "@/stores/options";
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
  const [keyword, setKeyword] = useState("");
  const [requestedPage, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [statusFilter, setStatusFilter] = useState<TaskStatusGroup | "all">("all");
  const [reasonCode, setReasonCode] = useState<string>();
  const changeSearch = (value: string) => {
    setKeyword(value);
    setPage(1);
  };
  const clearFilters = () => {
    changeSearch("");
    setStatusFilter("all");
    setReasonCode(undefined);
  };
  const [locateRequest, setLocateRequest] = useState<{ taskId: number; sequence: number }>();
  const nextLocationSequence = useRef(0);
  const linkedTaskId = usePostTaskDeepLink((taskId) => {
    setKeyword("");
    setStatusFilter("all");
    setReasonCode(undefined);
    setPage(1);
    setLocateRequest({ taskId, sequence: ++nextLocationSequence.current });
  });
  const acceptedPage = useRef(requestedPage);
  const consumeLocation = useCallback((sequence: number) => {
    setPage(acceptedPage.current);
    setLocateRequest((current) => (current?.sequence === sequence ? undefined : current));
  }, []);
  const search = keyword.trim().toLocaleLowerCase();
  const taskPage = usePostParserTaskPage({
    keyword,
    status: statusFilter,
    reasonCode,
    pageIndex: requestedPage,
    pageSize,
    locateTaskId: locateRequest?.taskId,
  });
  acceptedPage.current = taskPage.data?.pageIndex ?? requestedPage;
  const tasks = taskPage.data?.items ?? [];
  const pageScroll = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    if (taskPage.data && !taskPage.data.items.length && pageScroll.current)
      pageScroll.current.scrollTop = 0;
  }, [taskPage.scope]);
  const busyRef = useRef(false);
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();
  const [confirmingDeletion, setConfirmingDeletion] = useState(false);
  const deletionDialogOpen = useRef(false);
  const [confirmingPurchase, setConfirmingPurchase] = useState(false);
  const purchaseDialogOpen = useRef(false);
  const [confirmingCatchUp, setConfirmingCatchUp] = useState(false);
  const catchUpDialogOpen = useRef(false);
  const deletionPageMounted = useRef(true);
  useEffect(() => {
    deletionPageMounted.current = true;
    return () => {
      deletionPageMounted.current = false;
    };
  }, []);
  const [confirmingFailedReparse, setConfirmingFailedReparse] = useState(false);
  const [loadingFailedSelection, setLoadingFailedSelection] = useState(false);
  const failedReparseDialogOpen = useRef(false);
  const [failedReparseSummary, setFailedReparseSummary] = useState<{
    queuedCount: number;
    skippedCount: number;
    failedCount: number;
  }>();
  const [runs, setRuns] = useState<PostParserTask | "all">();
  const runsTaskId = runs === "all" ? undefined : runs?.id;
  const loadRuns = useCallback(
    ({ pageIndex, pageSize }: { pageIndex: number; pageSize: number }) =>
      BApi.postParser.searchPostParserWorkflowRuns({ taskId: runsTaskId, pageIndex, pageSize }),
    [runsTaskId],
  );
  const visibleRunIds = useMemo(
    () =>
      new Set(
        tasks.flatMap((task) => (task.workflowRunId ? [`workflow.run.${task.workflowRunId}`] : [])),
      ),
    [tasks],
  );
  // A downloader's byte/progress updates do not change a parser row. Subscribe
  // only to the visible parser executions' status and stage, not the global array.
  const liveRunState = useBTasksStore((state) =>
    JSON.stringify(
      state.tasks.flatMap((task) => {
        if (!visibleRunIds.has(task.id) || !processingTaskStatuses.has(task.status)) return [];
        const stage =
          task.data && typeof task.data === "object" && "stage" in task.data
            ? task.data.stage
            : undefined;
        return [[task.id, task.status, typeof stage === "string" ? stage : null]];
      }),
    ),
  );
  const activeRunTasks = useMemo(
    () =>
      new Map(
        (JSON.parse(liveRunState) as [string, BTaskStatus, string | null][]).map(
          ([id, status, stage]) => [id, { status, data: { stage } }] as const,
        ),
      ),
    [liveRunState],
  );
  const dispatching = useBTasksStore((state) =>
    state.tasks.some(
      (task) =>
        task.id === "ParseAllPosts" &&
        ![BTaskStatus.Completed, BTaskStatus.Cancelled, BTaskStatus.Error].includes(task.status),
    ),
  );
  const taskStatuses = useMemo(
    () =>
      new Map(
        tasks.map((task) => [
          task.id,
          getTaskDisplayStatus(
            task,
            task.workflowRunId
              ? activeRunTasks.get(`workflow.run.${task.workflowRunId}`)
              : undefined,
            ["fetch", "retry", "reparse", "purchase"].some(
              (actionName) => busy === `${actionName}-${task.id}`,
            ),
          ),
        ]),
      ),
    [tasks, activeRunTasks, busy],
  );
  const totalPages = Math.max(1, Math.ceil((taskPage.data?.totalCount ?? 0) / pageSize));
  const page = taskPage.data?.pageIndex ?? requestedPage;
  const pageStart = (page - 1) * pageSize;
  const pageTasks = tasks;

  useEffect(() => {
    // Deleting the last page must not leave a stale page that reappears on later updates.
    if (taskPage.data) setPage(taskPage.data.pageIndex);
  }, [taskPage.data?.pageIndex]);

  const sourceHistory = useSourceRuns(
    pageTasks.map((task) => postSourceRef(task)),
    JSON.stringify(
      pageTasks.map((task) => [task.id, task.revision, task.workflowRunId, task.workflowStatus]),
    ),
    postProcessingHistoryFilter,
  );
  const [sourceHistoryScope, setSourceHistoryScope] = useState<SourceRef>();
  const sourceAcquisitions = useSourceAcquisitions(
    pageTasks.map((task) => postSourceRef(task)),
    JSON.stringify(
      pageTasks.map((task) => [
        task.id,
        task.acquisitionHandoff?.status,
        task.acquisitionHandoff?.acquisitionTaskId,
      ]),
    ),
  );
  const refreshAcquisitions = useCallback(() => {
    sourceAcquisitions.refresh();
    sourceHistory.refresh();
  }, [sourceAcquisitions.refresh, sourceHistory.refresh]);
  const firstRunningTask = taskPage.data?.runningTaskId
    ? { id: taskPage.data.runningTaskId }
    : undefined;
  const locateRunningTask = () => {
    if (!firstRunningTask) return;
    setKeyword("");
    setStatusFilter("all");
    setReasonCode(undefined);
    setPage(1);
    setLocateRequest({
      taskId: firstRunningTask.id,
      sequence: ++nextLocationSequence.current,
    });
  };
  const hasStartableTasks = (taskPage.data?.startableCount ?? 0) > 0;
  const failedTaskCount = taskPage.data?.statusCounts.failed;
  const deletionCount = taskPage.data?.totalCount;
  const deletionQuery = JSON.stringify([keyword.trim(), statusFilter, reasonCode ?? ""]);
  const latestDeletionQuery = useRef(deletionQuery);
  latestDeletionQuery.current = deletionQuery;
  const refresh = taskPage.refresh;
  const purchaseOptionsVersion = JSON.stringify([autoBuyThreshold, minimumRemainingCoins]);
  const previousPurchaseOptions = useRef(purchaseOptionsVersion);
  useEffect(() => {
    if (previousPurchaseOptions.current !== purchaseOptionsVersion) {
      previousPurchaseOptions.current = purchaseOptionsVersion;
      void refresh();
    }
  }, [purchaseOptionsVersion, refresh]);

  const action = useCallback(
    async (
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

        if (response.code)
          throw new Error(response.message || t<string>("postParser.result.failed"));
        await refresh();
        sourceHistory.refresh();
      } catch (failure) {
        setError(
          failure instanceof Error ? failure.message : t<string>("postParser.result.failed"),
        );
        if (rethrow) throw failure;
      } finally {
        busyRef.current = false;
        setBusy(undefined);
      }
    },
    [refresh, sourceHistory.refresh, t],
  );

  const openReparseFailed = (
    selection: { taskId: number; revision: number; workflowRunId?: number }[],
  ) => {
    failedReparseDialogOpen.current = true;
    setConfirmingFailedReparse(true);
    const release = () => {
      failedReparseDialogOpen.current = false;
      setConfirmingFailedReparse(false);
    };
    createPortal(Modal, {
      defaultVisible: true,
      title: t("postParser.reparseFailed.title", { count: selection.length }),
      size: "lg",
      children: (
        <div className="space-y-3 text-sm leading-relaxed">
          <p>{t("postParser.reparseFailed.scope", { count: selection.length })}</p>
          <p>{t("postParser.reparseFailed.behavior")}</p>
        </div>
      ),
      footer: {
        actions: ["cancel", "ok"],
        okProps: {
          color: "warning",
          children: t("postParser.reparseFailed.confirm", { count: selection.length }),
        },
      },
      onClose: release,
      onDestroyed: release,
      onOk: () =>
        action(
          "reparseFailed",
          async () => {
            setFailedReparseSummary(undefined);
            const response = await BApi.postParser.reParseFailedPostParserTasks({
              tasks: selection,
            });
            if (!response.code && response.data)
              setFailedReparseSummary({
                queuedCount: response.data.queuedCount,
                skippedCount: response.data.skippedCount,
                failedCount: response.data.failedCount,
              });
            return response;
          },
          true,
        ),
    });
  };

  const confirmReparseFailed = async () => {
    if (busyRef.current || failedReparseDialogOpen.current || !failedTaskCount) return;
    failedReparseDialogOpen.current = true;
    setConfirmingFailedReparse(true);
    setLoadingFailedSelection(true);
    let opened = false;
    try {
      const response = await BApi.postParser.getFailedPostParserTaskSelection({
        sensitiveResponse: true,
      });
      if (response.code || !response.data || !Array.isArray(response.data.tasks))
        throw new Error(response.message || t("postParser.result.failed"));
      if (!response.data.tasks.length) {
        toast.success(t("postParser.reparseFailed.none"));
        await refresh();
      } else {
        // This lightweight global snapshot remains frozen while the confirmation is open.
        openReparseFailed(response.data.tasks);
        opened = true;
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : t("postParser.result.failed"));
    } finally {
      setLoadingFailedSelection(false);
      if (!opened) {
        failedReparseDialogOpen.current = false;
        setConfirmingFailedReparse(false);
      }
    }
  };

  const previewDeletion = async () => {
    if (
      busyRef.current ||
      deletionDialogOpen.current ||
      !deletionCount ||
      taskPage.loading ||
      taskPage.changing ||
      taskPage.error ||
      failedReparseDialogOpen.current ||
      catchUpDialogOpen.current ||
      purchaseDialogOpen.current
    )
      return;
    const scope = { keyword: keyword.trim() || undefined, status: statusFilter, reasonCode };
    const queryAtClick = deletionQuery;
    deletionDialogOpen.current = true;
    setConfirmingDeletion(true);
    busyRef.current = true;
    setBusy("previewDeletion");
    setError(undefined);
    let opened = false;
    const release = () => {
      deletionDialogOpen.current = false;
      setConfirmingDeletion(false);
    };
    try {
      const response = await BApi.postParser.previewPostParserTaskDeletion(scope, {
        sensitiveResponse: true,
      });
      if (!deletionPageMounted.current) return;
      if (queryAtClick !== latestDeletionQuery.current)
        throw new Error(t("postParser.deleteBatch.queryChanged"));
      const preview = response.data;
      if (
        response.code ||
        !preview ||
        !Array.isArray(preview.tasks) ||
        !Number.isSafeInteger(preview.count) ||
        preview.count !== preview.tasks.length
      )
        throw new Error(response.message || t("postParser.result.failed"));
      const selectedStatus = preview.scope.status ?? "all";
      if (
        !(selectedStatus === "all" || taskStatusGroups.includes(selectedStatus as TaskStatusGroup))
      )
        throw new Error(t("postParser.result.failed"));
      if (
        (preview.scope.keyword?.trim() ?? "") !== (scope.keyword ?? "") ||
        selectedStatus !== scope.status ||
        (preview.scope.reasonCode ?? "") !== (scope.reasonCode ?? "")
      )
        throw new Error(t("postParser.deleteBatch.queryChanged"));
      if (!preview.count) {
        toast.success(t("postParser.deleteBatch.none"));
        await refresh();
        return;
      }
      // Keep this exact server selection. Confirm never searches again or appends new matches.
      createPortal(DeleteTasksConfirmationModal, {
        count: preview.count,
        keyword: preview.scope.keyword ?? "",
        status: selectedStatus as TaskStatusGroup | "all",
        reasonCode: preview.scope.reasonCode,
        onClose: release,
        onDestroyed: release,
        onConfirm: async () => {
          if (!deletionPageMounted.current)
            throw new Error(t("postParser.deleteBatch.queryChanged"));
          if (busyRef.current) throw new Error(t("postParser.deleteBatch.busy"));
          await action(
            "deleteSelection",
            async () => {
              const confirmed = await BApi.postParser.confirmPostParserTaskDeletion({
                scope: preview.scope,
                tasks: preview.tasks,
              });
              if (!confirmed.code && confirmed.data)
                toast.success(
                  t("postParser.deleteBatch.result", {
                    deleted: confirmed.data.deletedCount,
                    skipped: confirmed.data.skippedCount,
                  }),
                );
              return confirmed;
            },
            true,
          );
        },
      });
      opened = true;
    } catch (failure) {
      if (deletionPageMounted.current)
        setError(failure instanceof Error ? failure.message : t("postParser.result.failed"));
    } finally {
      busyRef.current = false;
      if (deletionPageMounted.current) {
        setBusy(undefined);
        if (!opened) release();
      } else deletionDialogOpen.current = false;
    }
  };

  const openBatchCatchUp = () => {
    if (
      busyRef.current ||
      catchUpDialogOpen.current ||
      deletionDialogOpen.current ||
      purchaseDialogOpen.current ||
      failedReparseDialogOpen.current ||
      !deletionPageMounted.current ||
      !deletionCount ||
      taskPage.loading ||
      taskPage.changing ||
      taskPage.error
    )
      return;
    catchUpDialogOpen.current = true;
    setConfirmingCatchUp(true);
    const release = () => {
      catchUpDialogOpen.current = false;
      if (deletionPageMounted.current) setConfirmingCatchUp(false);
    };
    createPortal(PostAcquisitionBatchCatchUpModal, {
      scope: { keyword: keyword.trim() || undefined, status: statusFilter, reasonCode },
      onChanged: () => {
        void refresh();
        sourceHistory.refresh();
      },
      onClosed: release,
      onDestroyed: release,
    });
  };

  const openContentReview = useCallback(
    (scope: ContentReviewScope) => {
      if (
        busyRef.current ||
        purchaseDialogOpen.current ||
        deletionDialogOpen.current ||
        failedReparseDialogOpen.current ||
        catchUpDialogOpen.current ||
        !deletionPageMounted.current
      )
        return;
      purchaseDialogOpen.current = true;
      setConfirmingPurchase(true);
      const release = () => {
        purchaseDialogOpen.current = false;
        if (deletionPageMounted.current) setConfirmingPurchase(false);
      };
      createPortal(ContentReviewDialog, {
        scope,
        onClose: release,
        onDestroyed: release,
        onChanged: () => {
          if (!deletionPageMounted.current) return;
          void refresh();
          sourceHistory.refresh();
        },
      });
    },
    [createPortal, refresh, sourceHistory.refresh],
  );

  const copy = useCallback(
    async (value: string) => {
      try {
        await copyParserText(value);
        toast.success(t<string>("postParser.result.copied"));
      } catch {
        toast.danger(t<string>("postParser.result.copyFailed"));
      }
    },
    [t],
  );

  const showContent = useCallback(
    (task: PostParserTask) =>
      createPortal(Modal, {
        defaultVisible: true,
        title: task.title || t<string>("postParser.action.viewContent"),
        size: "lg",
        children: <PostDetails task={task} />,
        footer: { actions: ["cancel"] },
      }),
    [createPortal, t],
  );

  const renderResults = useCallback(
    (task: PostParserTask) => {
      const availability = task.availability ?? getDownloadInfo(task)?.availability;
      const reason = [
        ...new Set(
          [availability?.reason, task.parsingMessage]
            .filter(Boolean)
            .map((message) => translateKnownMessage(t, message!)),
        ),
      ].join("\n");

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
              <EvidencePopover
                label={t("postParser.action.details")}
                reason={translateKnownMessage(t, task.parsingMessage)}
              />
            )}
          </div>
          {task.workflowStatus === WorkflowRunStatus.Waiting &&
            ["unknown", "expired"].includes(availability?.status ?? "") &&
            hasBlockingContentLocks(task) && (
              <p className="rounded-lg bg-warning/10 p-2 text-xs leading-relaxed text-warning-700">
                {t("postParser.review.sourceHint")}
              </p>
            )}
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
                        source={
                          task.parsingState === "complete" &&
                          ![WorkflowRunStatus.Pending, WorkflowRunStatus.Running].includes(
                            task.workflowStatus!,
                          )
                            ? postSourceRef(task, true)
                            : undefined
                        }
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
    },
    [action, createPortal, t],
  );

  const renderTask = useCallback(
    (task: PostParserTask) => {
      const data = getDownloadInfo(task);
      const status = taskStatuses.get(task.id)!;
      const processing = status.processing;
      const queued = status.group === "queued";
      const paused = status.group === "paused";
      const processingLabel = status.label;
      const locks = task.contentSnapshot?.locks?.filter((lock) => !lock.isBought) ?? [];
      const needsUnlock = hasBlockingContentLocks(task);
      const canImport = canAcquirePostResult(task, data);
      const canCatchUp = canCatchUpPostAcquisition(task, canImport);
      const hasContent = !!(task.text || task.content || task.contentSnapshot);
      const canReviewContent =
        !!task.contentSnapshot &&
        (locks.length > 0 || task.contentSnapshot.readiness?.basis === "manual");
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
                  ) : (
                    <Chip
                      color={
                        status.group === "success"
                          ? "success"
                          : status.group === "failed"
                            ? "danger"
                            : status.group === "attention"
                              ? "warning"
                              : "default"
                      }
                      size="sm"
                      variant="flat"
                    >
                      {t<string>(status.label)}
                    </Chip>
                  )}
                  {task.workflowRunId && (
                    <span className="self-center text-xs tabular-nums text-default-400">
                      {t<string>(
                        task.workflowStatus == null
                          ? "postParser.history.originalRunUnavailableBadge"
                          : "postParser.label.run",
                        { id: task.workflowRunId },
                      )}
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
              {!!task.attentionReasonCodes?.length && (
                <div
                  className="flex min-w-0 flex-wrap gap-1"
                  role="group"
                  aria-label={t("postParser.filters.reason")}
                >
                  {[...new Set(task.attentionReasonCodes)].map((code) => (
                    <Chip
                      key={code}
                      data-attention-reason={code}
                      color="warning"
                      size="sm"
                      variant="flat"
                      classNames={{
                        base: "h-auto max-w-full",
                        content: "whitespace-normal py-0.5",
                      }}
                    >
                      {attentionReasonLabel(t, code)}
                    </Chip>
                  ))}
                </div>
              )}
              {task.contentSnapshot?.readiness && !needsUnlock && locks.length > 0 && (
                <Chip size="sm" variant="flat" color="primary">
                  {t(
                    task.contentSnapshot.readiness.basis === "manual"
                      ? "postParser.contentReadiness.manualBadge"
                      : "postParser.contentReadiness.analysisBadge",
                  )}
                </Chip>
              )}
              {!!locks.length && task.contentSnapshot && (
                <LockedContentDetails content={task.contentSnapshot} source={task.source} compact />
              )}
              {needsUnlock &&
                task.contentSnapshot?.locks.some((lock) => lock.isBought) &&
                locks.length > 0 && (
                  <p className="text-xs leading-relaxed text-warning-700">
                    {t("postParser.purchaseScope.partiallyUnlocked", {
                      bought: task.contentSnapshot.locks.filter((lock) => lock.isBought).length,
                      remaining: locks.length,
                    })}
                  </p>
                )}
              <SourceAcquisitionSummary
                tasks={sourceAcquisitions.rows.get(sourceRefKey(postSourceRef(task)))}
                onChanged={refreshAcquisitions}
              />
              <AcquisitionHandoffNotice
                key={task.acquisitionHandoff?.id}
                handoff={task.acquisitionHandoff}
                hasStartedTask={
                  !!sourceAcquisitions.rows
                    .get(sourceRefKey(postSourceRef(task)))
                    ?.some(
                      (related) =>
                        related.id === task.acquisitionHandoff?.acquisitionTaskId &&
                        related.workflowRunId != null,
                    )
                }
                hasTaskSummary={
                  !!sourceAcquisitions.rows
                    .get(sourceRefKey(postSourceRef(task)))
                    ?.some((related) => related.id === task.acquisitionHandoff?.acquisitionTaskId)
                }
                onChanged={refreshAcquisitions}
                onRetry={async () => {
                  if (!task.acquisitionHandoff) return;
                  const response = await BApi.acquisition.retryAcquisitionHandoff(
                    task.acquisitionHandoff.id,
                  );

                  if (response.code) throw new Error(response.message);
                  await refresh();
                  refreshAcquisitions();
                }}
              />
              {status.group === "success" &&
                !task.acquisitionHandoff &&
                sourceAcquisitions.rows.has(sourceRefKey(postSourceRef(task))) &&
                !sourceAcquisitions.rows.get(sourceRefKey(postSourceRef(task)))?.length &&
                !sourceHistory.rows.get(sourceRefKey(postSourceRef(task)))?.runCount && (
                  <p className="text-xs leading-relaxed text-default-500">
                    {t("postParser.history.acquisitionNextStep")}
                  </p>
                )}
              <SourceExecutionSummary
                value={sourceHistory.rows.get(sourceRefKey(postSourceRef(task)))}
                hideWhenEmpty={
                  !!sourceAcquisitions.rows.get(sourceRefKey(postSourceRef(task)))?.length
                }
                loading={sourceHistory.loading}
                error={sourceHistory.error}
                onRetry={sourceHistory.refresh}
                onHistory={() => setSourceHistoryScope(postSourceRef(task))}
              />
              {task.link && (
                <div className="flex min-w-0 items-start gap-1">
                  <Button
                    className="h-auto min-w-0 justify-start overflow-hidden px-0 py-1"
                    color="primary"
                    size="sm"
                    variant="light"
                    onPress={() => openExternalUrl(task.link)}
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
                canReviewContent ||
                (!task.workflowRunId && !hasResults(task) && !task.error)) && (
                <ActionGroup stage="source">
                  {hasContent && (
                    <Button
                      size="sm"
                      variant="light"
                      startContent={<AiOutlineFileText aria-hidden />}
                      onPress={() => showContent(task)}
                    >
                      {t("postParser.action.viewContent")}
                    </Button>
                  )}
                  {canReviewContent && (
                    <Button
                      size="sm"
                      variant="flat"
                      color="primary"
                      isDisabled={!!busy || processing || confirmingPurchase}
                      onPress={() => openContentReview({ status: "all", taskIds: [task.id] })}
                    >
                      {t("postParser.contentPurchaseReview.title")}
                    </Button>
                  )}
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
                  {!processing && task.workflowStatus === WorkflowRunStatus.Waiting && (
                    <Button
                      color="warning"
                      size="sm"
                      variant="flat"
                      startContent={<AiOutlineQuestionCircle aria-hidden />}
                      onPress={() => setRuns(task)}
                    >
                      {t("postParser.action.reviewWaiting")}
                    </Button>
                  )}
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
                  <Dropdown>
                    <DropdownTrigger>
                      <Button
                        color="primary"
                        size="sm"
                        variant="flat"
                        startContent={<AiOutlineCloudDownload aria-hidden />}
                      >
                        {t("acquire.plan.create")}
                      </Button>
                    </DropdownTrigger>
                    <DropdownMenu
                      aria-label={t("postParser.catchUp.menu")}
                      onAction={(key) => {
                        if (key === "configured" && canCatchUp) {
                          createPortal(PostAcquisitionCatchUpModal, {
                            task,
                            onChanged: () => {
                              void refresh();
                              refreshAcquisitions();
                            },
                          });
                        } else if (key === "manual") {
                          createPortal(CloudDownloadModal, {
                            ...postAcquireInput(task),
                            onCreated: refreshAcquisitions,
                          });
                        }
                      }}
                    >
                      {canCatchUp ? (
                        <DropdownItem
                          key="configured"
                          aria-label={t("postParser.catchUp.title")}
                          startContent={<AiOutlinePlayCircle aria-hidden />}
                          description={t("postParser.catchUp.menuHint")}
                        >
                          {t("postParser.catchUp.title")}
                        </DropdownItem>
                      ) : null}
                      <DropdownItem
                        key="manual"
                        aria-label={t("postParser.catchUp.manual")}
                        startContent={<AiOutlineCloudDownload aria-hidden />}
                      >
                        {t("postParser.catchUp.manual")}
                      </DropdownItem>
                    </DropdownMenu>
                  </Dropdown>
                </ActionGroup>
              )}
              {canImport && (
                <ActionGroup stage="processing">
                  <Button
                    size="sm"
                    startContent={<AiOutlineFolderOpen aria-hidden />}
                    variant="light"
                    onPress={() =>
                      createPortal(LocalProcessingModal, { task, onCreated: sourceHistory.refresh })
                    }
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
                  {t("acquire.history.parsing")}
                </Button>
                <Button
                  size="sm"
                  variant="light"
                  startContent={<AiOutlineHistory aria-hidden />}
                  onPress={() => setSourceHistoryScope(postSourceRef(task))}
                >
                  {t("acquire.history.title")}
                </Button>
                <Button
                  color="danger"
                  isDisabled={!!busy}
                  size="sm"
                  variant="light"
                  startContent={<AiOutlineDelete aria-hidden />}
                  onPress={() =>
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t("postParser.delete.title", { id: task.id }),
                      children: (
                        <div className="space-y-3 text-sm">
                          <p className="break-words font-medium">
                            {task.title || t("postParser.label.untitled")}
                          </p>
                          <p>{t("postParser.delete.description")}</p>
                        </div>
                      ),
                      footer: {
                        actions: ["cancel", "ok"],
                        okProps: { color: "danger", children: t("postParser.delete.confirm") },
                      },
                      onOk: () =>
                        action(
                          `delete-${task.id}`,
                          () => BApi.postParser.deletePostParserTask(task.id),
                          true,
                        ),
                    })
                  }
                >
                  {t("postParser.action.delete")}
                </Button>
              </ActionGroup>
            </div>
          </div>
        </>
      );
    },
    [
      action,
      busy,
      copy,
      createPortal,
      refresh,
      refreshAcquisitions,
      renderResults,
      openContentReview,
      confirmingPurchase,
      showContent,
      sourceAcquisitions.rows,
      sourceHistory.error,
      sourceHistory.loading,
      sourceHistory.refresh,
      sourceHistory.rows,
      t,
      taskStatuses,
    ],
  );

  return (
    <div
      className="flex h-full min-h-0 min-w-0 flex-col gap-2 overflow-hidden"
      data-post-parser-page
    >
      <div className="flex shrink-0 flex-wrap items-center justify-between gap-x-3 gap-y-1">
        <div className="min-w-0">
          <h1 className="flex items-center gap-2 text-lg font-semibold">
            <AiOutlineFileText aria-hidden className="text-primary" />
            {t<string>("postParser.page.title")}
          </h1>
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
      <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 rounded-xl bg-default-50 px-2 py-1.5">
        <div className="flex min-w-0 flex-[1_1_34rem] flex-wrap items-center gap-2">
          <TaskSearch
            isFiltered={statusFilter !== "all" || !!reasonCode}
            shown={taskPage.data?.totalCount}
            total={taskPage.data?.globalTotal}
            value={keyword}
            onChange={changeSearch}
          />
          {(search || statusFilter !== "all") && (
            <Button size="sm" variant="light" onPress={clearFilters}>
              {t<string>("postParser.filters.clear")}
            </Button>
          )}
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
          <Button
            color="warning"
            isDisabled={!failedTaskCount || !!busy || confirmingFailedReparse}
            isLoading={busy === "reparseFailed" || loadingFailedSelection}
            size="sm"
            startContent={<AiOutlineReload aria-hidden />}
            variant="flat"
            onPress={confirmReparseFailed}
          >
            {failedTaskCount == null
              ? t("postParser.action.reparseFailedUnknown")
              : t("postParser.action.reparseFailed", { count: failedTaskCount })}
          </Button>
          <Tooltip
            content={t(
              firstRunningTask
                ? "postParser.action.locateRunningHint"
                : "postParser.action.noRunningTask",
            )}
          >
            <span className="inline-flex" tabIndex={firstRunningTask ? undefined : 0}>
              <Button
                isIconOnly
                aria-label={t("postParser.action.locateRunning")}
                isDisabled={!firstRunningTask}
                size="sm"
                variant="light"
                onPress={locateRunningTask}
              >
                <AiOutlineAim aria-hidden className="text-base" />
              </Button>
            </span>
          </Tooltip>
        </div>
        <div className="flex flex-wrap items-center gap-1">
          <FilteredTaskActions
            count={deletionCount}
            filtered={!!search || statusFilter !== "all" || !!reasonCode}
            disabled={
              !!busy ||
              confirmingDeletion ||
              confirmingPurchase ||
              confirmingFailedReparse ||
              confirmingCatchUp ||
              taskPage.loading ||
              taskPage.changing ||
              taskPage.error
            }
            loading={
              busy === "previewDeletion" ||
              busy === "deleteSelection" ||
              busy === "purchaseSelection" ||
              busy === "exportInstructions" ||
              busy === "export"
            }
            onDelete={() => void previewDeletion()}
            onCatchUp={openBatchCatchUp}
            onExportInstructions={() =>
              void action("exportInstructions", async () => {
                downloadInstructions(
                  await loadPostParserExportTasks({
                    keyword: keyword.trim() || undefined,
                    status: statusFilter,
                    reasonCode,
                  }),
                );
                return { code: 0 };
              })
            }
            onExportExcel={() =>
              void action("export", async () => {
                const rows = buildExportRows(
                  await loadPostParserExportTasks({
                    keyword: keyword.trim() || undefined,
                    status: statusFilter,
                    reasonCode,
                  }),
                  (key) => t<string>(`PostParseTarget.${key}`),
                );
                const workbook = XLSX.utils.book_new();
                XLSX.utils.book_append_sheet(
                  workbook,
                  XLSX.utils.json_to_sheet(rows),
                  "PostParser",
                );
                XLSX.writeFile(
                  workbook,
                  `post-parser-export-${new Date().toISOString().slice(0, 10)}.xlsx`,
                );
                return { code: 0 };
              })
            }
          />
          <Button
            size="sm"
            startContent={<AiOutlineHistory aria-hidden />}
            variant="light"
            onPress={() => setRuns("all")}
          >
            {t<string>("postParser.action.viewAllRuns")}
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
      <div
        ref={pageScroll}
        className="min-h-0 flex-1 overflow-auto overscroll-contain"
        data-post-parser-scroll
        tabIndex={0}
      >
        <div className="flex min-h-full min-w-0 flex-col gap-2">
          <div className="shrink-0 space-y-1 rounded-xl bg-default-50 px-2 py-1.5">
            <TaskStatusSummary
              summary={
                taskPage.data
                  ? {
                      total: taskPage.data.globalTotal,
                      counts: taskPage.data.statusCounts,
                      stages: taskPage.data.runningStages,
                    }
                  : undefined
              }
              selected={statusFilter}
              onSelect={(group) => {
                setStatusFilter(group);
                if (group !== "attention") setReasonCode(undefined);
                setPage(1);
              }}
            />
            {statusFilter === "attention" && (
              <AttentionReasonFilter
                counts={taskPage.data?.attentionReasonCounts}
                value={reasonCode}
                onChange={(code) => {
                  setReasonCode(code);
                  setPage(1);
                }}
                disabled={!taskPage.data || confirmingDeletion || confirmingPurchase}
              />
            )}
            {statusFilter === "attention" && (
              <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-t border-default-200/60 pt-1.5 text-xs text-default-500">
                <span>
                  {taskPage.data
                    ? t("postParser.contentPurchaseReview.summary", {
                        count: taskPage.data.totalCount,
                      })
                    : t("postParser.loading.counts")}
                </span>
                <Button
                  size="sm"
                  variant="flat"
                  color="primary"
                  className="h-7 min-w-0 px-2 text-xs"
                  isDisabled={
                    !!busy ||
                    confirmingDeletion ||
                    confirmingPurchase ||
                    confirmingFailedReparse ||
                    !taskPage.data?.totalCount ||
                    taskPage.changing ||
                    taskPage.error
                  }
                  onPress={() =>
                    openContentReview({
                      keyword: keyword.trim() || undefined,
                      status: statusFilter,
                      reasonCode,
                    })
                  }
                >
                  {t("postParser.contentPurchaseReview.title")}
                </Button>
              </div>
            )}
          </div>

          {error && (
            <p className="shrink-0 text-sm text-danger" role="alert">
              {error}
            </p>
          )}
          {failedReparseSummary && (
            <p
              className={`shrink-0 text-sm ${failedReparseSummary.failedCount ? "text-warning-600" : "text-success-600"}`}
              role="status"
            >
              {t("postParser.reparseFailed.queued", {
                queued: failedReparseSummary.queuedCount,
                skipped: failedReparseSummary.skippedCount,
                failed: failedReparseSummary.failedCount,
              })}
            </p>
          )}
          {taskPage.error && !!taskPage.data && (
            <div role="alert" className="flex shrink-0 items-center gap-3 text-sm text-warning-600">
              <span>{t("postParser.loading.refreshFailed")}</span>
              <Button size="sm" variant="flat" onPress={() => void refresh()}>
                {t("common.action.retry")}
              </Button>
            </div>
          )}
          {linkedTaskId &&
            locateRequest?.taskId === linkedTaskId &&
            taskPage.data &&
            !taskPage.loading &&
            !taskPage.changing &&
            !taskPage.error &&
            !tasks.some((task) => task.id === linkedTaskId) && (
              <p role="status" className="shrink-0 text-sm text-warning-600">
                {t("acquisition.origin.taskUnavailable", { id: linkedTaskId })}
              </p>
            )}
          {!taskPage.data ? (
            <div
              className="flex min-h-72 flex-1 flex-col items-center justify-center gap-3 rounded-xl border border-default-200 text-center"
              aria-busy={taskPage.loading}
            >
              {taskPage.loading ? (
                <>
                  <Spinner size="lg" aria-label={t("postParser.loading.tasks")} />
                  <p className="text-sm text-default-500">{t("postParser.loading.tasks")}</p>
                </>
              ) : (
                <>
                  <p role="alert" className="text-sm text-danger">
                    {t("postParser.loading.failed")}
                  </p>
                  <Button size="sm" variant="flat" onPress={() => void refresh()}>
                    {t("common.action.retry")}
                  </Button>
                </>
              )}
            </div>
          ) : tasks.length > 0 ? (
            <div className="relative flex shrink-0 flex-col" aria-busy={taskPage.loading}>
              {taskPage.changing && (
                <div
                  className="absolute inset-0 z-10 flex items-center justify-center gap-3 rounded-xl bg-background/75 text-sm"
                  role="status"
                >
                  {taskPage.loading && <Spinner size="sm" />}
                  {t(taskPage.loading ? "postParser.loading.page" : "postParser.loading.failed")}
                </div>
              )}
              <div
                className="flex min-w-0 flex-col"
                ref={(element) => {
                  if (element) element.inert = taskPage.changing;
                }}
              >
                <TaskList
                  key={taskPage.scope}
                  locateRequest={locateRequest}
                  renderTask={renderTask}
                  search=""
                  pageKey={taskPage.scope}
                  scrollRef={pageScroll}
                  tasks={pageTasks}
                  onLocated={consumeLocation}
                />
              </div>
            </div>
          ) : (
            <div
              className="relative flex min-h-72 flex-1 flex-col items-center justify-center rounded-xl border border-dashed border-default-200 px-4 py-12 text-center"
              aria-busy={taskPage.loading}
            >
              {taskPage.changing && (
                <div
                  className="absolute inset-0 z-10 flex items-center justify-center gap-3 rounded-xl bg-background/90 text-sm"
                  role="status"
                >
                  {taskPage.loading && <Spinner size="sm" />}
                  {t(taskPage.loading ? "postParser.loading.page" : "postParser.loading.failed")}
                </div>
              )}
              <p className="text-sm text-default-500" role="status">
                {t<string>(
                  taskPage.data.globalTotal ? "postParser.search.empty" : "postParser.page.empty",
                )}
              </p>
              {taskPage.data.globalTotal > 0 && (
                <Button className="mt-3" size="sm" variant="flat" onPress={clearFilters}>
                  {t<string>(
                    statusFilter === "all" ? "postParser.search.clear" : "postParser.filters.clear",
                  )}
                </Button>
              )}
            </div>
          )}
        </div>
      </div>
      {!!taskPage.data?.globalTotal && (
        <div className="flex shrink-0 flex-wrap items-center justify-between gap-x-3 gap-y-1">
          <span className="text-xs tabular-nums text-default-500" aria-live="polite">
            {t<string>("postParser.pagination.range", {
              start: tasks.length ? pageStart + 1 : 0,
              end: pageStart + pageTasks.length,
              total: taskPage.data.totalCount,
            })}
          </span>
          <div className="flex flex-wrap items-center gap-3">
            <Select
              aria-label={t<string>("postParser.pagination.pageSize")}
              className="w-32"
              disallowEmptySelection
              dataSource={[20, 50, 100].map((size) => ({
                value: String(size),
                label: t<string>("postParser.pagination.perPage", { count: size }),
              }))}
              selectedKeys={[String(pageSize)]}
              size="sm"
              onSelectionChange={(keys) => {
                const size = Number(Array.from(keys)[0]);
                if (![20, 50, 100].includes(size)) return;
                setPageSize(size);
                setPage(1);
              }}
            />
            <Pagination
              aria-label={t<string>("postParser.pagination.label")}
              page={page}
              total={totalPages}
              showControls
              size="sm"
              onChange={setPage}
            />
          </div>
        </div>
      )}
      {(sourceHistory.error || sourceAcquisitions.error) && (
        <div className="flex items-center gap-2 text-xs text-warning-600" role="status">
          <span>
            {t(sourceAcquisitions.error ? "acquire.plan.loadFailed" : "acquire.history.loadFailed")}
          </span>
          <Button size="sm" variant="light" onPress={refreshAcquisitions}>
            {t("common.action.retry")}
          </Button>
        </div>
      )}
      {sourceHistoryScope && (
        <SourceExecutionHistory
          source={sourceHistoryScope}
          filter={postProcessingHistoryFilter}
          onClose={() => {
            setSourceHistoryScope(undefined);
            sourceHistory.refresh();
          }}
        />
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
              : t<string>("postParser.history.taskShort", { id: runs.id })
          }
          headerContent={
            runs !== "all" ? (
              <>
                <ParsingRecordContext task={runs} />
                {runs.workflowStatus === WorkflowRunStatus.Waiting && (
                  <p className="rounded-lg bg-warning/10 p-3 text-sm leading-relaxed">
                    {t("postParser.review.waitingHint")}
                  </p>
                )}
              </>
            ) : undefined
          }
          emptyContent={
            runs !== "all" ? (
              <ParsingRecordEmptyState
                task={runs}
                onProcessingHistory={() => {
                  setSourceHistoryScope(postSourceRef(runs));
                  setRuns(undefined);
                }}
              />
            ) : undefined
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

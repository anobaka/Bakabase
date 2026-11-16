import { type ReactNode, useCallback, useEffect, useRef, useState } from "react";
import { acquisitionTaskTitle } from "@/components/Acquire/acquisitionTaskTitle";
import { useTranslation } from "react-i18next";
import { Button, Chip, Input, Modal, Popover, Spinner, Tooltip } from "@/components/bakaui";
import { LuFolderCog, LuInfo } from "react-icons/lu";
import type { DestroyableProps } from "@/components/bakaui/types";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import FileSystemSelectorButton from "@/components/FileSystemSelector/components/Button";
import { WorkflowRunDetailContent } from "@/components/Workflow/WorkflowRunDetailModal";
import { acquisitionStageLabel, acquisitionTaskStage, acquisitionDownloadActivity } from "../taskStage";
import { AcquisitionStatus } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import ExternalLink from "@/components/ExternalLink";
import TaskPlanSnapshot, { type AcquisitionOptions } from "./TaskPlanSnapshot";
import TaskPlanSources from "./TaskPlanSources";
import TaskContinuationModal from "./TaskContinuationModal";
import TaskDiagnosticsModal, { type DiagnosticsPage } from "./TaskDiagnosticsModal";
import { HistoryOutlined } from "@ant-design/icons";
import { planTaskApi } from "./api";
import AcquisitionTiming from "../AcquisitionTiming";
import AcquisitionCreationOrigin, { postTaskId } from "../AcquisitionCreationOrigin";
import { ResumePostSourceContext } from "@/components/Workflow/ResumePresentation";
import type { PlanTaskDetail } from "./models";
import PlanReplanForm from "./PlanReplanForm";
import CompletedProcessingRevisionForm, {
  hasCompletedProcessingInputs,
  hasStoppedProcessingInputs,
  hasPublicationStorageInputs,
  hasReadableCurrentDirectoryInputs,
  hasStageDirectoryInputs,
} from "./CompletedProcessingRevisionForm";
import TaskDirectoriesModal from "./TaskDirectoriesModal";
import KeepPreparedFilesModal, { canKeepPreparedFiles } from "./KeepPreparedFilesModal";
import DraftPreparation from "./DraftPreparation";
import TaskDeliverySettings from "./TaskDeliverySettings";
import TaskStoragePolicySettings from "./TaskStoragePolicySettings";
import SourceCorrectionPanel from "./SourceCorrectionPanel";
import SourceRevisionModal from "./SourceRevisionModal";
import useSourceRevisionAvailability from "./useSourceRevisionAvailability";
import { hasSavedStorageProcessingCheckpoint } from "./savedProcessingCheckpoint";
import useSourceCorrections from "./useSourceCorrections";
import SourceAvailabilityReviewModal from "@/components/SourceAvailability/SourceAvailabilityReviewModal";
import { sourceAvailabilityApi } from "@/components/SourceAvailability/api";
import type { SourceAvailabilityRow } from "@/components/SourceAvailability/models";
import RuntimeOverviewSummary, { RuntimeSources } from "./RuntimeOverviewSummary";
import useRuntimeOverviews from "./useRuntimeOverviews";
import { currentProcessingWaitExplanation, downloadContinuationLabel, hasCurrentRuntimeProblem, runtimeProblemLabel } from "./runtimeOverview";
import ProcessingProblemSummary from "@/components/Workflow/ProcessingProblemSummary";
import AcquisitionAdaptiveProcessingModal from "@/pages/acquisition/components/AcquisitionAdaptiveProcessingModal";
import AcquisitionRecoveryModal from "@/pages/acquisition/components/AcquisitionRecoveryModal";
import StorageReserveOption from "./StorageReserveOption";
import DeleteAcquisitionTasksModal from "@/pages/acquisition/components/DeleteAcquisitionTasksModal";
import AcquisitionExecutionTimeline, {
  asAcquisitionStage,
  type AcquisitionStage,
} from "./AcquisitionExecutionTimeline";
import {
  canReviseDeliveryLayout,
} from "./CompletedDeliveryLayoutRevision";
import type { AttemptRunOptions } from "./useAttemptStorageReserve";

export default function PlanTaskDetailModal({
  taskId,
  onChanged,
  onDestroyed,
}: DestroyableProps & {
  taskId: number;
  onChanged?: () => void;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [detail, setDetail] = useState<PlanTaskDetail>();
  const [acquisitionOptions, setAcquisitionOptions] = useState<AcquisitionOptions>();
  const [sourceHealth, setSourceHealth] = useState<SourceAvailabilityRow[]>([]);
  const loadHealth = useCallback(async () => {
    try {
      setSourceHealth(await sourceAvailabilityApi.acquisition(taskId));
    } catch {
      /* A cache read must not hide the task or trigger remote checks. */
    }
  }, [taskId]);
  useEffect(() => {
    void loadHealth();
  }, [loadHealth]);
  useEffect(() => {
    let active = true;
    const getOptions = BApi.acquisition.getAcquisitionOptions;
    if (typeof getOptions !== "function") return;
    void getOptions({ sensitiveResponse: true, showErrorToast: false })
      .then((response) => {
        if (active && !response.code && response.data) setAcquisitionOptions(response.data);
      })
      .catch(() => {
        /* Current settings are advisory; a failed read must not hide the task. */
      });
    return () => {
      active = false;
    };
  }, []);
  const [visible, setVisible] = useState(true);
  const [query, setQuery] = useState("");
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [actionFailed, setActionFailed] = useState(false);
  const [directoryRevision, setDirectoryRevision] = useState(0);
  const [directoriesOpen, setDirectoriesOpen] = useState(false);
  const [diagnostics, setDiagnostics] = useState<DiagnosticsPage>();
  const [reserveSubmitting, setReserveSubmitting] = useState(false);
  const [attemptRunOverride, setAttemptRunOverride] = useState<{
    revision: number;
    options: AttemptRunOptions;
  }>();
  const [currentRunView, setCurrentRunView] = useState<number>();
  const showRun = (id: number) => setDiagnostics({ tab: "files", runId: id });
  const [continuation, setContinuation] = useState<{
    snapshot: PlanTaskDetail;
    current: boolean;
    files: boolean;
    purpose: "processing" | "delivery" | "reprocess";
    inputStage: AcquisitionStage;
  }>();
  const [editor, setEditor] = useState<{
    stage: "plan" | "delivery" | "keepPrepared";
    snapshot: PlanTaskDetail;
  }>();
  const replanSnapshot = editor?.snapshot;
  const replanOpen = !!editor || !!continuation;
  const formPanel = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (editor?.stage === "plan") formPanel.current?.scrollIntoView?.({ block: "nearest", behavior: "smooth" });
  }, [editor]);
  const [preparationOpen, setPreparationOpen] = useState(false);
  const requestId = useRef(0);
  const mutationPending = useRef(false);
  const load = useCallback(async () => {
    if (mutationPending.current) return;
    const request = ++requestId.current;
    try {
      const result = await planTaskApi.get(taskId);
      if (request !== requestId.current) return;
      setDetail(result);
      setFailed(false);
    } catch {
      if (request === requestId.current) setFailed(true);
    }
  }, [taskId]);
  useEffect(() => {
    void load();
    const timer = setInterval(() => {
      if (!document.hidden) void load();
    }, 5000);
    return () => {
      requestId.current++;
      clearInterval(timer);
    };
  }, [load]);
  useEffect(() => {
    setQuery(detail?.query ?? "");
  }, [detail?.query]);
  const mutate = async (fn: () => Promise<PlanTaskDetail | undefined>) => {
    if (mutationPending.current || detail?.task.isDeleted || detail?.task.deletedAt) return;
    mutationPending.current = true;
    requestId.current++;
    setBusy(true);
    setActionFailed(false);
    try {
      const next = await fn();
      if (next) setDetail(next);
      else {
        mutationPending.current = false;
        await load();
      }
      onChanged?.();
    } catch {
      setActionFailed(true);
      mutationPending.current = false;
      await load();
    } finally {
      mutationPending.current = false;
      setBusy(false);
      setDirectoryRevision((value) => value + 1);
    }
  };
  const task = detail?.task;
  const sourceRevisionAvailable = useSourceRevisionAvailability(task);
  const runtime = useRuntimeOverviews(task?.workflowRunId ? [task] : []);
  const overview =
    task?.workflowRunId && runtime.overviews[taskId]?.workflowRunId === task.workflowRunId
      ? runtime.overviews[taskId]
      : undefined;
  const continuationMessage = downloadContinuationLabel(t, overview);
  const processingWaitExplanation = currentProcessingWaitExplanation(detail, overview);
  const downloadActivity = task && acquisitionDownloadActivity(task, overview);
  const activeDownloadOnly =
    downloadActivity &&
    !hasCurrentRuntimeProblem(overview) &&
    !(overview?.attentionTags ?? task?.attentionTags)?.length;
  const corrections = useSourceCorrections(taskId, task);
  const readOnly = !!(task?.isDeleted || task?.deletedAt);
  const typedStorageReserveLow = (() => {
    if (!task?.waitPromptJson || task.waitPromptJson.length > 1024 * 1024) return false;
    try {
      const prompt = JSON.parse(task.waitPromptJson);
      return (
        prompt?.reason === "StorageUnavailable" &&
        prompt?.message === "LOCAL_STORAGE_RESERVE_LOW" &&
        detail?.plan?.parts.some((part) => part.id === prompt.targetId) === true
      );
    } catch {
      return false;
    }
  })();
  const currentRunMatches =
    !!task?.workflowRunId &&
    overview?.workflowRunId === task.workflowRunId &&
    overview.workflowStatus === "waiting";
  const storageReserveLow =
    currentRunMatches &&
    (typedStorageReserveLow ||
      overview?.problemReasonCode === "LOCAL_STORAGE_RESERVE_LOW" ||
      overview?.problemCode === "LOCAL_STORAGE_RESERVE_LOW");
  const canOverrideStorage =
    !readOnly &&
    !!task?.workflowRunId &&
    task.status === AcquisitionStatus.Waiting &&
    storageReserveLow;
  const publicationStoragePrompt = (() => {
    if (!task?.waitPromptJson || task.waitPromptJson.length > 1024 * 1024) return undefined;
    try {
      const prompt = JSON.parse(task.waitPromptJson);
      return prompt?.reason === "StorageUnavailable" &&
        detail?.plan?.parts.some((part) => part.id === prompt.targetId) === true
        ? { message: typeof prompt.message === "string" ? prompt.message : undefined }
        : undefined;
    } catch {
      return undefined;
    }
  })();
  const canDiagnoseRecovery = !readOnly && !!task?.workflowRunId &&
    overview?.canDiagnoseDownloadContinuation === true && overview.currentNodeKind !== "fs.publishArtifacts";
  const diagnoseContinuation = () =>
    createPortal(AcquisitionRecoveryModal, {
      taskIds: [taskId],
      ...(canOverrideStorage && currentStage !== "download" ? { reserveRunId: task!.workflowRunId! } : {}),
      initialScope: overview?.downloadsCompletedAwaitingWorkflow ? "localFiles" : "workflow",
      onChanged: () => {
        void load();
        onChanged?.();
      },
    });
  const completedDownloadWait =
    continuationMessage &&
    !hasCurrentRuntimeProblem(overview) &&
    overview?.workflowRunId != null &&
    overview.currentStepIndex != null
      ? {
          runId: overview.workflowRunId,
          stepIndex: overview.currentStepIndex,
          message: continuationMessage,
          onDiagnose:
            !readOnly && overview.canDiagnoseDownloadContinuation
              ? diagnoseContinuation
              : undefined,
        }
      : undefined;
  const canRevisePathConflict =
    task?.status === AcquisitionStatus.Waiting &&
    overview?.problemCode === "ProcessingPathConflict" &&
    overview.currentNodeKind === "acquisition.plan.process";
  const waitingPathConflict =
    canRevisePathConflict && task?.workflowRunId != null
      ? { taskId: task.id, runId: task.workflowRunId, planRevision: task.planRevision }
      : undefined;
  const localProcessingReasons = [
    "ProcessingPathConflict",
    "ExtractionPlanUnknown",
    "PasswordUnknown",
    "ArchiveInputRequired",
    "ProcessingInputNotMatched",
    "RepeatedContent",
    "ExtractionLayoutConflict",
    "FILE_DELIVERY_RESULT_UNCERTAIN",
    "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED",
  ];
  // The runtime projection intentionally keeps a generic SOURCE_ATTENTION_REQUIRED for
  // file-review waits. The task's typed wait prompt is the authoritative fallback, so a
  // valid assessment artifact remains actionable even when the projection has no reason code.
  const localProcessingPrompt = (() => {
    if (!task?.waitPromptJson || task.waitPromptJson.length > 1024 * 1024) return undefined;
    try {
      const prompt = JSON.parse(task.waitPromptJson);
      return typeof prompt?.reason === "string" && localProcessingReasons.includes(prompt.reason)
        ? prompt
        : undefined;
    } catch {
      return undefined;
    }
  })();
  const projectedLocalProcessingProblem = [overview?.problemReasonCode, overview?.problemCode].find(
    (code) => !!code && localProcessingReasons.includes(code),
  );
  const localProcessingProblem = projectedLocalProcessingProblem ?? localProcessingPrompt?.reason;
  const hasCurrentDirectoryInputs = !!detail && hasReadableCurrentDirectoryInputs(detail);
  const isProcessingStep = detail?.steps[overview?.currentStepIndex ?? -1]?.stage === "processing" ||
    ["acquisition.plan.process", "fs.assessDelivery"].includes(overview?.currentNodeKind ?? "");
  const canReviseLocalProcessing =
    task?.status === AcquisitionStatus.Waiting &&
    overview?.workflowStatus === "waiting" &&
    (isProcessingStep || hasCurrentDirectoryInputs && overview.currentNodeKind === "fs.publishArtifacts" &&
      !(overview.activeDownloadCount > 0) && !["running", "queued"].includes(overview.downloadActivity ?? "")) &&
    (hasCurrentDirectoryInputs || localProcessingReasons.includes(localProcessingProblem ?? "")) &&
    task.workflowRunId != null;
  const waitingLocalProcessing =
    canReviseLocalProcessing && task?.workflowRunId != null
      ? { taskId: task.id, runId: task.workflowRunId, planRevision: task.planRevision }
      : undefined;
  const publicationOverviewCodes = [overview?.problemReasonCode, overview?.problemCode];
  const publicationProblem =
    publicationOverviewCodes.find((code) => !!code && code !== "SOURCE_ATTENTION_REQUIRED") ??
    // Older projections retain only generic attention; use the current typed prompt,
    // never a storage code mentioned inside arbitrary error text.
    publicationStoragePrompt?.message;
  const canRevisePublicationStorage =
    task?.status === AcquisitionStatus.Waiting &&
    overview?.currentNodeKind === "fs.publishArtifacts" &&
    overview.workflowStatus === "waiting" &&
    publicationStoragePrompt &&
    [
      "LOCAL_STORAGE_FULL",
      "LOCAL_STORAGE_WRITE_FAILED",
      "LOCAL_STORAGE_ACCESS_DENIED",
      "LOCAL_STORAGE_UNAVAILABLE",
      "LOCAL_STORAGE_RESERVE_LOW",
    ].includes(publicationProblem ?? "") &&
    !!detail &&
    hasPublicationStorageInputs(detail);
  const waitingPublicationStorage =
    canRevisePublicationStorage && task?.workflowRunId != null
      ? { taskId: task.id, runId: task.workflowRunId, planRevision: task.planRevision }
      : undefined;
  const canReviseWaiting =
    canRevisePathConflict || canReviseLocalProcessing || canRevisePublicationStorage;
  const editable =
    task && !readOnly && !task.workflowRunId && task.status !== AcquisitionStatus.Cancelled;
  const showSelection = editable && ["selection", "attention"].includes(task.phase ?? "");
  const editorStale =
    !!replanSnapshot &&
    (task?.planRevision !== replanSnapshot.task.planRevision ||
      task?.workflowRunId !== replanSnapshot.task.workflowRunId ||
      task?.status !== replanSnapshot.task.status ||
      task?.waitPromptJson !== replanSnapshot.task.waitPromptJson);
  const closeEditor = () => setEditor(undefined);
  const startRevision = (next: PlanTaskDetail, options?: AttemptRunOptions) => {
    closeEditor();
    setContinuation(undefined);
    setDetail(next);
    setAttemptRunOverride(options ? { revision: next.task.planRevision, options } : undefined);
    // If start fails, the server keeps the saved revision ready for retry.
    void mutate(() => options
      ? planTaskApi.run(taskId, next.task.planRevision, options.useEmergencySpaceReserve)
      : planTaskApi.run(taskId, next.task.planRevision));
  };
  const currentAttempt = detail?.stageAttempts
    ?.slice()
    .reverse()
    .find((attempt) => attempt.runId === task?.workflowRunId);
  const currentStage =
    asAcquisitionStage(
      overview?.currentStepIndex != null
        ? detail?.steps[overview.currentStepIndex]?.stage
        : undefined,
    ) ?? asAcquisitionStage(currentAttempt?.stage);
  const stopped =
    !!task?.workflowRunId &&
    !readOnly &&
    [AcquisitionStatus.Failed, AcquisitionStatus.Cancelled].includes(task.status);
  const processingOffered =
    !!task?.workflowRunId &&
    !readOnly &&
    (canReviseWaiting || stopped || task.status === AcquisitionStatus.Completed);
  const processingAvailable =
    !!detail &&
    (task?.status === AcquisitionStatus.Completed
      ? hasCompletedProcessingInputs(detail)
      : hasStoppedProcessingInputs(
          detail,
          waitingPathConflict,
          waitingPublicationStorage,
          waitingLocalProcessing,
        ));
  const recordedDeliveryInputs = detail?.plan?.parts;
  const canRetryRecordedDelivery =
    !!recordedDeliveryInputs?.length &&
    recordedDeliveryInputs.every((part) =>
      detail?.replanArtifacts?.some(
        (artifact) => artifact.partId === part.id && artifact.canRetryDelivery === true,
      ),
    );
  const deliveryAvailable =
    !!detail && !readOnly && (hasStageDirectoryInputs(detail, "delivery") || canReviseDeliveryLayout(detail) || canRetryRecordedDelivery);
  const refreshAfterAction = () => {
    void load();
    void loadHealth();
    onChanged?.();
  };
  const resolveWaiting =
    !!task?.workflowRunId &&
    !readOnly &&
    !completedDownloadWait &&
    !activeDownloadOnly &&
    task.status === AcquisitionStatus.Waiting;
  const continueCurrent = resolveWaiting && !canDiagnoseRecovery;
  const continueFiles = processingOffered && processingAvailable;
  const retryStageCommit = stopped && overview?.workflowRunId === task?.workflowRunId &&
    overview?.currentNodeKind === "acquisition.plan.process" &&
    (overview.problemReasonCode === "STAGE_COMMIT_REFERENCED" || task?.error === "STAGE_COMMIT_REFERENCED");
  const savedProcessingCheckpoint = hasSavedStorageProcessingCheckpoint(detail, overview);
  const canAnswerProcessingPassword = currentRunMatches && localProcessingPrompt?.reason === "PasswordUnknown" &&
    (task?.currentStepIndex == null || overview.currentStepIndex == null || task.currentStepIndex === overview.currentStepIndex) &&
    ["acquisition.plan.process", "fs.assessDelivery"].includes(overview.currentNodeKind ?? "") &&
    detail?.plan?.parts.some(part => part.id === localProcessingPrompt.targetId) === true;
  const canReviewExistingOutput = overview?.currentNodeKind === "fs.assessDelivery" &&
    localProcessingPrompt?.reason === "FILE_DELIVERY_RESULT_UNCERTAIN" &&
    localProcessingPrompt.deliveryReview === true && !!localProcessingPrompt.artifactId &&
    !!localProcessingPrompt.fingerprint && !!localProcessingPrompt.reviewToken &&
    Array.isArray(localProcessingPrompt.allowedArchivePolicies) && localProcessingPrompt.allowedArchivePolicies.includes("keep");
  const latestProcessingAttempt = detail?.stageAttempts?.filter(
    (attempt) => attempt.stage === "processing" && attempt.runId === task?.workflowRunId
  ).pop();
  const deliveryWait = continueCurrent && overview?.currentNodeKind === "fs.publishArtifacts";
  const processAgain = task?.status === AcquisitionStatus.Completed ||
    (continueFiles && (deliveryWait || (!continueCurrent && latestProcessingAttempt?.status === "completed")));
  const openContinuation = (purpose: "processing" | "delivery" | "reprocess", inputStage: AcquisitionStage = "processing") => {
    if (!detail) return;
    setContinuation({
      snapshot: detail, purpose, inputStage,
      current: purpose !== "reprocess" && continueCurrent,
      files: purpose !== "delivery" && (hasStageDirectoryInputs(detail, inputStage) || continueFiles),
    });
  };
  const processingAction = (inputStage: AcquisitionStage = "processing") => detail && (
    <Button size="sm" color={processAgain ? "default" : "primary"} variant="flat"
      aria-haspopup="dialog"
      isDisabled={busy || replanOpen || (!retryStageCommit && !continueFiles && (!continueCurrent || deliveryWait))}
      onPress={() => retryStageCommit ? diagnoseContinuation() : openContinuation(processAgain ? "reprocess" : "processing", inputStage)}>
      {t(processAgain ? "acquisition.continue.again" : "acquisition.continue.open")}
    </Button>
  );
  const continuationStale = continuation != null && (
    readOnly ||
    task?.workflowRunId !== continuation.snapshot.task.workflowRunId ||
    task?.planRevision !== continuation.snapshot.task.planRevision ||
    task?.status !== continuation.snapshot.task.status ||
    task?.waitPromptJson !== continuation.snapshot.task.waitPromptJson ||
    !((continuation.current && continueCurrent) || (continuation.files && (continueFiles ||
      !!detail && hasStageDirectoryInputs(detail, continuation.inputStage))))
  );
  const currentControls = (
    <>
      {continueCurrent && (deliveryWait ? (
        <Button size="sm" color="primary" variant="flat" aria-haspopup="dialog"
          isDisabled={busy || replanOpen} onPress={() => openContinuation("delivery")}>
          {t("acquisition.continue.delivery")}
        </Button>
      ) : processingAction(currentStage ?? "processing"))}
      {!!task?.workflowRunId && !continueCurrent && !(continueFiles && currentStage === "processing") && (
        <Button
          size="sm"
          color={canDiagnoseRecovery ? "secondary" : "default"}
          variant="flat"
          onPress={() =>
            canDiagnoseRecovery
              ? diagnoseContinuation()
              : readOnly
                ? showRun(task.workflowRunId!)
                : setCurrentRunView(task.workflowRunId!)
          }
        >
          {t(
            canDiagnoseRecovery
              ? "acquisition.runtime.continueWorkflow"
              : "acquisition.plan.execution",
          )}
        </Button>
      )}
      {stopped && !canDiagnoseRecovery && !continueFiles && (
        <Button
          size="sm"
          variant="flat"
          isDisabled={busy || replanOpen}
          onPress={diagnoseContinuation}
        >
          {t("acquisition.continue.open")}
        </Button>
      )}
    </>
  );
  const sourceChange = !readOnly && !downloadActivity && sourceRevisionAvailable && (
    <Button
      size="sm"
      variant="flat"
      isDisabled={busy || replanOpen}
      onPress={() => createPortal(SourceRevisionModal, { taskId, onChanged: refreshAfterAction })}
    >
      {t("acquisition.sourceRevision.title")}
    </Button>
  );
  const reviewSources = () =>
    createPortal(SourceAvailabilityReviewModal, {
      taskIds: [taskId],
      onChecked: () => void loadHealth(),
      onChanged: refreshAfterAction,
    });
  const reviewAction = !readOnly && !!task?.workflowRunId && !!detail?.plan?.parts.length && (
    <Button size="sm" variant="light" onPress={reviewSources}>
      {t("sourceAvailability.review.one")}
    </Button>
  );
  const stageActions: Partial<Record<AcquisitionStage, ReactNode>> = {};
  const stageHeaderActions: Partial<Record<AcquisitionStage, ReactNode>> = {};
  for (const stage of ["download", "processing", "delivery"] as const) {
    stageHeaderActions[stage] = <>
      {currentStage === stage && currentControls}
      {stage === "processing" && detail && processingOffered && (!continueCurrent || deliveryWait) && processingAction("processing")}
      {stage === "delivery" && detail && canKeepPreparedFiles(detail) && (
        <Button size="sm" color="primary" variant="flat" aria-haspopup="dialog" isDisabled={busy || replanOpen}
          onPress={() => setEditor({ stage: "keepPrepared", snapshot: detail })}>
          {t("acquisition.keepPrepared.title")}
        </Button>
      )}
      {stage === "delivery" && deliveryAvailable && (
        <Button size="sm" variant="flat" isDisabled={busy || replanOpen}
          onPress={() => setEditor({ stage: "delivery", snapshot: detail! })}>
          {t("acquisition.timeline.retry.delivery")}
        </Button>
      )}
    </>;
    stageActions[stage] = (
      <div className="flex flex-wrap items-center gap-2">
        {currentStage === stage && overview && (
          <div className="w-full">
            <RuntimeOverviewSummary overview={overview} showSources={false} problemDetail={processingWaitExplanation} />
          </div>
        )}
        {stage === "download" && (
          <>
            {overview?.sources?.length ? <RuntimeSources overview={overview} /> : detail?.plan?.parts.some(p => p.sources?.length) ? (
              <Popover trigger={<Button size="sm" variant="light">{t("acquisition.plan.recordedSources")}</Button>}>
                <div className="max-h-[60vh] w-[min(32rem,85vw)] overflow-auto p-2">
                  <TaskPlanSources detail={detail} corrections={corrections.data} sourceHealth={sourceHealth} sourcesOnly />
                </div>
              </Popover>
            ) : null}
            {sourceChange}
            {reviewAction}
          </>
        )}
        {stage === "processing" && detail && processingOffered && (
          <>
            {stopped && !!detail.replanArtifacts?.length && (
              <Button
                size="sm"
                variant="light"
                isDisabled={busy || replanOpen}
                onPress={() => setEditor({ stage: "plan", snapshot: detail })}
              >
                {t("acquisition.plan.replan.title")}
              </Button>
            )}
          </>
        )}
      </div>
    );
  }
  const stageLabel = task ? acquisitionStageLabel(t, task, overview) : undefined;
  const taskStage = task && acquisitionTaskStage(task, overview);
  const title = acquisitionTaskTitle(taskId, task);
  const hasLinkedPost = continuation?.snapshot.task.creationOrigins?.some(origin => postTaskId(origin) != null) === true;
  const originLinks = !!task?.creationOrigins?.length && (
    <AcquisitionCreationOrigin origins={task.creationOrigins} compact inspectPosts />
  );
  return (
    <>
      <Modal
        visible={visible}
        onClose={() => setVisible(false)}
        size="6xl"
        classNames={{ header: "min-w-0 pr-10 [overflow-wrap:anywhere]" }}
        aria-label={title}
        title={
          <span className="block min-w-0 [overflow-wrap:anywhere]">
            {title}{" "}
            {stageLabel && (
              <Chip className="align-middle whitespace-nowrap" size="sm" variant="flat" color={
                taskStage === "completed" ? "success" : task?.status === AcquisitionStatus.Failed ? "danger" :
                taskStage === "attention" ? "warning" : taskStage === "running" ? "primary" : "default"
              }>
                {stageLabel}
              </Chip>
            )}
          </span>
        }
        footer={
          <div className="flex w-full flex-wrap items-center justify-end gap-2">
            {detail && (
              <Button
                size="sm"
                className="mr-auto"
                variant="flat"
                startContent={<HistoryOutlined aria-hidden />}
                aria-haspopup="dialog"
                onPress={() => setDiagnostics({ tab: "plan" })}
              >
                {t("acquisition.plan.diagnostics.open")}
              </Button>
            )}
            {task && !readOnly && (
              <Button
                color="danger"
                variant="light"
                isDisabled={busy}
                onPress={() => {
                  setVisible(false);
                  createPortal(DeleteAcquisitionTasksModal, { taskIds: [taskId], onChanged });
                }}
              >
                {t("acquisition.delete.single")}
              </Button>
            )}
            <Button variant="light" onPress={() => setVisible(false)}>
              {t("acquisition.plan.close")}
            </Button>
            {editable && (task.phase === "ready" ||
              (task.phase === "attention" && detail?.plan && detail.steps.length > 0)) && (
              <Button
                variant="flat"
                isLoading={busy}
                onPress={() => void mutate(() => planTaskApi.preflight(taskId))}
              >
                {t("acquisition.plan.preflight")}
              </Button>
            )}
            {editable && detail && task.phase === "ready" && (
              <Button
                color="primary"
                isLoading={busy}
                isDisabled={
                  detail.issues.some((issue) => issue.blocking !== false) || !detail.steps.length
                }
                onPress={() => void mutate(() => attemptRunOverride?.revision === task.planRevision
                  ? planTaskApi.run(taskId, task.planRevision, attemptRunOverride.options.useEmergencySpaceReserve)
                  : planTaskApi.run(taskId, task.planRevision))}
              >
                {t("acquisition.plan.run")}
              </Button>
            )}
          </div>
        }
        onDestroyed={onDestroyed}
      >
        {failed && (
          <div role="alert" className="flex flex-wrap items-center gap-2 text-danger">
            <span>{t("acquisition.plan.loadFailed")}</span>
            <Button size="sm" variant="light" onPress={() => void load()}>
              {t("common.action.retry")}
            </Button>
          </div>
        )}
        {!detail ? (
          !failed && <Spinner aria-label={t("acquisition.plan.loading")} />
        ) : (
          <div className="space-y-4">
            {readOnly && (
              <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-default-100 p-3">
                <p role="status" className="text-sm text-default-600">{t("acquisition.delete.readOnly")}</p>
                {task?.status === AcquisitionStatus.Completed && task.workflowRunId != null && (
                  <Button size="sm" variant="flat" onPress={() => createPortal(DeleteAcquisitionTasksModal, {
                    taskIds: [taskId], retryRetainedFiles: true, onChanged: refreshAfterAction,
                  })}>
                    {t("acquisition.delete.retained.title")}
                  </Button>
                )}
              </div>
            )}
            <div className="flex min-w-0 flex-wrap items-center justify-between gap-2">
              <div className="min-w-0">{originLinks}</div>
              {!readOnly && <Button size="sm" variant="light" startContent={<LuFolderCog aria-hidden size={16} />}
                isDisabled={busy || replanOpen} onPress={() => setDirectoriesOpen(true)}>
                {t("acquisition.directories.open")}
              </Button>}
            </div>
            {!currentStage && <div className="flex flex-wrap gap-2">{currentControls}</div>}
            {task && (
              <AcquisitionExecutionTimeline
                detail={detail}
                actions={stageActions}
                headerActions={stageHeaderActions}
                currentProblemCode={overview?.problemReasonCode ?? overview?.problemCode}
                currentStepIndex={overview?.currentStepIndex}
                onOpenRun={showRun}
              />
            )}
            {overview && !currentStage && <RuntimeOverviewSummary overview={overview} showSources={false} problemDetail={processingWaitExplanation} />}
            {runtime.failed && (
              <p role="status" className="text-xs text-warning-600">
                {t("acquisition.runtime.refreshFailed")}
              </p>
            )}
            {task && <AcquisitionTiming task={task} />}
            {!readOnly && editor?.stage === "plan" && replanSnapshot && (
              <div ref={formPanel}>
                {editorStale && (
                  <div role="alert" className="flex items-center gap-2 text-sm text-warning-700">
                    {t("acquisition.plan.replan.error.PLAN_VERSION_CONFLICT")}
                    <Button size="sm" variant="flat" onPress={closeEditor}>
                      {t("acquisition.plan.close")}
                    </Button>
                  </div>
                )}
                <fieldset disabled={editorStale} className="min-w-0">
                    <PlanReplanForm
                      detail={replanSnapshot}
                      onCancel={closeEditor}
                      onSaved={(next, options) => {
                        closeEditor();
                        setAttemptRunOverride(options ? { revision: next.task.planRevision, options } : undefined);
                        void mutate(async () => next);
                      }}
                    />
                </fieldset>
              </div>
            )}
            {actionFailed && (
              <p role="alert" className="text-sm text-danger">
                {t("acquisition.plan.actionFailed")}
              </p>
            )}
            {!!detail.issues.length && (
              <div
                role="status"
                className="space-y-2 rounded-lg border border-warning-200 bg-warning-50 p-3"
              >
                {detail.issues.map((issue) => (
                  <div key={issue.code} className="flex min-w-0 flex-wrap items-center gap-1">
                    <p className="text-sm">
                      {t(`acquisition.plan.issue.${issue.code}`, {
                        defaultValue: t(`acquire.requirement.${issue.code}`, {
                          defaultValue: t("acquisition.plan.issue.unknown"),
                        }),
                      })}
                    </p>
                    <Tooltip placement="bottom" content={<span className="[overflow-wrap:anywhere]">{issue.code}</span>}>
                      <Button isIconOnly size="sm" variant="light" className="h-6 min-w-6 w-6 text-default-500"
                        aria-label={t("workflow.processing.problem.details")}><LuInfo aria-hidden size={14} /></Button>
                    </Tooltip>
                  </div>
                ))}
              </div>
            )}
            {!readOnly && !!task?.workflowRunId && (
              <SourceCorrectionPanel
                key={`${taskId}:${task.workflowRunId}:${task.planRevision}`}
                taskId={taskId}
                preview={corrections.data}
                loading={corrections.loading}
                failed={corrections.failed}
                disabled={busy || replanOpen}
                readOnly={readOnly}
                onRefresh={corrections.refresh}
                onBusyChange={(value) => {
                  mutationPending.current = value;
                  if (value) requestId.current++;
                  setBusy(value);
                }}
                onApplied={() => {
                  void load();
                  void loadHealth();
                  onChanged?.();
                }}
              />
            )}
            {editable && (
              <>
                {!detail.plan && task?.builderId !== "draft" && (
                  <Button
                    size="sm"
                    variant="flat"
                    isDisabled={busy}
                    onPress={() => setPreparationOpen((value) => !value)}
                  >
                    {t("acquisition.draft.changeBuilder")}
                  </Button>
                )}
                {!detail.plan && (task?.builderId === "draft" || preparationOpen) && (
                  <DraftPreparation detail={detail} busy={busy} onAction={mutate} />
                )}
                {task?.builderId !== "draft" && !preparationOpen && detail.query != null && (
                  <section
                    className="space-y-3 rounded-lg border border-default-200 p-3"
                    aria-label={t("acquisition.plan.search")}
                  >
                    <p className="text-sm text-default-500">{t("acquisition.plan.searchHint")}</p>
                    <div className="flex flex-wrap items-end gap-2">
                      <Input
                        className="min-w-52 flex-1"
                        label={t("acquisition.plan.query")}
                        value={query}
                        onValueChange={setQuery}
                        maxLength={512}
                      />
                      <Button
                        isLoading={busy}
                        isDisabled={!query.trim()}
                        onPress={() => void mutate(() => planTaskApi.search(taskId, query.trim()))}
                      >
                        {t("acquisition.plan.search")}
                      </Button>
                    </div>
                    {showSelection &&
                      (detail.candidates.length ? (
                        <div className="space-y-2">
                          <p className="text-xs text-default-500">
                            {t("acquisition.plan.chooseHint", {
                              count: detail.candidates.length,
                            })}
                          </p>
                          {detail.candidates.map((candidate) => (
                            <div
                              key={candidate.id}
                              className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-default-50 p-3"
                            >
                              <div className="min-w-0 flex-1">
                                <p className="break-words text-sm font-medium">{candidate.title}</p>
                                <ExternalLink href={candidate.url} className="text-xs">
                                  {t("acquisition.plan.candidateSource")}
                                </ExternalLink>
                              </div>
                              <Button
                                size="sm"
                                variant="flat"
                                color="primary"
                                isDisabled={busy}
                                onPress={() =>
                                  void mutate(() =>
                                    planTaskApi.select(taskId, candidate.id, task!.planRevision),
                                  )
                                }
                              >
                                {t("acquisition.plan.choose")}
                              </Button>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <p className="text-sm text-default-500">
                          {t("acquisition.plan.noCandidates")}
                        </p>
                      ))}
                  </section>
                )}

                <TaskPlanSources
                  detail={detail}
                  corrections={corrections.data}
                  sourceHealth={sourceHealth}
                  onReview={reviewSources}
                  busy={busy}
                  onApply={
                    task.phase === "ready"
                      ? (partId, source, revision) =>
                          mutate(() =>
                            planTaskApi.applyProcessingRevision(
                              taskId,
                              task.planRevision,
                              partId,
                              source,
                              revision,
                            ),
                          )
                      : undefined
                  }
                />
                <section className="space-y-2 rounded-lg border border-default-200 p-3">
                  <h3 className="text-sm font-medium">{t("acquisition.plan.outputDirectory")}</h3>
                  <p className="text-xs text-default-500">{t("acquisition.plan.outputHint")}</p>
                  <FileSystemSelectorButton
                    key={`${task?.targetDirectory ?? "empty"}:${directoryRevision}`}
                    isDisabled={busy}
                    className="h-auto max-w-full whitespace-normal break-all py-2 text-left"
                    fileSystemSelectorProps={{
                      targetType: "folder",
                      defaultSelectedPath: task?.targetDirectory ?? undefined,
                      onSelected: (entry) =>
                        void mutate(() =>
                          task?.builderId === "draft"
                            ? planTaskApi.updateDraft(taskId, {
                                expectedRevision: task.planRevision,
                                outputDirectory: entry.path,
                              })
                            : planTaskApi.update(taskId, task!.planRevision, entry.path),
                        ),
                    }}
                  />
                </section>
                {detail.canEditStoragePolicy && (
                  <TaskStoragePolicySettings
                    detail={detail}
                    busy={busy}
                    onSave={async (revision, policy, overrides) => {
                      let saved = false;
                      await mutate(async () => {
                        const next = await planTaskApi.updateStorage(
                          taskId,
                          revision,
                          policy,
                          overrides.map((item) => ({ ...item, policy: item.policy ?? undefined })),
                        );
                        saved = true;
                        return next;
                      });
                      return saved;
                    }}
                  />
                )}
                {detail.canEditDeliverySettings && (
                  <TaskDeliverySettings
                    key={`${taskId}:${task?.planRevision}`}
                    detail={detail}
                    busy={busy}
                    editable
                    onSave={(deliverySettings) =>
                      mutate(() =>
                        task?.builderId === "draft"
                          ? planTaskApi.updateDraft(taskId, {
                              expectedRevision: task.planRevision,
                              deliverySettings,
                            })
                          : planTaskApi.updateDelivery(
                              taskId,
                              task!.planRevision,
                              deliverySettings,
                            ),
                      )
                    }
                  />
                )}
              </>
            )}
          </div>
        )}
      </Modal>
      {!readOnly && editor?.stage === "keepPrepared" && replanSnapshot && (
        <KeepPreparedFilesModal detail={replanSnapshot} stale={editorStale || !canKeepPreparedFiles(detail)}
          onClose={closeEditor} onSaved={startRevision} onRefresh={() => void load()} />
      )}
      {!readOnly && editor?.stage === "delivery" && replanSnapshot && (
        <Modal visible size="5xl" scrollBehavior="inside" title={t("acquisition.timeline.retry.delivery")}
          footer={false} onClose={closeEditor}>
          {editorStale && <p role="alert" className="text-sm text-warning-700">{t("acquisition.plan.replan.error.PLAN_VERSION_CONFLICT")}</p>}
          <fieldset disabled={editorStale} className="min-w-0">
              <CompletedProcessingRevisionForm
                detail={replanSnapshot} currentDeliveryRoot={acquisitionOptions && (acquisitionOptions.deliveryRootDirectory ?? null)}
                retryStage="delivery" inputStage="delivery" mode={replanSnapshot.task.status === AcquisitionStatus.Completed ? "completed" : "stopped"} inDialog
                waitingPathConflict={waitingPathConflict} waitingLocalProcessing={waitingLocalProcessing}
                waitingPublicationStorage={waitingPublicationStorage} onCancel={closeEditor} onSaved={startRevision} />
          </fieldset>
        </Modal>
      )}
      {diagnostics && detail && (
        <TaskDiagnosticsModal
          taskId={taskId}
          detail={detail}
          page={diagnostics}
          onPageChange={setDiagnostics}
          onClose={() => setDiagnostics(undefined)}
          completedDownloadWait={completedDownloadWait}
        >
          <TaskPlanSnapshot detail={detail} acquisitionOptions={acquisitionOptions} onRun={showRun}>
            <AcquisitionCreationOrigin
              origins={task?.creationOrigins}
              inspectPosts
            />
            {task?.originResourceId != null && (
              <p className="text-xs text-default-500">
                {t("acquisition.draft.originResource", { id: task.originResourceId })}
              </p>
            )}
            <TaskPlanSources
              detail={detail}
              corrections={corrections.data}
              sourceHealth={sourceHealth}
            />
          </TaskPlanSnapshot>
        </TaskDiagnosticsModal>
      )}
      {continuation && (
        <TaskContinuationModal
          taskTitle={acquisitionTaskTitle(taskId, continuation.snapshot.task)}
          origin={<div className="flex min-w-0 flex-wrap items-center justify-between gap-2">
            <div className="min-w-0">{originLinks}</div>
            <Button size="sm" variant="light" startContent={<LuFolderCog aria-hidden size={16} />}
              isDisabled={reserveSubmitting} onPress={() => setDirectoriesOpen(true)}>{t("acquisition.directories.open")}</Button>
          </div>}
          busy={reserveSubmitting}
          processAgain={continuation.purpose === "reprocess"}
          delivery={continuation.purpose === "delivery"}
          reviewOutput={canReviewExistingOutput}
          passwordRequested={canAnswerProcessingPassword}
          allowModeChoice={continuation.purpose === "processing" && continuation.current && continuation.files &&
            (canAnswerProcessingPassword || canReviewExistingOutput || (!hasStageDirectoryInputs(continuation.snapshot, continuation.inputStage) &&
              savedProcessingCheckpoint && hasSavedStorageProcessingCheckpoint(continuation.snapshot, overview)))}
          preferFiles={!canAnswerProcessingPassword && continuation.purpose !== "delivery" && continuation.files &&
            (hasStageDirectoryInputs(continuation.snapshot, continuation.inputStage) ||
              hasReadableCurrentDirectoryInputs(continuation.snapshot) && !savedProcessingCheckpoint)}
          current={continuation.current && (continuationStale || continueCurrent) && (
            <>
            <WorkflowRunDetailContent
              runId={continuation.snapshot.task.workflowRunId!}
              responseOnly
              respondInitially={!continuationStale}
              readOnly={continuationStale}
              wrapWaitingResponse={continuationStale ? undefined : (run, form) => {
                if (run.id !== continuation.snapshot.task.workflowRunId ||
                    (overview?.currentStepIndex != null && run.currentStepIndex !== overview.currentStepIndex))
                  return form;
                const taskForm = hasLinkedPost && run.workflowDefinitionId === 0
                  ? <ResumePostSourceContext.Provider value>{form}</ResumePostSourceContext.Provider>
                  : form;
                const downloadStage = asAcquisitionStage(
                  continuation.snapshot.steps[run.currentStepIndex ?? -1]?.stage,
                ) === "download";
                if (!canOverrideStorage && !downloadStage) return taskForm;
                let destination: string | undefined;
                if (deliveryWait && run.waitPromptJson) {
                  try {
                    const { targetDirectory } = JSON.parse(run.waitPromptJson);
                    if (typeof targetDirectory === "string") destination = targetDirectory;
                  } catch { /* Historical waits may not record a directory. */ }
                }
                return (
                  <StorageReserveOption key={JSON.stringify([taskId, run.id, run.currentStepIndex, run.waitPromptJson])}
                    taskId={taskId} workflowRunId={run.id!} destination={destination}
                    scope={downloadStage ? "download" : undefined}
                    actionLabel={t(deliveryWait ? "cloudDownload.publication.retry" : "acquisition.continue.current")}
                    onBusyChange={setReserveSubmitting} onChanged={refreshAfterAction}>
                    {taskForm}
                  </StorageReserveOption>
                );
              }}
              completedDownloadWait={completedDownloadWait}
              onChanged={() => {
                setContinuation(undefined);
                refreshAfterAction();
              }}
            />
            {!continuationStale && !continueFiles && currentRunMatches &&
              overview?.currentNodeKind === "fs.assessDelivery" &&
              localProcessingPrompt?.deliveryReview === true && !!localProcessingPrompt.artifactId &&
              !localProcessingPrompt.adaptiveAssessment && localProcessingPrompt.reason !== "PasswordUnknown" && (
                <Button size="sm" variant="flat" onPress={() => {
                  setContinuation(undefined);
                  createPortal(AcquisitionAdaptiveProcessingModal, { taskIds: [taskId], onChanged: refreshAfterAction });
                }}>{t("acquisition.adaptive.batchEntry")}</Button>
              )}
            </>
          )}
          files={continuation.files && (
            <>
            {!continuationStale && processingWaitExplanation && <ProcessingProblemSummary
              message={processingWaitExplanation}
              reason={localProcessingProblem ?? overview?.problemReasonCode ?? overview?.problemCode ?? undefined}
              summary={runtimeProblemLabel(t, localProcessingProblem ?? overview?.problemReasonCode ?? overview?.problemCode, overview?.problem)} />}
            <CompletedProcessingRevisionForm
              inDialog
              detail={continuation.snapshot}
              currentDeliveryRoot={acquisitionOptions && (acquisitionOptions.deliveryRootDirectory ?? null)}
              retryStage="processing"
              inputStage={continuation.inputStage}
              preferAi={savedProcessingCheckpoint}
              mode={continuation.snapshot.task.status === AcquisitionStatus.Completed ? "completed" : "stopped"}
              waitingPathConflict={waitingPathConflict}
              waitingLocalProcessing={waitingLocalProcessing}
              waitingPublicationStorage={waitingPublicationStorage}
              onCancel={() => setContinuation(undefined)}
              onSaved={startRevision}
            />
            </>
          )}
          stale={continuationStale}
          onClose={() => setContinuation(undefined)}
        />
      )}
      {directoriesOpen && <TaskDirectoriesModal taskId={taskId} onClose={() => setDirectoriesOpen(false)}
        onChanged={() => { setContinuation(undefined); closeEditor(); refreshAfterAction(); }} />}
      {currentRunView != null && (
        <Modal
          visible
          size="6xl"
          title={t("workflow.runDetail.title", { id: currentRunView })}
          onClose={() => setCurrentRunView(undefined)}
          footer={
            <Button variant="light" onPress={() => setCurrentRunView(undefined)}>
              {t("acquisition.plan.close")}
            </Button>
          }
        >
          <WorkflowRunDetailContent
            runId={currentRunView}
            readOnly={readOnly || !!activeDownloadOnly || currentRunView !== task?.workflowRunId}
            completedDownloadWait={completedDownloadWait}
            onChanged={refreshAfterAction}
          />
        </Modal>
      )}
    </>
  );
}

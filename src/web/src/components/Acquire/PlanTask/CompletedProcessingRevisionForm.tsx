import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, NumberInput, Select, Textarea } from "@/components/bakaui";
import ResumeActions from "@/components/Workflow/ResumeActions";
import ProcessingChoiceGroup from "@/components/Workflow/ProcessingChoiceGroup";
import { ProcessingDirectoryFiles, ProcessingFileList } from "@/components/Workflow/CurrentProcessingFiles";
import ProcessingPlanField, {
  isCompleteProcessingPlan,
} from "@/components/Workflow/ProcessingPlanField";
import type { PlanTaskDetail } from "./models";
import { planTaskApi } from "./api";
import CompletedDeliveryLayoutRevision, {
  canReviseDeliveryLayout,
} from "./CompletedDeliveryLayoutRevision";
import RevisionPlacementDialog from "./RevisionPlacementDialog";
import { AcquisitionStatus } from "@/sdk/constants";
import type { AcquisitionStage } from "./AcquisitionExecutionTimeline";
import useAttemptStorageReserve, { type AttemptRunOptions } from "./useAttemptStorageReserve";

type ProcessingRevisionMode = "completed" | "stopped";

export interface WaitingPathConflictRevision {
  taskId: number;
  runId: number;
  planRevision: number;
}

function reviewedProcessingInput(detail: PlanTaskDetail) {
  const prompt = detail.task.waitPromptJson;
  if (detail.task.status !== AcquisitionStatus.Waiting || !prompt || prompt.length > 1024 * 1024)
    return;
  try {
    const review = JSON.parse(prompt);
    if (
      review?.deliveryReview !== true ||
      !review.reviewToken ||
      !review.artifactId ||
      !review.fingerprint
    )
      return;
    const matching = detail.replanArtifacts.filter(
      (item) =>
        ["processedFiles", "publishedFiles", "currentFiles"].includes(item.kind) &&
        item.canReadCurrentDirectory === true &&
        item.partId === review.targetId &&
        item.artifactId === review.artifactId &&
        item.fingerprint === review.fingerprint,
    );
    if (matching.length === 1) return matching[0];
  } catch {
    /* Leave the selection to the user if the historical prompt cannot be read. */
  }
}

function inputKinds(mode: ProcessingRevisionMode): string[] {
  return mode === "stopped"
    ? ["download", "processedFiles", "publishedFiles", "compositeFiles", "currentFiles"]
    : ["processedFiles", "publishedFiles", "compositeFiles", "currentFiles"];
}

const processingChoices = ["keep", "manual", "ai", "defer"] as const;
type ProcessingChoice = (typeof processingChoices)[number];
const manualPlan = JSON.stringify({ requirement: "required", steps: [], evidence: [] });
type PartDraft = {
  artifactId: string;
  choice?: ProcessingChoice;
  instructions: string;
};
type ProcessingInput = PlanTaskDetail["replanArtifacts"][number];
const canReadCurrentDirectory = (item?: ProcessingInput) => item?.canReadCurrentDirectory === true;

function planForChoice(draft: PartDraft) {
  if (draft.choice === "manual") return JSON.parse(draft.instructions);
  return {
    requirement: draft.choice === "keep" ? "notRequired" : "unknown",
    steps: [],
    evidence: [],
  };
}
function completeInstructions(json: string) {
  return isCompleteProcessingPlan(json) && JSON.parse(json).requirement === "required";
}

function matchesWaitingRevision(
  detail: PlanTaskDetail,
  revision?: WaitingPathConflictRevision,
): boolean {
  return (
    detail.task.status === AcquisitionStatus.Waiting &&
    revision?.taskId === detail.task.id &&
    revision.runId === detail.task.workflowRunId &&
    revision.planRevision === detail.task.planRevision
  );
}

const isPublicationInput = (item: PlanTaskDetail["replanArtifacts"][number]) =>
  item.kind === "processedFiles" || (item.kind === "publishedFiles" && item.canRetryDelivery === true);

export function hasPublicationStorageInputs(detail: PlanTaskDetail): boolean {
  const parts = detail.plan?.parts ?? [];
  return (
    parts.length > 0 &&
    parts.every((part) =>
      (detail.replanArtifacts ?? []).some(
        (item) => item.partId === part.id && isPublicationInput(item),
      ),
    )
  );
}

function hasProcessingInputs(detail: PlanTaskDetail, mode: ProcessingRevisionMode): boolean {
  const parts = detail.plan?.parts ?? [];
  return (
    parts.length > 0 &&
    parts.every((part) =>
      (detail.replanArtifacts ?? []).some(
        (item) => item.partId === part.id && inputKinds(mode).includes(item.kind),
      ),
    )
  );
}

export function hasCompletedProcessingInputs(detail: PlanTaskDetail): boolean {
  return hasStageDirectoryInputs(detail, "processing") || hasProcessingInputs(detail, "completed");
}

export function hasStageDirectoryInputs(detail: PlanTaskDetail, stage: AcquisitionStage): boolean {
  const parts = detail.plan?.parts ?? [];
  return detail.stageDirectories?.taskId === detail.task.id && parts.length > 0 &&
    parts.every(part => !!detail.stageDirectories?.parts[part.id]?.[stage]?.directory);
}

function currentProcessingInputStage(
  detail: PlanTaskDetail,
  partId: string,
  requestedStage: AcquisitionStage,
): AcquisitionStage {
  if (requestedStage !== "processing" || detail.task.status === AcquisitionStatus.Completed ||
      detail.stageDirectories?.taskId !== detail.task.id) return requestedStage;
  const directories = detail.stageDirectories.parts[partId];
  // First processing starts from its completed download. Once a processing
  // directory is bound, never fall back to an older download if it becomes unavailable.
  if (directories?.processing || !directories?.download?.directory) return requestedStage;
  return detail.replanArtifacts.some(item => item.partId === partId && item.kind === "download" &&
    item.canReadCurrentDirectory === true && item.rootDirectory === directories.download!.directory)
    ? "download" : requestedStage;
}

/** Server-projected ownership/readability is authoritative, not a wait reason or old AI flag. */
export function hasReadableCurrentDirectoryInputs(detail: PlanTaskDetail): boolean {
  const parts = detail.plan?.parts ?? [];
  return hasStageDirectoryInputs(detail, "processing") || parts.length > 0 && parts.every(part => (detail.replanArtifacts ?? []).some(item =>
    item.partId === part.id && inputKinds("stopped").includes(item.kind) && canReadCurrentDirectory(item)));
}

export function hasStoppedProcessingInputs(
  detail: PlanTaskDetail,
  waitingPathConflict?: WaitingPathConflictRevision,
  waitingPublicationStorage?: WaitingPathConflictRevision,
  waitingLocalProcessing?: WaitingPathConflictRevision,
): boolean {
  const publicationWaiting = matchesWaitingRevision(detail, waitingPublicationStorage);
  const verifiedWaiting =
    matchesWaitingRevision(detail, waitingPathConflict) ||
    matchesWaitingRevision(detail, waitingLocalProcessing) ||
    publicationWaiting;
  return (
    !!detail.task.workflowRunId &&
    (verifiedWaiting ||
      [AcquisitionStatus.Failed, AcquisitionStatus.Cancelled].includes(detail.task.status)) &&
    (publicationWaiting
      ? hasPublicationStorageInputs(detail)
      : hasStageDirectoryInputs(detail, "processing") || hasProcessingInputs(detail, "stopped"))
  );
}

/** Continue verified local outputs without replaying a successful task's downloads. */
export default function CompletedProcessingRevisionForm({
  detail,
  currentDeliveryRoot,
  onSaved,
  onCancel,
  mode = "completed",
  inDialog = false,
  onBack,
  retryStage,
  waitingPathConflict,
  waitingLocalProcessing,
  waitingPublicationStorage,
  preferAi = false,
  inputStage,
}: {
  detail: PlanTaskDetail;
  currentDeliveryRoot?: string | null;
  onSaved: (next: PlanTaskDetail, options: AttemptRunOptions) => void;
  onCancel: () => void;
  mode?: ProcessingRevisionMode;
  inDialog?: boolean;
  onBack?: () => void;
  retryStage?: "processing" | "delivery";
  /** Explicitly chosen re-analysis from a saved checkpoint; delivery keeps its exact contract. */
  preferAi?: boolean;
  /** The clicked stage, resolved against current task bindings by the server, not old artifacts. */
  inputStage?: AcquisitionStage;
  /** Supplied only by the owning task after matching its current processing-conflict runtime. */
  waitingPathConflict?: WaitingPathConflictRevision;
  /** Supplied for a stopped processing or assessment node with a current artifact. */
  waitingLocalProcessing?: WaitingPathConflictRevision;
  /** Bound to a current publication storage wait with server-projected processed inputs. */
  waitingPublicationStorage?: WaitingPathConflictRevision;
}) {
  const { t } = useTranslation();
  const [snapshot] = useState(detail);
  const [container, setContainer] = useState<HTMLElement | null>(null);
  const popoverProps = {
    portalContainer: container?.closest<HTMLElement>('[role="dialog"]') ?? undefined,
  };
  const stopped = mode === "stopped";
  const deliveryOnly = retryStage === "delivery";
  const [publicationStorage] = useState(
    stopped && (waitingPublicationStorage != null || deliveryOnly),
  );
  const labels = stopped
    ? "acquisition.plan.continueStopped"
    : "acquisition.plan.continueCompleted";
  const [deliveryLayoutOnly, setDeliveryLayoutOnly] = useState(false);
  const parts = snapshot.plan?.parts ?? [];
  const partInputStage = (partId: string) => inputStage && currentProcessingInputStage(snapshot, partId, inputStage);
  const stageDirectory = (partId: string) => inputStage && snapshot.stageDirectories?.taskId === snapshot.task.id
    ? snapshot.stageDirectories.parts[partId]?.[partInputStage(partId)!]?.directory : undefined;
  const [drafts, setDrafts] = useState<Record<string, PartDraft>>(() => {
    const reviewed =
      retryStage === "processing" && !publicationStorage
        ? reviewedProcessingInput(snapshot)
        : undefined;
    let hasPriorReview = false;
    try { hasPriorReview = JSON.parse(snapshot.task.waitPromptJson || "{}").deliveryReview === true; } catch { /* Require a choice for an unreadable prior review. */ hasPriorReview = true; }
    return Object.fromEntries(
      parts.map((part) => {
        const processed = (snapshot.replanArtifacts ?? []).filter(
          (item) =>
            item.partId === part.id &&
            isPublicationInput(item) &&
            (!deliveryOnly || item.canRetryDelivery === true),
        );
        const currentDirectories = (snapshot.replanArtifacts ?? []).filter(item =>
          item.partId === part.id && inputKinds(mode).includes(item.kind) && canReadCurrentDirectory(item));
        return [
          part.id,
          {
            artifactId:
              publicationStorage && processed.length === 1
                ? processed[0].artifactId
                : reviewed?.partId === part.id
                  ? reviewed.artifactId
                  : retryStage === "processing" && !hasPriorReview && currentDirectories.length === 1
                    ? currentDirectories[0].artifactId : "",
            choice: deliveryOnly
              ? "keep"
              : preferAi || (part.useAdaptiveAssessment ?? snapshot.plan?.adaptiveFileProcessing)
                ? "ai"
                : retryStage === "processing"
                  ? undefined
                  : "defer",
            instructions: manualPlan,
          },
        ];
      }),
    );
  });
  const [combineInputs, setCombineInputs] = useState(false);
  const [combinedPlan, setCombinedPlan] = useState(manualPlan);
  const analyzeWithAi =
    !deliveryOnly && !combineInputs && parts.some((part) => drafts[part.id]?.choice === "ai");
  const [adaptiveFileProcessingMaxIterations, setAdaptiveFileProcessingMaxIterations] = useState(
    Math.min(5, Math.max(1, snapshot.plan?.adaptiveFileProcessingMaxIterations ?? 3)),
  );
  const [adaptiveContext, setAdaptiveContext] = useState(
    snapshot.plan?.adaptiveFileProcessingContext ?? "",
  );
  const canCombine =
    !inputStage &&
    !deliveryOnly &&
    snapshot.task.status !== AcquisitionStatus.Waiting &&
    parts.length >= 2 &&
    parts.length <= 10 &&
    parts.every((part) =>
      snapshot.replanArtifacts.some(
        (a) =>
          a.partId === part.id && a.kind !== "compositeFiles" && inputKinds(mode).includes(a.kind),
      ),
    );
  const [deliveryRootDirectory, setDeliveryRootDirectory] = useState<string>();
  const [useCurrentStorageDefaults, setUseCurrentStorageDefaults] = useState(false);
  const [saving, setSaving] = useState(false);
  const reserve = useAttemptStorageReserve(saving);
  const submitting = useRef(false);
  const [error, setError] = useState<string>();
  const candidates = (partId: string) =>
    (snapshot.replanArtifacts ?? []).filter(
      (item) =>
        item.partId === partId &&
        (!deliveryOnly || item.canRetryDelivery === true) &&
        (publicationStorage
          ? isPublicationInput(item)
          : inputKinds(mode).includes(item.kind)) &&
        (!combineInputs || item.kind !== "compositeFiles"),
    );
  const update = (partId: string, value: Partial<PartDraft>) =>
    setDrafts((current) => ({
      ...current,
      [partId]: {
        ...current[partId],
        ...value,
      },
    }));
  const alreadyWaiting =
    retryStage === "processing" &&
    !combineInputs &&
    snapshot.task.status === AcquisitionStatus.Waiting &&
    parts.length > 0 &&
    parts.every((part) => drafts[part.id]?.choice === "defer");
  const valid =
    reserve.ready &&
    (inputStage ? parts.length > 0 && parts.every(part => !!stageDirectory(part.id)) : deliveryOnly
      ? snapshot.task.workflowRunId === detail.task.workflowRunId &&
        parts.length > 0 &&
        parts.every((part) =>
          detail.replanArtifacts.some((a) => a.partId === part.id && a.canRetryDelivery),
        )
      : stopped
        ? hasStoppedProcessingInputs(
            snapshot,
            waitingPathConflict,
            waitingPublicationStorage,
            waitingLocalProcessing,
          )
        : hasCompletedProcessingInputs(snapshot)) &&
    deliveryRootDirectory !== "" &&
    !!snapshot.task.workflowRunId &&
    !alreadyWaiting &&
    error !== "PLAN_VERSION_CONFLICT" &&
    parts.every((part) => {
      const draft = drafts[part.id];
      return (
        draft &&
        (inputStage ? !!stageDirectory(part.id) :
          candidates(part.id).some((item) => item.artifactId === draft.artifactId) &&
          (deliveryOnly || combineInputs || canReadCurrentDirectory(candidates(part.id).find(item => item.artifactId === draft.artifactId)))) &&
        (combineInputs ||
          (!!draft.choice &&
            (draft.choice !== "manual" || completeInstructions(draft.instructions))))
      );
    }) &&
    (!combineInputs || completeInstructions(combinedPlan)) &&
    (!analyzeWithAi ||
      (Number.isInteger(adaptiveFileProcessingMaxIterations) &&
        adaptiveFileProcessingMaxIterations >= 1 &&
        adaptiveFileProcessingMaxIterations <= 5));
  if (!stopped && deliveryLayoutOnly)
    return (
      <CompletedDeliveryLayoutRevision
        detail={snapshot}
        currentDeliveryRoot={currentDeliveryRoot}
        onSaved={onSaved}
        onCancel={onCancel}
        onBack={() => setDeliveryLayoutOnly(false)}
      />
    );
  return (
    <section
      ref={setContainer}
      aria-label={t(`${labels}.title`)}
      className={inDialog ? "space-y-3" : "space-y-3 rounded-lg border border-primary-200 p-3"}
    >
      {!inDialog && <h3 className="font-medium">
        {t(deliveryOnly ? "acquisition.timeline.retry.delivery" : `${labels}.title`)}
      </h3>}
      {onBack && (
        <Button size="sm" variant="light" isDisabled={saving} onPress={onBack}>
          {t("acquisition.plan.continueStopped.back")}
        </Button>
      )}
      {!retryStage && !stopped && canReviseDeliveryLayout(snapshot) && (
        <Button
          size="sm"
          variant="flat"
          isDisabled={saving}
          onPress={() => setDeliveryLayoutOnly(true)}
        >
          {t("artifact.deliveryLayout.revisionTitle")}
        </Button>
      )}
      <p className="text-sm text-default-500">
        {t(
          deliveryOnly
            ? "acquisition.timeline.deliveryHint"
            : retryStage
              ? "acquisition.timeline.processingHint"
              : `${labels}.hint`,
        )}
      </p>
      {canCombine && (
        <Checkbox isSelected={combineInputs} isDisabled={saving} onValueChange={setCombineInputs}>
          <span className="block text-sm">{t("acquisition.plan.composite.title")}</span>
          <span className="block text-xs text-default-500">
            {t("acquisition.plan.composite.hint")}
          </span>
        </Checkbox>
      )}
      {!inputStage && <RevisionPlacementDialog
        detail={snapshot} currentRoot={currentDeliveryRoot} disabled={saving}
        useCurrent={useCurrentStorageDefaults} override={deliveryRootDirectory}
        forceCurrent={publicationStorage || combineInputs} showRoots={!stopped}
        onApply={({ useCurrent, override }) => {
          setUseCurrentStorageDefaults(useCurrent);
          setDeliveryRootDirectory(override);
        }}
      />}
      {parts.map((part) => (
        <section
          key={part.id}
          aria-label={part.title || part.id}
          className="min-w-0 space-y-2 rounded-lg border border-default-200 p-3"
        >
          {(!inDialog || parts.length > 1) && <h4 className="text-sm font-medium">{part.title || part.id}</h4>}
          {combineInputs && <p className="text-xs text-default-500">{part.id}</p>}
          {!inputStage && <Select
            popoverProps={popoverProps}
            labelPlacement="outside"
            classNames={{ base: "min-w-0 max-w-full", trigger: "min-h-10 h-auto py-2", innerWrapper: "min-w-0", value: "!whitespace-normal break-all text-left !overflow-visible" }}
            listboxProps={{ itemClasses: { base: "h-auto", title: "whitespace-normal break-all", wrapper: "min-w-0" } }}
            label={t(`${labels}.input`)}
            selectedKeys={drafts[part.id]?.artifactId ? [drafts[part.id].artifactId] : []}
            isDisabled={saving}
            dataSource={candidates(part.id).map((item) => ({
              value: item.artifactId,
              label: item.rootDirectory,
              description: deliveryOnly || combineInputs
                ? t("acquisition.plan.replan.fileCount", { count: item.fileCount }) : undefined,
            }))}
            onSelectionChange={(keys) =>
              update(part.id, { artifactId: String(Array.from(keys)[0] ?? "") })
            }
          />}
          {inputStage && stageDirectory(part.id) && snapshot.task.workflowRunId && <ProcessingDirectoryFiles
            key={`${part.id}:${partInputStage(part.id)}:${stageDirectory(part.id)}`}
            taskContext={{ taskId: snapshot.task.id, runId: snapshot.task.workflowRunId }} path={stageDirectory(part.id)!} />}
          {inputStage && !stageDirectory(part.id) && <p role="alert" className="text-sm text-warning-700">{t("acquisition.directories.noStageInput")}</p>}
          {!inputStage && !deliveryOnly && !combineInputs && drafts[part.id].artifactId && <>
            <p className="text-xs text-default-500">{t("acquisition.processingInput.currentDirectoryHint")}</p>
            {!canReadCurrentDirectory(candidates(part.id).find(item => item.artifactId === drafts[part.id].artifactId)) && (
              <p role="alert" className="text-sm text-warning-600">{t("acquisition.processingInput.currentDirectoryUnavailable")}</p>
            )}
          </>}
          {!inputStage && retryStage === "processing" && snapshot.task.workflowRunId && drafts[part.id].artifactId &&
            candidates(part.id).some(item => item.artifactId === drafts[part.id].artifactId) && (
              !combineInputs ?
                canReadCurrentDirectory(candidates(part.id).find(item => item.artifactId === drafts[part.id].artifactId)) &&
                  <ProcessingDirectoryFiles key={drafts[part.id].artifactId}
                    taskContext={{ taskId: snapshot.task.id, runId: snapshot.task.workflowRunId }}
                    path={candidates(part.id).find(item => item.artifactId === drafts[part.id].artifactId)!.rootDirectory} /> :
                <ProcessingFileList key={drafts[part.id].artifactId}
                  runId={snapshot.task.workflowRunId} artifactId={drafts[part.id].artifactId} />
            )}
          {!inputStage && !candidates(part.id).length && (
            <p role="status" className="text-xs text-warning-600">
              {t("acquisition.plan.replan.noEvidence")}
            </p>
          )}
          {!deliveryOnly && !combineInputs && (
            <>
              <ProcessingChoiceGroup
                label={t("acquisition.processingChoice.label")}
                value={drafts[part.id].choice}
                isRequired
                isDisabled={saving}
                choices={processingChoices.map(value => ({
                  value,
                  icon: value,
                  label: t(`acquisition.processingChoice.${value}`),
                }))}
                onChange={choice => update(part.id, { choice })}
              />
              {drafts[part.id].choice === "manual" && (
                <ProcessingPlanField
                  stepsOnly
                  value={drafts[part.id].instructions}
                  inputRootDirectory={
                    stageDirectory(part.id) ?? candidates(part.id).find(
                      (item) => item.artifactId === drafts[part.id].artifactId,
                    )?.rootDirectory
                  }
                  onChange={(instructions) => update(part.id, { instructions })}
                  isDisabled={saving}
                />
              )}
            </>
          )}
        </section>
      ))}
      {analyzeWithAi && (
        <section
          className="space-y-2 rounded-lg border border-primary-200 p-3"
          aria-label={t("acquisition.processingChoice.ai")}
        >
          <NumberInput
            size="sm"
            label={t("acquisition.plan.continueCompleted.adaptiveIterations")}
            description={t("acquisition.plan.continueCompleted.adaptiveIterationsHint")}
            minValue={1}
            maxValue={5}
            step={1}
            value={adaptiveFileProcessingMaxIterations}
            isDisabled={saving}
            onValueChange={(value) => {
              setAdaptiveFileProcessingMaxIterations(Math.min(5, Math.max(1, value)));
            }}
          />
          <Textarea
            size="sm"
            label={t("acquisition.plan.continueCompleted.adaptiveContext")}
            description={t("acquisition.plan.continueCompleted.adaptiveContextHint")}
            value={adaptiveContext}
            maxLength={16384}
            minRows={2}
            isDisabled={saving}
            onValueChange={setAdaptiveContext}
          />
        </section>
      )}
      {combineInputs && (
        <section
          className="space-y-2 rounded-lg border border-primary-200 p-3"
          aria-label={t("acquisition.plan.composite.scope")}
        >
          <p className="text-sm">
            {t("acquisition.plan.composite.summary", {
              count: parts.length,
              files: parts.reduce(
                (sum, part) =>
                  sum +
                  (candidates(part.id).find((a) => a.artifactId === drafts[part.id]?.artifactId)
                    ?.fileCount ?? 0),
                0,
              ),
            })}
          </p>
          <p className="text-xs text-default-500">{t("acquisition.plan.composite.pathsHint")}</p>
          <ProcessingPlanField
            stepsOnly
            value={combinedPlan}
            onChange={setCombinedPlan}
            isDisabled={saving}
          />
        </section>
      )}
      {alreadyWaiting && (
        <p role="status" className="text-sm text-default-500">
          {t("acquisition.processingChoice.alreadyWaiting")}
        </p>
      )}
      {error && (
        <p role="alert" className="text-sm text-danger">
          {t(`${error.startsWith("STAGE_DIRECTORY_") ? "acquisition.directories.error" : "acquisition.plan.replan.error"}.${error}`, {
            defaultValue: t("acquisition.plan.actionFailed"),
          })}
        </p>
      )}
      {reserve.control}
      <ResumeActions busy={saving} cancel={<Button variant="light" isDisabled={saving} onPress={onCancel}>
          {t("common.action.cancel")}
        </Button>}>
        <Button
          color="primary"
          isDisabled={!valid}
          isLoading={saving}
          onPress={() => {
            if (!valid || submitting.current) return;
            submitting.current = true;
            setSaving(true);
            setError(undefined);
            void planTaskApi
              .replan(snapshot.task.id, {
                expectedRevision: snapshot.task.planRevision,
                expectedRunId: snapshot.task.workflowRunId!,
                reason: t(
                  deliveryOnly
                    ? "acquisition.timeline.retry.delivery"
                    : "acquisition.timeline.retry.processing",
                ),
                ...(deliveryOnly ? { retryDeliveryOnly: true } : {}),
                ...(deliveryRootDirectory ? { deliveryRootDirectory } : {}),
                ...(!inputStage && (publicationStorage || combineInputs || useCurrentStorageDefaults)
                  ? { useCurrentStorageDefaults: true }
                  : {}),
                ...(combineInputs
                  ? {
                      compositeInputs: parts.map((part) => ({
                        partId: part.id,
                        artifactId: drafts[part.id].artifactId,
                      })),
                    }
                  : {}),
                ...(!deliveryOnly && !combineInputs
                  ? {
                      enableAdaptiveAssessment: analyzeWithAi,
                      ...(analyzeWithAi
                        ? {
                            maxAdaptiveIterations: adaptiveFileProcessingMaxIterations,
                            adaptiveContext: adaptiveContext.trim(),
                          }
                        : {}),
                    }
                  : {}),
                items: combineInputs
                  ? [
                      {
                        partId: parts[0].id,
                        plan: JSON.parse(combinedPlan),
                        useAdaptiveAssessment: false,
                        artifactEvidenceIds: parts.map((part) => drafts[part.id].artifactId),
                      },
                    ]
                  : parts.map((part) => ({
                      partId: part.id,
                      plan: planForChoice(drafts[part.id]),
                      ...(!deliveryOnly
                        ? { useAdaptiveAssessment: drafts[part.id].choice === "ai" }
                        : {}),
                      ...(inputStage ? { artifactEvidenceIds: [], inputMode: "stageDirectory", inputStage: partInputStage(part.id) } : {
                        artifactEvidenceIds: [drafts[part.id].artifactId],
                        continueFromArtifactId: drafts[part.id].artifactId,
                        ...(!deliveryOnly ? { inputMode: "currentDirectory" } : {}),
                      }),
                    })),
              })
              .then((next) => onSaved(next, reserve.options!))
              .catch((cause: unknown) => {
                const code = cause instanceof Error ? cause.message : "";
                setError(
                  /^PLAN_(STAGE_DIRECTORY_(INVALID|UNAVAILABLE|ALIGNMENT_REQUIRED)|REPLAN_(INVALID|NOT_STOPPED|EVIDENCE_INVALID|EVIDENCE_UNAVAILABLE|DELIVERY_CONFLICT)|CURRENT_DIRECTORY_(UNAVAILABLE|SHARED|CHANGED|TOO_LARGE|EMPTY|MODE_INVALID)|COMPOSITE_(INPUT_INVALID|ROOT_INVALID|PATH_CONFLICT)|VERSION_CONFLICT)$/.test(
                    code,
                  ) || code.startsWith("STAGE_DIRECTORY_")
                    ? code
                    : "unknown",
                );
              })
              .finally(() => {
                submitting.current = false;
                setSaving(false);
              });
          }}
        >
          {t(retryStage ? "acquisition.timeline.startAttempt" : "acquisition.plan.replan.confirm")}
        </Button>
      </ResumeActions>
    </section>
  );
}

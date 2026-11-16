import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Select } from "@/components/bakaui";
import ResumeActions from "@/components/Workflow/ResumeActions";
import { defaultDeliveryContentLayout } from "@/components/FileArtifacts/DeliveryContentLayoutFields";
import DeliveryLayoutPreview from "@/components/FileArtifacts/DeliveryLayoutPreview";
import type { PlanTaskDetail } from "./models";
import { planTaskApi } from "./api";
import RevisionPlacementDialog from "./RevisionPlacementDialog";
import { AcquisitionStatus } from "@/sdk/constants";
import useAttemptStorageReserve, { type AttemptRunOptions } from "./useAttemptStorageReserve";

const published = (detail: PlanTaskDetail, partId: string) =>
  (detail.replanArtifacts ?? []).filter(
    (item) =>
      item.partId === partId &&
      item.kind === "publishedFiles" &&
      item.canRepublish === true &&
      !!item.fingerprint &&
      item.fileCount > 0,
  );

export const canReviseDeliveryLayout = (detail: PlanTaskDetail) =>
  detail.task.status === AcquisitionStatus.Completed &&
  !!detail.task.workflowRunId &&
  !!detail.plan?.parts.length &&
  detail.plan.parts.every((part) => published(detail, part.id).length > 0);

type Proof = {
  artifactId: string;
  projectionFingerprint: string;
  changed: boolean;
};

const errors: Record<string, string> = {
  PLAN_VERSION_CONFLICT: "acquisition.plan.replan.error.PLAN_VERSION_CONFLICT",
  PLAN_REPLAN_EVIDENCE_INVALID: "acquisition.plan.replan.error.PLAN_REPLAN_EVIDENCE_INVALID",
  PLAN_REPLAN_EVIDENCE_UNAVAILABLE:
    "acquisition.plan.replan.error.PLAN_REPLAN_EVIDENCE_UNAVAILABLE",
  PLAN_REPLAN_DELIVERY_CONFLICT: "acquisition.plan.replan.error.PLAN_REPLAN_DELIVERY_CONFLICT",
  PLAN_REPLAN_NOT_STOPPED: "acquisition.plan.replan.error.PLAN_REPLAN_NOT_STOPPED",
  DELIVERY_LAYOUT_PREVIEW_CHANGED: "artifact.deliveryLayout.changedSinceReview",
  DELIVERY_LAYOUT_UNCHANGED: "artifact.deliveryLayout.previewUnchanged",
};

/** Save a new version bound to the selected, previously published outputs. Starting it is separate. */
export default function CompletedDeliveryLayoutRevision({
  detail,
  currentDeliveryRoot,
  onSaved,
  onBack,
  retry = false,
  inDialog = false,
  onCancel,
}: {
  detail: PlanTaskDetail;
  currentDeliveryRoot?: string | null;
  onSaved: (next: PlanTaskDetail, options: AttemptRunOptions) => void;
  onBack?: () => void;
  retry?: boolean;
  inDialog?: boolean;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const [snapshot] = useState(detail);
  const parts = snapshot.plan?.parts ?? [];
  const runId = snapshot.task.workflowRunId;
  const [policy] = useState(defaultDeliveryContentLayout);
  const [selected, setSelected] = useState<Record<string, string>>(() =>
    Object.fromEntries(
      parts.map((part) => {
        const choices = published(snapshot, part.id);
        return [part.id, choices.length === 1 ? choices[0].artifactId : ""];
      }),
    ),
  );
  const [proofs, setProofs] = useState<Record<string, Proof | undefined>>({});
  const [useCurrentStorageDefaults, setUseCurrentStorageDefaults] = useState(false);
  const [deliveryRootDirectory, setDeliveryRootDirectory] = useState<string>();
  const [saving, setSaving] = useState(false);
  const reserve = useAttemptStorageReserve(saving);
  const submitting = useRef(false);
  const [error, setError] = useState<string>();
  const valid =
    reserve.ready &&
    !saving &&
    deliveryRootDirectory !== "" &&
    canReviseDeliveryLayout(snapshot) &&
    error !== "PLAN_VERSION_CONFLICT" &&
    parts.every((part) => {
      const proof = proofs[part.id];
      return (
        proof?.projectionFingerprint &&
        proof.artifactId === selected[part.id] &&
        published(snapshot, part.id).some((item) => item.artifactId === proof.artifactId)
      );
    }) &&
    (retry || parts.some((part) => proofs[part.id]?.changed));

  const save = async () => {
    if (!valid || !runId || submitting.current) return;
    submitting.current = true;
    setSaving(true);
    setError(undefined);
    try {
      const next = await planTaskApi.replan(snapshot.task.id, {
        expectedRevision: snapshot.task.planRevision,
        expectedRunId: runId,
        reason: t(
          retry ? "acquisition.timeline.retry.delivery" : "artifact.deliveryLayout.revisionReason",
        ),
        deliveryContentLayout: policy,
        ...(retry ? { retryDeliveryOnly: true } : {}),
        ...(deliveryRootDirectory ? { deliveryRootDirectory } : {}),
        useCurrentStorageDefaults,
        items: parts.map((part) => ({
          partId: part.id,
          plan: { requirement: "notRequired", steps: [], evidence: [] },
          continueFromArtifactId: selected[part.id],
          artifactEvidenceIds: [selected[part.id]],
          expectedDeliveryProjectionFingerprint: proofs[part.id]!.projectionFingerprint,
        })),
      });
      onSaved(next, reserve.options!);
    } catch (cause) {
      setError(
        cause instanceof Error && Object.prototype.hasOwnProperty.call(errors, cause.message)
          ? cause.message
          : "failed",
      );
      setProofs({});
    } finally {
      submitting.current = false;
      setSaving(false);
    }
  };

  return (
    <section
      aria-label={t(
        retry ? "acquisition.timeline.retry.delivery" : "artifact.deliveryLayout.revisionTitle",
      )}
      className={inDialog ? "space-y-3" : "space-y-3 rounded-lg border border-primary-200 p-3"}
    >
      {!inDialog && <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-medium">
          {t(
            retry ? "acquisition.timeline.retry.delivery" : "artifact.deliveryLayout.revisionTitle",
          )}
        </h3>
        {onBack && (
          <Button size="sm" variant="light" isDisabled={saving} onPress={onBack}>
            {t("artifact.deliveryLayout.backToProcessing")}
          </Button>
        )}
      </div>}
      <p className="text-sm text-default-500">
        {t(retry ? "acquisition.timeline.deliveryHint" : "artifact.deliveryLayout.revisionHint")}
      </p>
      <p className="text-xs text-default-500">{t("artifact.deliveryLayout.acquisitionDefault")}</p>
      <RevisionPlacementDialog
        detail={snapshot} currentRoot={currentDeliveryRoot} disabled={saving}
        useCurrent={useCurrentStorageDefaults} override={deliveryRootDirectory}
        showRoots
        onApply={({ useCurrent, override }) => {
          setUseCurrentStorageDefaults(useCurrent);
          setDeliveryRootDirectory(override);
        }}
      />
      {parts.map((part) => {
        const choices = published(snapshot, part.id);
        const item = choices.find((candidate) => candidate.artifactId === selected[part.id]);
        return (
          <section
            key={part.id}
            className="min-w-0 space-y-2 rounded-lg border border-default-200 p-3"
            aria-label={part.title || part.id}
          >
            <h4 className="text-sm font-medium">{part.title || part.id}</h4>
            <Select
              labelPlacement="outside"
            classNames={{ base: "min-w-0 max-w-full", trigger: "min-h-10 h-auto py-2", innerWrapper: "min-w-0", value: "!whitespace-normal break-all text-left !overflow-visible" }}
            listboxProps={{ itemClasses: { base: "h-auto", title: "whitespace-normal break-all", wrapper: "min-w-0" } }}
            label={t("artifact.deliveryLayout.selectPublished")}
              isDisabled={saving}
              selectedKeys={item ? [item.artifactId] : []}
              dataSource={choices.map((candidate) => ({
                value: candidate.artifactId,
                label: candidate.rootDirectory,
                description: t("acquisition.plan.replan.fileCount", { count: candidate.fileCount }),
              }))}
              onSelectionChange={(keys) => {
                setSelected((prev) => ({ ...prev, [part.id]: String(Array.from(keys)[0] ?? "") }));
                setProofs((prev) => ({ ...prev, [part.id]: undefined }));
              }}
            />
            {item && runId && (
              <DeliveryLayoutPreview
                key={item.artifactId}
                runId={runId}
                artifactId={item.artifactId}
                expectedFingerprint={item.fingerprint}
                policy={policy}
                disabled={saving}
                onReviewed={(proof) =>
                  setProofs((prev) => ({
                    ...prev,
                    [part.id]: proof && { ...proof, artifactId: item.artifactId },
                  }))
                }
              />
            )}
          </section>
        );
      })}
      {error && (
        <p role="alert" className="text-sm text-danger">
          {t(errors[error] ?? "artifact.deliveryLayout.revisionFailed")}
        </p>
      )}
      {saving && (
        <p role="status" className="text-xs text-default-500">
          {t("artifact.deliveryLayout.validatingFiles")}
        </p>
      )}
      <p className="text-xs text-default-500">
        {t(retry ? "acquisition.timeline.reviewDelivery" : "artifact.deliveryLayout.reviewAll")}
      </p>
      {reserve.control}
      <ResumeActions busy={saving} cancel={<Button variant="light" isDisabled={saving} onPress={onCancel}>
          {t("common.action.cancel")}
        </Button>}>
        <Button color="primary" isDisabled={!valid} isLoading={saving} onPress={() => void save()}>
          {t(retry ? "acquisition.timeline.startAttempt" : "artifact.deliveryLayout.saveRevision")}
        </Button>
      </ResumeActions>
    </section>
  );
}

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Select } from "@/components/bakaui";
import ProcessingPlanField, {
  isCompleteProcessingPlan,
} from "@/components/Workflow/ProcessingPlanField";
import type { PlanTaskDetail } from "./models";
import { planTaskApi } from "./api";
import CompletedProcessingRevisionForm, {
  hasStoppedProcessingInputs,
} from "./CompletedProcessingRevisionForm";

/** A task-local repair creates a new ready plan; starting it remains a separate action. */
export default function PlanReplanForm({
  detail,
  onSaved,
  onCancel,
}: {
  detail: PlanTaskDetail;
  onSaved: (next: PlanTaskDetail) => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  // Keep the inspected revision and evidence stable while the parent refreshes.
  const [snapshot] = useState(detail);
  const [continueLocalFiles, setContinueLocalFiles] = useState(false);
  const parts = snapshot.plan?.parts ?? [];
  const initialPart = parts.length === 1 ? parts[0] : undefined;
  const [partId, setPartId] = useState(initialPart?.id ?? "");
  const [planJson, setPlanJson] = useState(
    initialPart?.processing ? JSON.stringify(initialPart.processing, null, 2) : "",
  );
  const [evidenceIds, setEvidenceIds] = useState<string[]>(
    initialPart?.continueFrom ? [initialPart.continueFrom.artifactId] : [],
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const artifacts = snapshot.replanArtifacts.filter((item) => item.partId === partId);
  const continuedInput = parts.find((part) => part.id === partId)?.continueFrom;
  const continuedInputAvailable =
    !continuedInput ||
    artifacts.some(
      (item) =>
        item.artifactId === continuedInput.artifactId &&
        item.fingerprint === continuedInput.fingerprint,
    );
  const valid =
    !!partId &&
    evidenceIds.length > 0 &&
    evidenceIds.length <= 10 &&
    continuedInputAvailable &&
    isCompleteProcessingPlan(planJson);
  if (continueLocalFiles)
    return (
      <CompletedProcessingRevisionForm
        detail={snapshot}
        mode="stopped"
        onSaved={onSaved}
        onCancel={onCancel}
        onBack={() => setContinueLocalFiles(false)}
      />
    );
  return (
    <section
      aria-label={t("acquisition.plan.replan.title")}
      className="space-y-3 rounded-lg border border-primary-200 p-3"
    >
      <h3 className="font-medium">{t("acquisition.plan.replan.title")}</h3>
      <p className="text-sm text-default-500">{t("acquisition.plan.replan.hint")}</p>
      {hasStoppedProcessingInputs(snapshot) && (
        <Button
          size="sm"
          variant="flat"
          isDisabled={saving}
          onPress={() => setContinueLocalFiles(true)}
        >
          {t("acquisition.plan.continueStopped.title")}
        </Button>
      )}
      {parts.length > 1 && (
        <Select
          label={t("acquisition.plan.replan.part")}
          selectedKeys={partId ? [partId] : []}
          isDisabled={saving}
          dataSource={parts.map((part) => ({ value: part.id, label: part.title || part.id }))}
          onSelectionChange={(keys) => {
            const id = String(Array.from(keys)[0] ?? "");
            const part = parts.find((item) => item.id === id);
            setPartId(id);
            setEvidenceIds(part?.continueFrom ? [part.continueFrom.artifactId] : []);
            setPlanJson(part?.processing ? JSON.stringify(part.processing, null, 2) : "");
          }}
        />
      )}
      {partId && (
        <>
          <ProcessingPlanField value={planJson} onChange={setPlanJson} isDisabled={saving} />
          <div className="space-y-2">
            <p className="text-sm font-medium">{t("acquisition.plan.replan.evidence")}</p>
            <p className="text-xs text-default-500">{t("acquisition.plan.replan.evidenceHint")}</p>
            {artifacts.length ? (
              artifacts.map((item) => (
                <Checkbox
                  key={item.artifactId}
                  isDisabled={
                    saving ||
                    item.artifactId === continuedInput?.artifactId ||
                    (evidenceIds.length >= 10 && !evidenceIds.includes(item.artifactId))
                  }
                  isSelected={evidenceIds.includes(item.artifactId)}
                  onValueChange={(selected) =>
                    setEvidenceIds((previous) =>
                      selected
                        ? [...previous, item.artifactId]
                        : previous.filter((id) => id !== item.artifactId),
                    )
                  }
                >
                  <span className="block break-all text-sm">{item.rootDirectory}</span>
                  {item.artifactId === continuedInput?.artifactId && (
                    <span className="block text-xs text-primary">
                      {t("acquisition.plan.continueFiles")}
                    </span>
                  )}
                  <span className="block text-xs text-default-500">
                    {t("acquisition.plan.replan.fileCount", { count: item.fileCount })}
                  </span>
                </Checkbox>
              ))
            ) : (
              <p role="status" className="text-xs text-warning-600">
                {t("acquisition.plan.replan.noEvidence")}
              </p>
            )}
            {!continuedInputAvailable && (
              <p role="status" className="text-xs text-warning-600">
                {t("acquisition.plan.replan.error.PLAN_REPLAN_EVIDENCE_UNAVAILABLE")}
              </p>
            )}
          </div>
        </>
      )}
      {error && (
        <p role="alert" className="text-sm text-danger">
          {t(`acquisition.plan.replan.error.${error}`, {
            defaultValue: t("acquisition.plan.actionFailed"),
          })}
        </p>
      )}
      <div className="flex flex-wrap justify-end gap-2">
        <Button variant="light" isDisabled={saving} onPress={onCancel}>
          {t("common.action.cancel")}
        </Button>
        <Button
          color="primary"
          isDisabled={!valid}
          isLoading={saving}
          onPress={() => {
            if (!valid || saving) return;
            setSaving(true);
            setError(undefined);
            void planTaskApi
              .replan(snapshot.task.id, {
                expectedRevision: snapshot.task.planRevision,
                expectedRunId: snapshot.task.workflowRunId!,
                reason: t("acquisition.plan.replan.title"),
                items: [
                  {
                    partId,
                    plan: JSON.parse(planJson),
                    artifactEvidenceIds: evidenceIds,
                    ...(continuedInput
                      ? { continueFromArtifactId: continuedInput.artifactId }
                      : {}),
                  },
                ],
              })
              .then(onSaved)
              .catch((cause: unknown) => {
                const code = cause instanceof Error ? cause.message : "";
                setError(
                  /^PLAN_(REPLAN_(INVALID|NOT_STOPPED|EVIDENCE_INVALID|EVIDENCE_UNAVAILABLE)|VERSION_CONFLICT)$/.test(
                    code,
                  )
                    ? code
                    : "unknown",
                );
              })
              .finally(() => setSaving(false));
          }}
        >
          {t("acquisition.plan.replan.confirm")}
        </Button>
      </div>
    </section>
  );
}

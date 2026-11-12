import { useState } from "react";
import type { TFunction } from "i18next";
import { useTranslation } from "react-i18next";
import { Button, Modal } from "@/components/bakaui";
import { activityDisplayName } from "@/components/Workflow/displayNames";
import { WorkflowOutputBlock } from "@/components/Workflow/WorkflowNodeMetadata";
import { AttemptStamp, AttemptStatus, assessmentCode, type Attempt } from "./acquisitionAttempt";
import type { AcquisitionStage } from "./AcquisitionExecutionTimeline";
import AssessmentInputOutput from "./AssessmentInputOutput";

type Block = Attempt["outputs"][number];
type Group = "attention" | "result" | "files" | "related";

function groupOf(block: Block): Group {
  if (block.kind === "warning") return "attention";
  if (block.kind === "artifact") return "files";
  if (block.kind === "remote" || block.kind === "task") return "related";
  return "result";
}

function attemptOutputs(stage: AcquisitionStage, outputs: Block[], t: TFunction) {
  const groups: Record<Group, Block[]> = { attention: [], result: [], files: [], related: [] };
  const notices = new Set<string>();
  const fileRoles = new Map<string, { position: number; labels: Set<string> }>();
  for (const block of outputs) {
    // The workset carries the remote source and download task through later steps.
    if (stage !== "download" && (block.kind === "remote" || block.taskKind === "download"))
      continue;
    if (block.kind === "warning") {
      // A target can project its processing wait again as its general problem.
      const target = block.key.replace(/:(problem|processing)$/, "");
      const key = JSON.stringify([target, block.severity, block.code || block.message]);
      if (notices.has(key)) continue;
      notices.add(key);
    }
    if (block.kind === "artifact" && block.artifactId) {
      // An unchanged file can be both the input and the output of a review. Keep
      // the roles, but only one file list. Distinct locations and receipts stay separate.
      const target = block.key.slice(0, block.key.lastIndexOf(":"));
      const fileKey = JSON.stringify([
        target,
        block.artifactId,
        block.operationId,
        block.directory,
        block.cleanupReceiptId,
        block.cleanupStatus,
      ]);
      const existing = fileRoles.get(fileKey);
      const label = block.key.endsWith(":continuation")
        ? t("acquisition.records.inputFiles")
        : block.labelKey
          ? t(block.labelKey)
          : block.label || t("workflow.metadata.kind.artifact");
      if (existing) {
        const prior = groups.files[existing.position];
        if (prior.state && block.state && prior.state !== block.state) {
          groups.files.push(block);
          continue;
        }
        existing.labels.add(label);
        groups.files[existing.position] = {
          ...prior,
          state: prior.state || block.state,
          labelKey: undefined,
          label: Array.from(existing.labels).join(" · "),
        };
        continue;
      }
      fileRoles.set(fileKey, { position: groups.files.length, labels: new Set([label]) });
      groups.files.push({ ...block, labelKey: undefined, label });
      continue;
    }
    groups[groupOf(block)].push(block);
  }
  return groups;
}

export default function StageExecutionRecords({
  taskId,
  stage,
  attempts,
  initial,
  onClose,
  onOpenRun,
}: {
  taskId: number;
  stage: AcquisitionStage;
  attempts: Attempt[];
  initial: { id: string; io: boolean };
  onClose: () => void;
  onOpenRun: (id: number) => void;
}) {
  const { t } = useTranslation();
  const [selected, setSelected] = useState(initial);
  const attempt = attempts.find((item) => item.id === selected.id);
  const groups = attemptOutputs(stage, attempt?.outputs ?? [], t);
  const showIo = selected.io && !!attempt;
  const canInspect =
    attempt?.nodes.includes("fs.assessDelivery") || (attempt && assessmentCode(attempt));
  const stageName = t(`acquisition.timeline.stage.${stage}`);
  return (
    <Modal
      visible
      size="6xl"
      title={t("acquisition.records.title", { stage: stageName, id: taskId })}
      classNames={{
        base: "h-[83dvh] max-h-[calc(100dvh-1rem)] sm:max-h-[calc(100dvh-3rem)]",
        body: "min-h-0 overflow-hidden",
      }}
      onClose={onClose}
      footer={
        <Button variant="light" onPress={onClose}>
          {t("acquisition.plan.close")}
        </Button>
      }
    >
      <div className="grid h-full min-h-0 min-w-0 grid-rows-[auto_minmax(0,1fr)] gap-3 md:grid-cols-[14rem_minmax(0,1fr)] md:grid-rows-1">
        <nav
          aria-label={t("acquisition.records.attempts")}
          className="flex min-h-0 gap-1 overflow-x-auto pb-1 md:flex-col md:overflow-y-auto md:border-r md:border-default-200 md:pr-2"
        >
          {attempts
            .slice()
            .reverse()
            .map((item) => (
              <Button
                key={item.id}
                size="sm"
                variant={item.id === selected.id ? "flat" : "light"}
                color={item.id === selected.id ? "primary" : "default"}
                aria-pressed={item.id === selected.id}
                className="h-auto shrink-0 justify-start px-2 py-2 text-left"
                onPress={() => setSelected({ id: item.id, io: false })}
              >
                <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
                  {t("workflow.metadata.attempt", { number: item.attempt })}
                  <span className="text-xs text-default-500">#{item.runId}</span>
                  <AttemptStatus status={item.status} />
                </span>
              </Button>
            ))}
        </nav>
        {attempt && (
          <div key={attempt.id} className="min-h-0 min-w-0 space-y-4 overflow-auto pr-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-sm font-medium">
                {t("workflow.resume.guide.run")} #{attempt.runId}
              </span>
              <AttemptStamp attempt={attempt} />
              <AttemptStatus status={attempt.status} />
            </div>
            <div className="flex flex-wrap gap-2">
              {canInspect && (
                <Button
                  size="sm"
                  variant="flat"
                  color={showIo ? "primary" : "default"}
                  aria-expanded={showIo}
                  onPress={() => setSelected({ id: attempt.id, io: !showIo })}
                >
                  {t(showIo ? "acquisition.records.closeIo" : "acquisition.records.viewIo")}
                </Button>
              )}
              <Button size="sm" variant="light" onPress={() => onOpenRun(attempt.runId)}>
                {t("acquisition.timeline.openRun", { id: attempt.runId })}
              </Button>
            </div>
            {showIo && <AssessmentInputOutput taskId={taskId} attempt={attempt} />}
            {attempt.nodes.length > 0 && (
              <section>
                <h4 className="text-sm font-medium">{t("acquisition.records.steps")}</h4>
                <p className="mt-1 break-words text-sm text-default-600">
                  {attempt.nodes.map((kind) => activityDisplayName(t, kind)).join(" → ")}
                </p>
              </section>
            )}
            {(Object.keys(groups) as Group[]).map(
              (group) =>
                groups[group].length > 0 && (
                  <section key={group} aria-label={t(`acquisition.records.${group}`)}>
                    <h4 className="mb-1 text-sm font-medium">
                      {t(`acquisition.records.${group}`)}
                    </h4>
                    <div className="space-y-2">
                      {groups[group].map((block, index) => (
                        <WorkflowOutputBlock
                          key={`${block.key}:${index}`}
                          block={block}
                          compact
                          runId={attempt.runId}
                          readOnly
                        />
                      ))}
                    </div>
                  </section>
                ),
            )}
            {!attempt.outputs.length && (
              <p className="text-xs text-default-500">{t("workflow.metadata.noOutput")}</p>
            )}
            {attempt.outputsTruncated && (
              <p className="text-xs text-warning-600">{t("workflow.metadata.truncated")}</p>
            )}
          </div>
        )}
      </div>
    </Modal>
  );
}

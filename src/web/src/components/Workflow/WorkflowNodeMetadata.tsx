import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem, Button, Chip, Pagination, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowRunViewModel as Run } from "@/sdk/Api";
import { activityDisplayName } from "./displayNames";
import WorksetResults, { CleanupReceipt } from "@/components/CloudDownload/WorksetResults";
import { downloadProblemSummary } from "@/components/CloudDownload/problemLabels";
import {
  useDetailAccordion,
  useDetailDisclosure,
  useDetailExpansion,
  type DetailExpansionRequest,
} from "@/hooks/useDetailExpansion";
import DetailExpansionControls from "./DetailExpansionControls";
import WorkflowStepCounts from "./WorkflowStepCounts";
import WorkflowFileLocation from "./WorkflowFileLocation";
import ExecutionTiming from "./ExecutionTiming";
import WorkflowArtifactBrowserModal from "./WorkflowArtifactBrowserModal";

type Execution = NonNullable<Run["nodeExecutions"]>[number];
type Block = Execution["outputs"][number];
type ArtifactPage = NonNullable<
  Awaited<ReturnType<typeof BApi.workflow.getWorkflowArtifactFiles>>["data"]
>;
const bytes = (value: number) => {
  if (!Number.isFinite(value) || value < 0) return "—";
  const unit = value < 1024 ? 0 : Math.min(4, Math.floor(Math.log(value) / Math.log(1024)));
  return `${unit ? (value / 1024 ** unit).toFixed(1) : value} ${["B", "KiB", "MiB", "GiB", "TiB"][unit]}`;
};

/** Only the server's typed, redacted projections are rendered. Unknown payloads stay opaque. */
export default function WorkflowNodeMetadata({
  runId,
  executions,
  legacyOutput,
  planNodes,
  stepStats,
  planName,
  expansion,
  readOnly = false,
}: {
  runId: number;
  executions?: Execution[] | null;
  legacyOutput?: string | null;
  planNodes?: Run["planNodes"] | null;
  stepStats?: Run["stepStats"] | null;
  planName?: string;
  expansion?: DetailExpansionRequest;
  readOnly?: boolean;
}) {
  const { t } = useTranslation();
  const [fileBrowser, setFileBrowser] = useState<{ executionId?: number }>();
  const { request, setAll } = useDetailExpansion(expansion);
  const groups = new Map<string, {
    stepIndex: number; kind: string; notes?: string; attempts: Execution[];
    stat?: NonNullable<Run["stepStats"]>[number];
  }>();
  for (const node of planNodes ?? []) {
    groups.set(`${node.order}:${node.nodeId}`, {
      stepIndex: node.order, kind: node.kind, notes: node.notes, attempts: [],
    });
  }
  for (const execution of executions ?? []) {
    const key = `${execution.stepIndex}:${execution.nodeId}`;
    const group = groups.get(key) ?? { stepIndex: execution.stepIndex, kind: execution.kind, attempts: [] };
    group.attempts.push(execution);
    groups.set(key, group);
  }
  for (const stat of stepStats ?? []) {
    const matching = [...groups.values()].filter(group => group.stepIndex === stat.stepIndex && group.kind === stat.kind);
    // Stats have no node ID. Do not merge two distinct nodes merely because their kinds match.
    if (matching.length === 1) matching[0].stat = stat;
    else if (matching.length === 0) groups.set(`stat:${stat.stepIndex}:${stat.kind}`, {
      stepIndex: stat.stepIndex, kind: stat.kind, attempts: [], stat,
    });
  }
  const accordion = useDetailAccordion([...groups.keys()], request);
  return (
    <section className="min-w-0 space-y-2" aria-label={t("workflow.metadata.title")}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-medium">{t("workflow.metadata.title")}{planName ? ` · ${planName}` : ""}</h3>
        <div className="flex flex-wrap items-center gap-2">
          {!!executions?.length && (
            <Button size="sm" variant="flat" onPress={() => setFileBrowser({})}>
              {t("workflow.files.open")}
            </Button>
          )}
          <DetailExpansionControls onChange={setAll} />
        </div>
      </div>
      {groups.size > 0 && (
        <Accordion isCompact selectionMode="multiple" className="min-w-0 px-0" {...accordion}>
          {[...groups].sort((a, b) => a[1].stepIndex - b[1].stepIndex).map(([key, group]) => {
            const attempts = [...group.attempts].sort((a, b) => a.attempt - b.attempt || a.id - b.id);
            const latest = attempts[attempts.length - 1];
            const counts = latest ?? group.stat;
            return (
              <AccordionItem
                key={key}
                title={
                  <span className="flex min-w-0 flex-wrap items-center gap-2 text-sm">
                    <span>
                      {group.stepIndex + 1}. {activityDisplayName(t, group.kind)}
                    </span>{" "}
                    <Chip size="sm" variant="flat">
                      {latest ? t(`workflow.metadata.status.${latest.status}`, { defaultValue: latest.status })
                        : t("workflow.metadata.noExecution")}
                    </Chip>{" "}
                    {!!attempts.length && <span className="text-xs text-default-500">
                      {t("workflow.metadata.attemptCount", { count: attempts.length })}
                    </span>}
                    {counts && <WorkflowStepCounts kind={group.kind} inputCount={counts.inputCount}
                      outputCount={counts.outputCount} failedCount={counts.failedCount} />}
                    {group.notes && <span className="w-full text-xs font-normal text-default-500">{group.notes}</span>}
                  </span>
                }
              >
                {attempts.length > 0 ? <NodeAttempts
                  runId={runId}
                  attempts={attempts}
                  expansion={request}
                  readOnly={readOnly}
                  onBrowseFiles={(executionId) => setFileBrowser({ executionId })}
                /> : <p className="text-xs text-default-500">{t("workflow.metadata.noExecutionHint")}</p>}
              </AccordionItem>
            );
          })}
        </Accordion>
      )}
      {!executions?.length && (
        <div className="space-y-2">
          <p className="text-xs text-default-500">{t("workflow.metadata.legacyHint")}</p>
          <WorksetResults json={legacyOutput} expansion={request} readOnly={readOnly} />
        </div>
      )}
      {fileBrowser && (
        <WorkflowArtifactBrowserModal
          runId={runId}
          executions={executions}
          initialExecutionId={fileBrowser.executionId}
          onClose={() => setFileBrowser(undefined)}
        />
      )}
    </section>
  );
}

function NodeAttempts({
  runId,
  attempts,
  expansion,
  readOnly,
  onBrowseFiles,
}: {
  runId: number;
  attempts: Execution[];
  expansion?: DetailExpansionRequest;
  readOnly: boolean;
  onBrowseFiles: (executionId: number) => void;
}) {
  const { t } = useTranslation();
  const accordion = useDetailAccordion(
    attempts.map((attempt) => String(attempt.id)),
    expansion,
  );
  return (
    <Accordion isCompact selectionMode="multiple" className="min-w-0 px-0" {...accordion}>
      {attempts.map((attempt) => (
        <AccordionItem
          key={String(attempt.id)}
          title={
            <span className="flex flex-wrap items-center gap-2 text-xs">
              <span>{t("workflow.metadata.attempt", { number: attempt.attempt })}</span>{" "}
              <span>
                {t(`workflow.metadata.resume.${attempt.resumeKind}`, {
                  defaultValue: attempt.resumeKind,
                })}
              </span>{" "}
              <span>
                {t(`workflow.metadata.status.${attempt.status}`, {
                  defaultValue: attempt.status,
                })}
              </span>
            </span>
          }
        >
          <div className="min-w-0 space-y-2 pb-2">
            <Button size="sm" variant="flat" onPress={() => onBrowseFiles(attempt.id)}>
              {t("workflow.files.openStep")}
            </Button>
            <ExecutionTiming startedAt={attempt.startedAt} completedAt={attempt.completedAt} />
            <WorkflowStepCounts
              kind={attempt.kind}
              inputCount={attempt.inputCount}
              outputCount={attempt.outputCount}
              failedCount={attempt.failedCount}
            />
            {attempt.waitReason && (
              <p className="break-words text-xs text-secondary-600">
                {t("workflow.resume.waitingFor", {
                  reason: t(`workflow.waitReason.${attempt.waitReason}`, {
                    defaultValue: attempt.waitReason,
                  }),
                })}
              </p>
            )}
            {attempt.outputs.map((block) => (
              <WorkflowOutputBlock
                key={block.key}
                runId={runId}
                block={block}
                expansion={expansion}
                readOnly={readOnly}
              />
            ))}
            {!attempt.outputs.length && (
              <p className="text-xs text-default-500">{t("workflow.metadata.noOutput")}</p>
            )}
            {attempt.outputsTruncated && (
              <p className="text-xs text-warning-600">{t("workflow.metadata.truncated")}</p>
            )}
          </div>
        </AccordionItem>
      ))}
    </Accordion>
  );
}

export function WorkflowOutputBlock({
  runId,
  block,
  expansion,
  readOnly,
  compact,
}: {
  runId: number;
  block: Block;
  expansion?: DetailExpansionRequest;
  readOnly: boolean;
  compact?: boolean;
}) {
  const { t } = useTranslation();
  const kindTitle = ["artifact", "remote", "task", "scalar", "warning"].includes(block.kind)
    ? t(`workflow.metadata.kind.${block.kind}`)
    : t("workflow.metadata.unknown");
  const fallbackTitle = block.label || kindTitle;
  const translatedTitle = block.labelKey
    ? t(block.labelKey, { defaultValue: fallbackTitle })
    : fallbackTitle;
  const title = translatedTitle === block.labelKey ? fallbackTitle : translatedTitle;
  let content;
  switch (block.kind) {
    case "artifact":
      content = (
        <>
          {block.directory && !block.artifactId && (
            <WorkflowFileLocation
              path={block.directory}
              available={false}
              label={t("workflow.location.recordedDirectory")}
            />
          )}
          <div className="flex flex-wrap gap-2 text-xs text-default-500">
            {block.fileCount != null && (
              <span>{t("workflow.metadata.files", { count: block.fileCount })}</span>
            )}
            {block.totalBytes != null && <span>{bytes(block.totalBytes)}</span>}
            {block.state && (
              <span>
                {block.state === "reused"
                  ? t("cloudDownload.execution.reused")
                  : block.cleanupReceiptId && block.state === "quarantined"
                  ? t("cloudDownload.cleanup.status.quarantined")
                  : t(`workflow.metadata.artifactState.${block.state}`, {
                      defaultValue: block.state,
                    })}
              </span>
            )}
          </div>
          {block.artifactId && (
            <ArtifactFiles
              key={`${runId}:${block.artifactId}`}
              runId={runId}
              artifactId={block.artifactId}
              recordedDirectory={block.directory}
              compact={compact}
              expansion={expansion}
            />
          )}
          {!compact && block.operationId && (
            <p className="break-all text-xs text-default-500">
              {t("workflow.metadata.operation")}: {block.operationId}
            </p>
          )}
          {block.cleanupReceiptId && (
            <CleanupReceipt
              readOnly={readOnly}
              key={block.cleanupReceiptId}
              receiptId={block.cleanupReceiptId}
              status={block.cleanupStatus}
            />
          )}
        </>
      );
      break;
    case "remote":
      content = (
        <dl className="space-y-1 break-all text-xs">
          <div>
            <dt className="text-default-500">{t("workflow.metadata.provider")}</dt>
            <dd>
              {[block.providerId, block.methodId, block.engineId].filter(Boolean).join(" · ") ||
                "—"}
            </dd>
          </div>
          {block.remotePath && (
            <div>
              <dt className="text-default-500">{t("workflow.metadata.remotePath")}</dt>
              <dd>{block.remotePath}</dd>
            </div>
          )}
          {block.url && (
            <div>
              <dt className="text-default-500">{t("workflow.metadata.url")}</dt>
              <dd>{block.url}</dd>
            </div>
          )}
        </dl>
      );
      break;
    case "task":
      content = (
        <div className="space-y-1 break-all text-xs">
          <p>
            {block.taskKind || t("workflow.metadata.kind.task")}: {block.taskId || "—"}
          </p>
          {block.taskKind === "download" && block.taskId && (
            <Button
              as="a"
              href={`#/downloader?id=${encodeURIComponent(block.taskId)}`}
              size="sm"
              variant="light"
            >
              {t("cloudDownload.viewTask")}
            </Button>
          )}
        </div>
      );
      break;
    case "scalar":
      content = (
        <p className="whitespace-pre-wrap break-words select-text text-xs">
          {block.labelKey === "workflow.nodeOutput.executionStatus" && block.value
            ? t(
                block.value === "excluded"
                  ? "acquisition.continueActions.excluded"
                  : `cloudDownload.execution.${block.value === "reuse" ? "reused" : block.value}`,
                { defaultValue: block.value },
              )
            : (block.value ?? "—")}
        </p>
      );
      break;
    case "warning": {
      const warningKey = `workflow.metadata.warning.${block.code}`;
      const translatedWarning = block.code ? t(warningKey, { defaultValue: "" }) : undefined;
      const warningText =
        translatedWarning && translatedWarning !== warningKey
          ? translatedWarning
          : block.code
            ? (downloadProblemSummary(block.code, t) ??
              (/^[A-Za-z][A-Za-z0-9_]{0,119}$/.test(block.code)
                ? block.code
                : t("workflow.metadata.unknownHint")))
            : block.message || t("workflow.metadata.kind.warning");
      content = (
        <p
          className={`break-words text-xs ${block.severity === "error" ? "text-danger" : block.severity === "info" ? "text-default-500" : "text-warning-600"}`}
        >
          {warningText}
        </p>
      );
      break;
    }
    default:
      content = <p className="text-xs text-default-500">{t("workflow.metadata.unknownHint")}</p>;
  }
  if (compact && block.kind === "warning") return content;
  return (
    <div className="min-w-0 space-y-2 rounded-lg border border-default-200 p-3">
      <h4 className="break-words text-xs font-medium">{title}</h4>
      {content}
    </div>
  );
}

function ArtifactFiles({
  runId,
  artifactId,
  recordedDirectory,
  compact,
  expansion,
}: {
  runId: number;
  artifactId: string;
  recordedDirectory?: string;
  compact?: boolean;
  expansion?: DetailExpansionRequest;
}) {
  const { t } = useTranslation();
  const { open, toggle } = useDetailDisclosure(expansion);
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<{ key: string; data: ArtifactPage }>();
  const [error, setError] = useState(false);
  const [retry, setRetry] = useState(0);
  const requestId = useRef(0);
  const key = JSON.stringify([runId, artifactId, page]);
  const data = result?.key === key ? result.data : undefined;
  useEffect(() => {
    const request = ++requestId.current;
    setError(false);
    setResult(undefined);
    void BApi.workflow
      .getWorkflowArtifactFiles(
        runId,
        artifactId,
        { pageIndex: page, pageSize: 100 },
        { showErrorToast: false },
      )
      .then((response) => {
        if (request !== requestId.current) return;
        if (
          response.code ||
          !response.data ||
          response.data.artifactId !== artifactId ||
          response.data.pageIndex !== page
        )
          throw new Error("ARTIFACT_FILES_UNAVAILABLE");
        setResult({ key, data: response.data });
      })
      .catch(() => {
        if (request === requestId.current) {
          setResult(undefined);
          setError(true);
        }
      });
    return () => {
      requestId.current++;
    };
  }, [runId, artifactId, page, retry]);
  return (
    <div className="min-w-0 space-y-2">
      {!compact && (
        <p className="break-all text-xs text-default-500">
          {t("workflow.metadata.artifact")}: {artifactId}
        </p>
      )}
      {data?.directory ? (
        <WorkflowFileLocation
          path={data.directory}
          available={data.state === "available"}
          label={t("workflow.metadata.directory")}
        />
      ) : recordedDirectory ? (
        <WorkflowFileLocation
          path={recordedDirectory}
          available={false}
          label={t("workflow.location.recordedDirectory")}
        />
      ) : null}
      {data?.directory && recordedDirectory && data.directory !== recordedDirectory && (
        <p className="break-all text-xs text-default-400">
          {t("workflow.location.recordedDirectory")}: {recordedDirectory}
        </p>
      )}
      {error && (
        <div role="alert" className="space-y-1 text-xs text-warning-600">
          <p>{t("workflow.metadata.filesUnavailable")}</p>
          <Button size="sm" variant="light" onPress={() => setRetry((value) => value + 1)}>
            {t("common.action.retry")}
          </Button>
        </div>
      )}
      <Button size="sm" variant="flat" onPress={toggle}>
        {t(open ? "workflow.metadata.hideFiles" : "workflow.metadata.showFiles")}
      </Button>
      {open && !error && (
        <div className="min-w-0 space-y-2" aria-label={t("workflow.metadata.fileList")}>
          {!data ? (
            <Spinner size="sm" />
          ) : (
            <>
              <p className="text-xs text-default-500">
                {t(`workflow.metadata.artifactState.${data.state}`, { defaultValue: data.state })} ·{" "}
                {t("workflow.metadata.files", { count: data.fileCount })} · {bytes(data.totalBytes)}
              </p>
              {data.state === "retired" && (
                <p className="text-xs text-default-500" role="status">
                  {t("workflow.metadata.artifactRetiredNotice")}
                </p>
              )}
              <ul className="max-h-64 space-y-2 overflow-auto text-xs">
                {data.entries.map((entry) => (
                  <li key={entry.relativePath} className="min-w-0 space-y-0.5">
                    <p className="break-all select-text">{entry.relativePath}</p>
                    <p className="text-default-500">{bytes(entry.length)}</p>
                    <p className="break-all select-text font-mono text-[10px] text-default-400">
                      SHA256 {entry.sha256}
                    </p>
                  </li>
                ))}
              </ul>
              {!data.entries.length && (
                <p className="text-xs text-default-500">{t("workflow.metadata.noFiles")}</p>
              )}
              {data.fileCount > 100 && (
                <Pagination
                  size="sm"
                  page={page}
                  total={Math.ceil(data.fileCount / 100)}
                  onChange={setPage}
                />
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
}

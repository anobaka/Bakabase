import { lazy, Suspense, useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowRunViewModel as Run } from "@/sdk/Api";
import BApi from "@/sdk/BApi";
import { WorkflowRunStatus } from "@/sdk/constants";
import { Button, Pagination, Spinner } from "@/components/bakaui";
import { WorkflowArtifactBrowserButton } from "./WorkflowArtifactBrowserModal";
import { humanFileSize } from "@/components/utils";
import TaskDirectoryExplorer from "./TaskDirectoryExplorer";
import type { TaskDirectoryContext } from "./useTaskDirectoryAccess";

const FileExplorer = lazy(() => import("@/components/FileExplorer/FileExplorer"));
const pageSize = 25;
type FilePage = NonNullable<
  Awaited<ReturnType<typeof BApi.workflow.getWorkflowArtifactDirectory>>["data"]
>;

/** The workset may contain other targets, older processing results, or both. The current wait
 * identifies the target. Delivery review pins a particular artifact, even after a nested extraction. */
export function currentProcessingArtifact(run: Run): string | undefined {
  if (
    run.status !== WorkflowRunStatus.Waiting ||
    !["cloud.fileProcessing", "files.processing", "artifacts.publication"].includes(run.waitReason ?? "") ||
    !run.waitPromptJson ||
    run.waitPromptJson.length > 1024 * 1024
  )
    return;
  let prompt: {
    targetId?: string;
    artifactId?: string;
    deliveryReview?: boolean;
    archiveId?: string;
    knownStepId?: string;
    extractionPlanJson?: string;
  };
  try {
    prompt = JSON.parse(run.waitPromptJson);
  } catch {
    return;
  }
  if (!prompt || typeof prompt.targetId !== "string" || !prompt.targetId) return;
  const node = run.planNodes?.find((node) => node.order === run.currentStepIndex);
  const attempt = (run.nodeExecutions ?? [])
    .filter(
      (item) => item.stepIndex === run.currentStepIndex && (!node || item.nodeId === node.nodeId),
    )
    .reduce<
      Run["nodeExecutions"][number] | undefined
    >((latest, next) => (!latest || next.id > latest.id ? next : latest), undefined);
  if (attempt?.status !== "waiting" || attempt.waitReason !== run.waitReason) return;
  if (run.waitReason === "artifacts.publication" && attempt.kind !== "fs.publishArtifacts") return;
  if (run.waitReason === "files.processing") {
    // The local processor registers the exact normalized input on the suspended work item.
    // A recursive password request can refer to a deeper archive without its own artifact.
    if (
      attempt.kind !== "fs.processArtifacts" ||
      attempt.inputCount !== 1 ||
      attempt.outputsTruncated ||
      prompt.archiveId
    )
      return;
    const ids = new Set(
      (attempt.outputs ?? [])
        .filter((b) => b.kind === "artifact" && b.key === "artifact.inputId" && b.artifactId)
        .map((b) => b.artifactId),
    );
    return ids.size === 1 ? Array.from(ids)[0] : undefined;
  }
  const prefix = prompt.targetId + ":";
  const roles = ["download", "processed", "continuation", "published"].map((role) => prefix + role);
  const matching = (blocks: typeof attempt.inputs) =>
    blocks.filter((b) => b.kind === "artifact" && roles.includes(b.key) && !!b.artifactId);
  if (prompt.deliveryReview === true) {
    return typeof prompt.artifactId === "string" &&
      matching([...(attempt.inputs ?? []), ...(attempt.outputs ?? [])]).some(
        (b) => b.artifactId === prompt.artifactId,
      )
      ? prompt.artifactId
      : undefined;
  }
  if (!attempt.inputsRecorded || attempt.inputsTruncated || prompt.archiveId) return;
  if (prompt.knownStepId) {
    // A later instruction can consume private step intermediates. A download manifest alone
    // cannot describe that instruction's input files.
    try {
      const step = JSON.parse(prompt.extractionPlanJson ?? "{}").steps?.find(
        (s: { id?: string }) => s.id === prompt.knownStepId,
      );
      const refs = step?.inputs ?? [step?.input ?? "download"];
      if (!step || !Array.isArray(refs) || refs.length !== 1 || refs[0] !== "download") return;
    } catch {
      return;
    }
  }
  const inputs = new Set(matching(attempt.inputs ?? []).map((block) => block.artifactId));
  return inputs.size === 1 ? Array.from(inputs)[0] : undefined;
}

export default function CurrentProcessingFiles({ run }: { run: Run }) {
  const artifactId = currentProcessingArtifact(run);
  const { t } = useTranslation();
  const fallback = <WorkflowArtifactBrowserButton runId={run.id} executions={run.nodeExecutions} />;
  return artifactId ? (
    <ProcessingFileList
      key={`${run.id}:${artifactId}`}
      runId={run.id}
      artifactId={artifactId}
      fallback={fallback}
    />
  ) : (
    <div className="flex flex-wrap items-center gap-2 text-sm text-default-500">
      {t("workflow.processing.inputFiles.missing")}
      {fallback}
    </div>
  );
}

/** Inspect the directory being selected for a fresh attempt, including manual edits. */
export function ProcessingDirectoryFiles({ path, taskContext }: { path: string; taskContext?: TaskDirectoryContext }) {
  const { t } = useTranslation();
  return <section aria-label={t("workflow.processing.inputFiles.title")} className="min-w-0 space-y-1">
    {!taskContext && <h4 className="text-sm font-medium">{t("workflow.processing.inputFiles.title")}</h4>}
    <div className="min-w-0">
      {taskContext ? <TaskDirectoryExplorer path={path} context={taskContext} maxContentHeight={288}
        heading={<h4 className="shrink-0 text-sm font-medium text-foreground">{t("workflow.processing.inputFiles.title")}</h4>} /> : <Suspense fallback={<Spinner size="sm" />}>
        <FileExplorer rootPath={path} maxContentHeight={288} expandable capabilities={["enter-directory"]}
          keyboard={false} watchChanges={false} selectable="disabled" />
      </Suspense>}
    </div>
  </section>;
}

/** Read the persisted manifest of this exact input. Never scan or alter the input directory. */
export function ProcessingFileList({
  runId,
  artifactId,
  fallback,
}: {
  runId: number;
  artifactId: string;
  fallback?: ReactNode;
}) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [directory, setDirectory] = useState<string>();
  const [retry, setRetry] = useState(0);
  const [result, setResult] = useState<{ key: string; data?: FilePage }>();
  const key = JSON.stringify([runId, artifactId, directory, page, retry]);
  const current = result?.key === key ? result : undefined;
  useEffect(() => {
    const controller = new AbortController();
    void BApi.workflow
      .getWorkflowArtifactDirectory(
        runId,
        artifactId,
        { directory, pageIndex: page, pageSize, locateSingleFile: directory === undefined },
        {
          showErrorToast: false,
          sensitiveResponse: true,
          signal: controller.signal,
        },
      )
      .then((response) => {
        if (controller.signal.aborted) return;
        const data = response.data;
        if (response.code || !data || data.artifactId !== artifactId || data.pageIndex !== page || (directory !== undefined && data.relativeDirectory !== directory)) {
          setResult({ key });
          return;
        }
        setResult({ key, data });
      })
      .catch(() => {
        if (!controller.signal.aborted) setResult({ key });
      });
    return () => controller.abort();
  }, [runId, artifactId, directory, page, key]);
  const data = current?.data;
  const navigate = useCallback((next: string) => {
    setDirectory(next);
    setPage(1);
  }, []);
  const loadChildren = useCallback(async (relativeDirectory: string, signal: AbortSignal) => {
    const response = await BApi.workflow.getWorkflowArtifactDirectory(runId, artifactId,
      { directory: relativeDirectory, pageIndex: 1, pageSize },
      { sensitiveResponse: true, showErrorToast: false, signal });
    const listing = response.data;
    if (response.code || !listing || listing.artifactId !== artifactId || listing.relativeDirectory !== relativeDirectory || listing.pageIndex !== 1)
      throw new Error(t("workflow.metadata.filesUnavailable"));
    return listing;
  }, [runId, artifactId, t]);
  const directoryPage = useMemo(() => data && ({
    directory: data.relativeDirectory, entries: data.entries, onNavigate: navigate, loadChildren,
  }), [data, navigate, loadChildren]);
  return (
    <section aria-label={t("workflow.processing.inputFiles.title")} className="min-w-0 space-y-1">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <h4 className="font-medium">{t("workflow.processing.inputFiles.title")}</h4>
        {data && (
          <span className="text-xs text-default-500">
            {t("workflow.metadata.files", { count: data.fileCount })} ·{" "}
            {humanFileSize(data.totalBytes)}
          </span>
        )}
      </div>
      {!current ? (
        <Spinner size="sm" aria-label={t("common.state.loading")} />
      ) : !data ? (
        <div role="alert" className="flex flex-wrap items-center gap-2 text-xs text-warning-600">
          {t("workflow.metadata.filesUnavailable")}
          <Button size="sm" variant="light" onPress={() => setRetry((value) => value + 1)}>
            {t("common.action.retry")}
          </Button>
          {fallback}
        </div>
      ) : (
        <>
          {data.state !== "available" && (
            <div
              role="status"
              className="flex flex-wrap items-center gap-2 text-xs text-warning-600"
            >
              {t(`workflow.metadata.artifactState.${data.state}`, {
                defaultValue: t("workflow.processing.inputFiles.unconfirmed"),
              })}
              {fallback}
            </div>
          )}
          {data.entries.length === 0 ? (
            <p className="text-xs text-default-500">{t("workflow.metadata.noFiles")}</p>
          ) : (
            <Suspense fallback={<Spinner size="sm" />}>
              {directoryPage && (
                <div className="min-w-0">
                  <FileExplorer recordedDirectory={directoryPage} maxContentHeight={288} />
                </div>
              )}
            </Suspense>
          )}
          {data.childCount > pageSize && (
            <Pagination
              size="sm"
              page={page}
              total={Math.ceil(data.childCount / pageSize)}
              onChange={setPage}
            />
          )}
        </>
      )}
    </section>
  );
}

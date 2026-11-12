import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowRunViewModel as Run } from "@/sdk/Api";

import { lazy, Suspense, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { FolderOpenOutlined } from "@ant-design/icons";

import { activityDisplayName } from "./displayNames";
import TaskDirectoryExplorer from "./TaskDirectoryExplorer";
import type { TaskDirectoryContext } from "./useTaskDirectoryAccess";

import {
  Accordion,
  AccordionItem,
  Button,
  Chip,
  Modal,
  Pagination,
  Select,
  Spinner,
  Tab,
  Tabs,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import FilePathValue from "@/components/FilePathValue";
import DeliveryLayoutPreview from "@/components/FileArtifacts/DeliveryLayoutPreview";

const FileExplorer = lazy(() => import("@/components/FileExplorer/FileExplorer"));
const FileSystemSelectorPanel = lazy(
  () => import("@/components/FileSystemSelector/components/Panel"),
);

type Execution = NonNullable<Run["nodeExecutions"]>[number];
type Block = Execution["outputs"][number];
// Optional fields keep history from earlier versions distinguishable from a recorded empty input.
export type FileExecution = Omit<Execution, "inputs" | "inputsRecorded" | "inputsTruncated"> & {
  inputs?: Block[];
  inputsRecorded?: boolean;
  inputsTruncated?: boolean;
};
type ArtifactPage = NonNullable<
  Awaited<ReturnType<typeof BApi.workflow.getWorkflowArtifactFiles>>["data"]
>;
type Direction = "input" | "output";
export type WorkflowArtifactBrowserProps = {
  visible?: boolean;
  onClose: () => void;
  runId?: number;
  executions?: FileExecution[] | null;
  initialExecutionId?: number;
  initialArtifactId?: string;
  initialDirection?: Direction;
  title?: ReactNode;
  location?: { path: string; label?: ReactNode };
  /** Allow the caller to manage the current server-side directory in place. */
  allowFileOperations?: boolean;
  /** Only a current task directory can expose manual operations; manifests stay historical. */
  taskDirectory?: TaskDirectoryContext;
};

const bytes = (value: number) => {
  if (!Number.isFinite(value) || value < 0) return "—";
  const unit = value < 1024 ? 0 : Math.min(4, Math.floor(Math.log(value) / Math.log(1024)));

  return `${unit ? (value / 1024 ** unit).toFixed(1) : value} ${["B", "KiB", "MiB", "GiB", "TiB"][unit]}`;
};
const groupKey = (node: FileExecution) => JSON.stringify([node.stepIndex, node.nodeId]);

export function groupFileExecutions(executions: FileExecution[]) {
  const groups = new Map<string, FileExecution[]>();

  for (const node of executions) {
    const key = groupKey(node);

    groups.set(key, [...(groups.get(key) ?? []), node]);
  }

  return [...groups.entries()]
    .map(([key, attempts]) => ({
      key,
      attempts: [...attempts].sort((a, b) => a.attempt - b.attempt || a.id - b.id),
    }))
    .sort((a, b) => a.attempts[0].stepIndex - b.attempts[0].stepIndex);
}

/** A single read-only file workspace. Artifact manifests, never neighboring folders, define membership. */
export default function WorkflowArtifactBrowserModal({
  visible = true,
  onClose,
  runId,
  executions = [],
  initialExecutionId,
  initialArtifactId,
  initialDirection = "output",
  title,
  location,
  allowFileOperations = false,
  taskDirectory,
}: WorkflowArtifactBrowserProps) {
  const { t } = useTranslation();
  const groups = useMemo(() => groupFileExecutions(executions ?? []), [executions]);
  const [executionId, setExecutionId] = useState(initialExecutionId);
  const [direction, setDirection] = useState<Direction>(initialDirection);
  const [artifactKey, setArtifactKey] = useState<string>();

  useEffect(() => {
    if (!visible) return;
    setExecutionId(initialExecutionId);
    setDirection(initialDirection);
    setArtifactKey(undefined);
  }, [visible, runId, initialExecutionId, initialDirection, initialArtifactId]);
  useEffect(() => {
    if (visible && executionId == null) {
      const latest = groups.at(-1)?.attempts.at(-1);

      if (latest) setExecutionId(latest.id);
    }
  }, [visible, executionId, groups]);
  const execution =
    (executions ?? []).find((node) => node.id === executionId) ?? groups.at(-1)?.attempts.at(-1);
  const selectedGroup = execution
    ? groups.find((group) => group.key === groupKey(execution))
    : undefined;
  const blocks = direction === "input" ? (execution?.inputs ?? []) : (execution?.outputs ?? []);
  const artifacts = blocks.filter((block) => block.kind === "artifact");
  const artifact =
    artifacts.find((block) => block.key === artifactKey) ??
    artifacts.find((block) => block.artifactId === initialArtifactId) ??
    artifacts[0];
  const blockLabel = (block: Block) =>
    block.labelKey
      ? t(block.labelKey, { defaultValue: block.label || t("workflow.metadata.kind.artifact") })
      : block.label || t("workflow.metadata.kind.artifact");
  const chooseExecution = (id: number) => {
    setExecutionId(id);
    setArtifactKey(undefined);
  };

  return (
    <Modal
      classNames={{
        base: "h-[85dvh] max-h-[calc(100dvh-2rem)]",
        body: "min-h-0 flex-1 overflow-hidden",
      }}
      footer={
        <Button variant="light" onPress={onClose}>
          {t("common.action.close")}
        </Button>
      }
      scrollBehavior="inside"
      size="7xl"
      title={
        title ??
        (runId ? t("workflow.files.title", { id: runId }) : t("workflow.files.folderTitle"))
      }
      visible={visible}
      onClose={onClose}
    >
      {location ? (
        <div className="flex min-h-0 flex-1 flex-col gap-2">
          {location.label && <p className="text-sm font-medium">{location.label}</p>}
          {!taskDirectory && <p className="text-xs text-default-500">
            {t(
              allowFileOperations
                ? "workflow.files.directoryManageNotice"
                : "workflow.files.directoryNotice",
            )}
          </p>}
          <DirectoryDetails path={location.path} />
          {taskDirectory ? <TaskDirectoryExplorer path={location.path} context={taskDirectory} /> : allowFileOperations ? (
            <ManagedDirectory path={location.path} onClose={onClose} />
          ) : (
            <ReadonlyDirectory path={location.path} />
          )}
        </div>
      ) : (
        <div className="flex min-h-0 flex-1 flex-col gap-3 md:flex-row">
          <nav
            aria-label={t("workflow.files.steps")}
            className="flex max-h-32 shrink-0 flex-col gap-1 overflow-auto md:max-h-none md:w-56 md:border-r md:border-default-200 md:pr-3"
          >
            {groups.map((group) => {
              const node = group.attempts.at(-1)!;

              return (
                <Button
                  key={group.key}
                  aria-pressed={selectedGroup?.key === group.key}
                  className="h-auto min-h-10 shrink-0 justify-start whitespace-normal py-2 text-left"
                  color={selectedGroup?.key === group.key ? "primary" : "default"}
                  size="sm"
                  variant={selectedGroup?.key === group.key ? "flat" : "light"}
                  onPress={() => chooseExecution(node.id)}
                >
                  <span className="min-w-0 break-words">
                    {node.stepIndex + 1}. {activityDisplayName(t, node.kind)}
                  </span>
                </Button>
              );
            })}
          </nav>
          <div className="flex min-h-0 min-w-0 flex-1 flex-col gap-3">
            {!execution ? (
              <p className="text-sm text-default-500">{t("workflow.files.noRecordedSteps")}</p>
            ) : (
              <>
                <div className="flex shrink-0 flex-wrap items-center gap-2">
                  <h3 className="min-w-0 flex-1 text-sm font-medium">
                    {execution.stepIndex + 1}. {activityDisplayName(t, execution.kind)}
                  </h3>
                  <Chip size="sm" variant="flat">
                    {t(`workflow.metadata.status.${execution.status}`, {
                      defaultValue: execution.status,
                    })}
                  </Chip>
                  <Select
                    aria-label={t("workflow.files.attempt")}
                    className="w-48"
                    dataSource={(selectedGroup?.attempts ?? []).map((attempt) => ({
                      value: String(attempt.id),
                      label: t("workflow.metadata.attempt", { number: attempt.attempt }),
                      description: new Date(attempt.startedAt).toLocaleString(),
                    }))}
                    selectedKeys={[String(execution.id)]}
                    size="sm"
                    onSelectionChange={(keys) => {
                      const id = Array.from(keys)[0];

                      if (id != null) chooseExecution(Number(id));
                    }}
                  />
                </div>
                <Tabs
                  aria-label={t("workflow.files.role")}
                  selectedKey={direction}
                  size="sm"
                  onSelectionChange={(key) => {
                    setDirection(key as Direction);
                    setArtifactKey(undefined);
                  }}
                >
                  <Tab key="input" title={t("workflow.files.input")} />
                  <Tab key="output" title={t("workflow.files.output")} />
                </Tabs>
                {direction === "input" && !execution.inputsRecorded ? (
                  <p className="rounded-lg bg-default-50 p-3 text-sm text-default-500">
                    {t("workflow.files.inputsNotRecorded")}
                  </p>
                ) : !artifacts.length ? (
                  <p className="rounded-lg bg-default-50 p-3 text-sm text-default-500">
                    {t(
                      direction === "input"
                        ? "workflow.files.noInputFiles"
                        : "workflow.files.noOutputFiles",
                    )}
                  </p>
                ) : (
                  <>
                    <p className="shrink-0 text-xs text-default-500">
                      {t(
                        direction === "input"
                          ? "workflow.files.inputHint"
                          : "workflow.files.outputHint",
                      )}
                    </p>
                    {artifacts.length > 1 && (
                      <Select
                        aria-label={t("workflow.files.artifact")}
                        className="shrink-0"
                        dataSource={artifacts.map((block) => ({
                          value: block.key,
                          label: blockLabel(block),
                          description:
                            block.fileCount == null
                              ? undefined
                              : t("workflow.metadata.files", { count: block.fileCount }),
                        }))}
                        selectedKeys={artifact ? [artifact.key] : []}
                        size="sm"
                        onSelectionChange={(keys) =>
                          setArtifactKey(Array.from(keys)[0]?.toString())
                        }
                      />
                    )}
                    {artifact && runId != null && (
                      <ArtifactContent
                        key={`${runId}:${execution.id}:${direction}:${artifact.key}:${artifact.artifactId}`}
                        block={artifact}
                        runId={runId}
                        title={blockLabel(artifact) ?? ""}
                      />
                    )}
                  </>
                )}
                {(direction === "input"
                  ? execution.inputsTruncated
                  : execution.outputsTruncated) && (
                  <p className="shrink-0 text-xs text-warning-600">
                    {t("workflow.metadata.truncated")}
                  </p>
                )}
              </>
            )}
          </div>
        </div>
      )}
    </Modal>
  );
}

function DirectoryDetails({
  path,
  artifactId,
  operationId,
}: {
  path?: string;
  artifactId?: string;
  operationId?: string;
}) {
  const { t } = useTranslation();

  return (
    <Accordion isCompact className="shrink-0 px-0">
      <AccordionItem
        key="details"
        title={<span className="text-xs">{t("workflow.files.technicalDetails")}</span>}
      >
        <div className="max-h-28 space-y-2 overflow-auto text-xs text-default-500">
          {path && <FilePathValue path={path} />}
          {artifactId && (
            <p className="break-all">
              {t("workflow.metadata.artifact")}: {artifactId}
            </p>
          )}
          {operationId && (
            <p className="break-all">
              {t("workflow.metadata.operation")}: {operationId}
            </p>
          )}
        </div>
      </AccordionItem>
    </Accordion>
  );
}

function ReadonlyDirectory({ path }: { path: string }) {
  const { t } = useTranslation();

  return (
    <section
      aria-label={t("workflow.location.folderContents")}
      className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-lg border border-default-200"
    >
      <Suspense fallback={<Spinner size="sm" />}>
        <FileExplorer
          expandable
          capabilities={["enter-directory"]}
          keyboard={false}
          rootPath={path}
          selectable="disabled"
          watchChanges={false}
        />
      </Suspense>
    </section>
  );
}

function ManagedDirectory({ path, onClose }: { path: string; onClose: () => void }) {
  return (
    <section className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-lg border border-default-200">
      <Suspense fallback={<Spinner size="sm" />}>
        <FileSystemSelectorPanel mode="manage" startPath={path} onCancel={onClose} />
      </Suspense>
    </section>
  );
}

function ArtifactContent({ runId, block, title }: { runId: number; block: Block; title: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [view, setView] = useState("manifest");
  const [data, setData] = useState<ArtifactPage>();
  const [error, setError] = useState(false);
  const [retry, setRetry] = useState(0);
  const sequence = useRef(0);

  useEffect(() => {
    const request = ++sequence.current;

    setData(undefined);
    setError(false);
    if (!block.artifactId) return;
    void BApi.workflow
      .getWorkflowArtifactFiles(
        runId,
        block.artifactId,
        { pageIndex: page, pageSize: 100 },
        { showErrorToast: false },
      )
      .then((response) => {
        if (request !== sequence.current) return;
        if (
          response.code ||
          !response.data ||
          response.data.artifactId !== block.artifactId ||
          response.data.pageIndex !== page
        )
          throw new Error("ARTIFACT_FILES_UNAVAILABLE");
        setData(response.data);
      })
      .catch(() => {
        if (request === sequence.current) setError(true);
      });

    return () => {
      sequence.current++;
    };
  }, [runId, block.artifactId, page, retry]);
  if (!block.artifactId)
    return <p className="text-sm text-default-500">{t("workflow.files.manifestNotRecorded")}</p>;
  if (error)
    return (
      <div className="space-y-2 text-sm text-warning-600" role="alert">
        <p>{t("workflow.metadata.filesUnavailable")}</p>
        <Button size="sm" onPress={() => setRetry((value) => value + 1)}>
          {t("common.action.retry")}
        </Button>
      </div>
    );
  if (!data)
    return (
      <div className="flex min-h-0 flex-1 items-center justify-center">
        <Spinner />
      </div>
    );
  const available = data.state === "available" && !!data.directory;

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-2">
      <div className="flex shrink-0 flex-wrap items-center gap-2 text-sm">
        <h4 className="font-medium">{title}</h4>
        <Chip size="sm" variant="flat">
          {t(`workflow.metadata.artifactState.${data.state}`, { defaultValue: data.state })}
        </Chip>
        <span className="text-xs text-default-500">
          {t("workflow.metadata.files", { count: data.fileCount })} · {bytes(data.totalBytes)}
        </span>
      </div>
      <DirectoryDetails
        artifactId={block.artifactId}
        operationId={block.operationId}
        path={data.directory}
      />
      {data.state === "retired" && (
        <p className="shrink-0 text-sm text-default-500" role="status">
          {t("workflow.metadata.artifactRetiredNotice")}
        </p>
      )}
      <Tabs
        aria-label={t("workflow.files.view")}
        selectedKey={view}
        size="sm"
        onSelectionChange={(key) => setView(String(key))}
      >
        <Tab key="manifest" title={t("workflow.files.manifest")} />
        <Tab key="directory" isDisabled={!available} title={t("workflow.files.directory")} />
        <Tab key="deliveryLayout" title={t("artifact.deliveryLayout.preview")} />
      </Tabs>
      {view === "deliveryLayout" ? (
        <DeliveryLayoutPreview runId={runId} artifactId={block.artifactId} />
      ) : view === "directory" && available ? (
        <>
          <p className="shrink-0 text-xs text-default-500">{t("workflow.files.directoryNotice")}</p>
          <ReadonlyDirectory path={data.directory!} />
        </>
      ) : (
        <>
          <div
            aria-label={t("workflow.metadata.fileList")}
            className="min-h-0 flex-1 overflow-auto rounded-lg border border-default-200"
          >
            {!data.entries.length ? (
              <p className="p-3 text-sm text-default-500">{t("workflow.metadata.noFiles")}</p>
            ) : (
              <ul className="divide-y divide-default-200">
                {data.entries.map((entry) => (
                  <li
                    key={entry.relativePath}
                    className="flex min-w-0 items-start gap-3 p-3 text-sm"
                  >
                    <span className="min-w-0 flex-1 select-text break-all">
                      {entry.relativePath}
                    </span>
                    <span className="shrink-0 text-xs text-default-500">{bytes(entry.length)}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>
          {data.fileCount > 100 && (
            <Pagination
              page={page}
              size="sm"
              total={Math.ceil(data.fileCount / 100)}
              onChange={setPage}
            />
          )}
        </>
      )}
    </div>
  );
}

export function WorkflowArtifactBrowserButton({
  runId,
  executions,
  initialExecutionId,
  allowFileOperations = false,
}: {
  runId: number;
  executions?: FileExecution[] | null;
  initialExecutionId?: number;
  allowFileOperations?: boolean;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  if (!executions?.length) return null;

  return (
    <>
      <Button
        size="sm"
        startContent={<FolderOpenOutlined aria-hidden />}
        variant="flat"
        onPress={() => setOpen(true)}
      >
        {t("workflow.files.open")}
      </Button>
      {open && (
        <WorkflowArtifactBrowserModal
          executions={executions}
          initialExecutionId={initialExecutionId}
          allowFileOperations={allowFileOperations}
          runId={runId}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  );
}

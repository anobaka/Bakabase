"use client";

import type { PostParserTask } from "@/core/models/PostParserTask";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineCloudDownload,
  AiOutlineCopy,
  AiOutlineDelete,
  AiOutlineDownload,
  AiOutlineFileText,
  AiOutlineHistory,
  AiOutlinePlayCircle,
  AiOutlinePlus,
  AiOutlineQuestionCircle,
  AiOutlineReload,
  AiOutlineSetting,
} from "react-icons/ai";
import * as XLSX from "xlsx";

import AddTasksModal from "./components/AddTasksModal";
import AddToAcquisitionModal from "./components/AddToAcquisitionModal";
import ConfigurationModal from "./components/ConfigurationModal";
import DownloadInfoResultRenderer from "./components/DownloadInfoResultRenderer";
import { buildExportRows, copyParserText, getDownloadInfo, getTargetResult } from "./results";

import {
  Alert,
  Button,
  Checkbox,
  Chip,
  Modal,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  toast,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import TampermonkeyInstallButton from "@/components/ThirdPartyConfig/base/TampermonkeyInstallButton";
import WorkflowRunsDrawer from "@/components/Workflow/WorkflowRunsDrawer";
import BApi from "@/sdk/BApi";
import {
  PostParserSource,
  PostParseTarget,
  PostParseTargetLabel,
  ThirdPartyId,
  WorkflowRunStatus,
} from "@/sdk/constants";
import { useThirdPartyOptionsStore } from "@/stores/options";
import { usePostParserTasksStore } from "@/stores/postParserTasks";

const activeStatuses = [WorkflowRunStatus.Pending, WorkflowRunStatus.Running];
const retryStatuses = [
  WorkflowRunStatus.Failed,
  WorkflowRunStatus.Cancelled,
  WorkflowRunStatus.Interrupted,
];
const isRunning = (task: PostParserTask) =>
  !!task.workflowRunId &&
  (task.workflowStatus == null || activeStatuses.includes(task.workflowStatus));
const hasResults = (task: PostParserTask) => Object.keys(task.results ?? {}).length > 0;

const PostParserPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const automaticallyParsing = useThirdPartyOptionsStore(
    (state) => state.data.automaticallyParsingPosts,
  );
  const tasks = usePostParserTasksStore((state) => state.tasks).filter((task) => !task.isDeleted);
  const setTasks = usePostParserTasksStore((state) => state.setTasks);
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();
  const [runs, setRuns] = useState<PostParserTask>();
  const running = tasks.some(isRunning);
  const hasPending = tasks.some((task) => !task.workflowRunId && !task.error && !hasResults(task));

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
  }, [running, setTasks]);

  const action = async (
    key: string,
    invoke: () => Promise<{ code?: number; message?: string }>,
  ) => {
    if (busy) return;
    setBusy(key);
    setError(undefined);
    try {
      const response = await invoke();

      if (response.code) throw new Error(response.message || t<string>("postParser.result.failed"));
      await refresh();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : t<string>("postParser.result.failed"));
    } finally {
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
      children: (
        <pre className="whitespace-pre-wrap break-words text-sm">{task.text || task.content}</pre>
      ),
      footer: { actions: ["cancel"] },
    });

  const renderResults = (task: PostParserTask) => (
    <div className="flex min-w-0 flex-col gap-2">
      {task.error && (
        <p className="break-words text-xs text-danger" role="alert">
          {task.error}
        </p>
      )}
      {!hasResults(task) ? (
        <span className="text-sm text-default-400">
          {t<string>(
            task.error
              ? "postParser.label.noResult"
              : isRunning(task)
                ? "postParser.label.parsing"
                : "postParser.label.pending",
          )}
        </span>
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
                  <DownloadInfoResultRenderer data={getDownloadInfo(task) ?? {}} />
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

  return (
    <div className="flex min-w-0 flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
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
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-default-50 p-3">
        <div className="flex flex-wrap items-center gap-2">
          <Button
            color="primary"
            isDisabled={!hasPending || !!busy}
            isLoading={busy === "start"}
            size="sm"
            startContent={<AiOutlinePlayCircle aria-hidden className="text-base" />}
            variant="flat"
            onPress={() => action("start", () => BApi.postParser.startAllPostParserTasks())}
          >
            {t<string>("postParser.action.start")}
          </Button>
          <Checkbox
            isDisabled={!!busy}
            isSelected={!!automaticallyParsing}
            size="sm"
            onValueChange={(value) =>
              action("automatic", async () => {
                const response = await BApi.options.patchThirdPartyOptions({
                  automaticallyParsingPosts: value,
                });

                if (response.code || !value) return response;

                return BApi.postParser.startAllPostParserTasks();
              })
            }
          >
            {t<string>("postParser.label.automaticallyParsing")}
          </Checkbox>
        </div>
        <div className="flex flex-wrap items-center gap-1">
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
        <p className="text-sm text-danger" role="alert">
          {error}
        </p>
      )}
      <div className="min-w-0 overflow-x-auto">
        <Table
          removeWrapper
          aria-label={t<string>("postParser.page.title")}
          classNames={{
            table: "table-fixed min-w-[680px]",
            td: "align-top py-4",
            wrapper: "overflow-x-auto",
          }}
        >
          <TableHeader>
            <TableColumn className="w-16">{t<string>("postParser.table.id")}</TableColumn>
            <TableColumn className="w-[30%]">{t<string>("postParser.table.target")}</TableColumn>
            <TableColumn>{t<string>("postParser.table.results")}</TableColumn>
            <TableColumn className="w-44">{t<string>("postParser.table.operations")}</TableColumn>
          </TableHeader>
          <TableBody
            emptyContent={
              <div className="py-8 text-sm text-default-500">
                {t<string>("postParser.page.empty")}
              </div>
            }
          >
            {tasks.map((task) => {
              const data = getDownloadInfo(task);
              const canImport = data?.resources?.some((resource) => !!resource.link?.trim());
              const retryable =
                task.workflowRunId &&
                task.workflowStatus != null &&
                retryStatuses.includes(task.workflowStatus);

              return (
                <TableRow key={task.id}>
                  <TableCell>
                    <span className="text-xs tabular-nums text-default-400">#{task.id}</span>
                  </TableCell>
                  <TableCell>
                    <div className="flex min-w-0 flex-col gap-2">
                      <div className="flex min-w-0 items-center gap-1">
                        {task.source === PostParserSource.SoulPlus && (
                          <ThirdPartyIcon thirdPartyId={ThirdPartyId.SoulPlus} />
                        )}
                        <span className="min-w-0 break-words font-medium">
                          {task.title ||
                            t<string>(
                              task.text ? "postParser.input.text" : "postParser.label.untitled",
                            )}
                        </span>
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
                        <Button
                          className="h-auto min-w-0 justify-start px-0 py-1"
                          color="primary"
                          size="sm"
                          variant="light"
                          onPress={() => BApi.gui.openUrlInDefaultBrowser({ url: task.link })}
                        >
                          <span className="break-all whitespace-normal text-left text-xs">
                            {task.link}
                          </span>
                        </Button>
                      )}
                      {(task.text || task.content) && (
                        <Button
                          className="w-fit"
                          size="sm"
                          startContent={<AiOutlineFileText aria-hidden />}
                          variant="light"
                          onPress={() => showContent(task)}
                        >
                          {t<string>("postParser.action.viewContent")}
                        </Button>
                      )}
                      <div className="flex flex-wrap gap-1">
                        {task.workflowStatus != null ? (
                          <Chip
                            color={
                              isRunning(task)
                                ? "primary"
                                : task.workflowStatus === WorkflowRunStatus.Success
                                  ? "success"
                                  : "default"
                            }
                            size="sm"
                            variant="flat"
                          >
                            {t<string>(
                              `workflow.runs.status.${WorkflowRunStatus[task.workflowStatus]}`,
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
                    </div>
                  </TableCell>
                  <TableCell>{renderResults(task)}</TableCell>
                  <TableCell>
                    <div className="flex flex-col items-start gap-1">
                      {canImport && (
                        <Button
                          color="primary"
                          size="sm"
                          startContent={<AiOutlineCloudDownload aria-hidden />}
                          variant="flat"
                          onPress={() => createPortal(AddToAcquisitionModal, { task })}
                        >
                          {t<string>("postParser.action.addToAcquisition")}
                        </Button>
                      )}
                      {task.workflowDefinitionId && (
                        <Button
                          size="sm"
                          startContent={<AiOutlineHistory aria-hidden />}
                          variant="light"
                          onPress={() => setRuns(task)}
                        >
                          {t<string>("postParser.action.viewRuns")}
                        </Button>
                      )}
                      <div className="flex items-center gap-1">
                        {retryable ? (
                          <Button
                            isDisabled={!!busy}
                            isLoading={busy === `retry-${task.id}`}
                            size="sm"
                            startContent={<AiOutlineReload aria-hidden />}
                            variant="light"
                            onPress={() =>
                              action(`retry-${task.id}`, () =>
                                BApi.postParser.retryPostParserTaskWorkflow(task.id),
                              )
                            }
                          >
                            {t<string>("postParser.action.retry")}
                          </Button>
                        ) : null}
                        {(hasResults(task) || task.error || task.workflowRunId) &&
                          !isRunning(task) && (
                            <Button
                              isDisabled={!!busy}
                              isLoading={busy === `reparse-${task.id}`}
                              size="sm"
                              variant="light"
                              onPress={() =>
                                action(`reparse-${task.id}`, async () => {
                                  const response = await BApi.postParser.reParsePostParserTask(
                                    task.id,
                                  );

                                  return response.code
                                    ? response
                                    : BApi.postParser.startAllPostParserTasks();
                                })
                              }
                            >
                              {t<string>("postParser.action.reParse")}
                            </Button>
                          )}
                        <Button
                          isIconOnly
                          aria-label={t<string>("postParser.action.delete")}
                          color="danger"
                          isDisabled={!!busy}
                          size="sm"
                          variant="light"
                          onPress={() =>
                            action(`delete-${task.id}`, () =>
                              BApi.postParser.deletePostParserTask(task.id),
                            )
                          }
                        >
                          <AiOutlineDelete aria-hidden className="text-base" />
                        </Button>
                      </div>
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
      {runs?.workflowDefinitionId && (
        <WorkflowRunsDrawer
          isOpen
          triggerKind="postParser.manual"
          workflowDefinitionId={runs.workflowDefinitionId}
          workflowName={t<string>("postParser.label.execution", { id: runs.workflowRunId })}
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

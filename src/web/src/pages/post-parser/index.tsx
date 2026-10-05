("use client");

import type { ReactNode } from "react";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineCloudDownload,
  AiOutlineCopy,
  AiOutlineDelete,
  AiOutlineDownload,
  AiOutlineFileText,
  AiOutlineHistory,
  AiOutlineLink,
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
import AddToAcquisitionModal from "./components/AddToAcquisitionModal";
import ConfigurationModal from "./components/ConfigurationModal";
import DownloadInfoResultRenderer from "./components/DownloadInfoResultRenderer";
import TaskList from "./components/TaskList";
import Tooltip from "./components/PostParserTooltip";
import TaskSearch from "./components/TaskSearch";
import PostDetails, { AvailabilityDetails, EvidencePopover } from "./components/PostDetails";
import LocalProcessingModal from "./components/LocalProcessingModal";
import {
  buildExportRows,
  copyParserText,
  downloadInstructions,
  getDownloadInfo,
  getTargetResult,
} from "./results";
import { buildTaskSearchText } from "./search";

import { Alert, Button, Checkbox, Chip, Modal, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import TampermonkeyInstallButton from "@/components/ThirdPartyConfig/base/TampermonkeyInstallButton";
import WorkflowRunsDrawer from "@/components/Workflow/WorkflowRunsDrawer";
import WorkflowIntegrationHint from "@/components/Workflow/WorkflowIntegrationHint";
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
  WorkflowRunStatus.Waiting,
];
const isRunning = (task: PostParserTask) =>
  !!task.workflowRunId &&
  (task.workflowStatus == null || activeStatuses.includes(task.workflowStatus));
const hasResults = (task: PostParserTask) => Object.keys(task.results ?? {}).length > 0;

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
  const storedTasks = usePostParserTasksStore((state) => state.tasks);
  const tasks = useMemo(() => storedTasks.filter((task) => !task.isDeleted), [storedTasks]);
  const setTasks = usePostParserTasksStore((state) => state.setTasks);
  const [keyword, setKeyword] = useState("");
  const search = keyword.trim().toLocaleLowerCase();
  const searchIndex = useMemo(
    () => tasks.map((task) => ({ task, text: buildTaskSearchText(task) })),
    [tasks],
  );
  const shownTasks = useMemo(
    () =>
      search
        ? searchIndex.filter(({ text }) => text.includes(search)).map(({ task }) => task)
        : tasks,
    [search, searchIndex, tasks],
  );
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
      children: <PostDetails task={task} />,
      footer: { actions: ["cancel"] },
    });

  const renderResults = (task: PostParserTask) => {
    const availability = task.availability ?? getDownloadInfo(task)?.availability;
    const reason = [...new Set([availability?.reason, task.parsingMessage].filter(Boolean))].join(
      "\n",
    );

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
                onOk: async () => {
                  const response =
                    task.workflowRunId &&
                    task.workflowStatus != null &&
                    retryStatuses.includes(task.workflowStatus)
                      ? await BApi.postParser.retryPostParserTaskWorkflow(task.id)
                      : await BApi.postParser.reParsePostParserTask(task.id);

                  if (response.code)
                    throw new Error(response.message || t("postParser.result.failed"));
                  if (
                    !task.workflowRunId ||
                    task.workflowStatus == null ||
                    !retryStatuses.includes(task.workflowStatus)
                  )
                    await BApi.postParser.startAllPostParserTasks();
                  await refresh();
                },
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
            <EvidencePopover label={t("postParser.action.details")} reason={task.parsingMessage} />
          )}
        </div>
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
  };

  const renderTask = (task: PostParserTask) => {
    const data = getDownloadInfo(task);
    const hasLinks = !!data?.resources?.some((resource) => !!resource.link?.trim());
    const locks = task.contentSnapshot?.locks?.filter((lock) => !lock.isBought) ?? [];
    const needsUnlock =
      locks.length > 0 ||
      task.parsingState === "awaitingPurchase" ||
      task.parsingState === "possiblyExpired";
    const canImport = hasLinks && !needsUnlock && data?.isComplete !== false && !task.error;
    const hasContent = !!(task.text || task.content || task.contentSnapshot);
    const state = task.error
      ? "error"
      : task.parsingState && task.parsingState !== "complete"
        ? task.parsingState
        : locks.length || data?.isComplete === false
          ? "partial"
          : task.workflowStatus === WorkflowRunStatus.Waiting
            ? "awaitingAction"
            : isRunning(task)
              ? null
              : task.parsingState;
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
                {state ? (
                  <Chip
                    color={
                      state === "complete" ? "success" : state === "error" ? "danger" : "warning"
                    }
                    size="sm"
                    variant="flat"
                  >
                    {t<string>(`postParser.state.${state}`)}
                  </Chip>
                ) : task.workflowStatus != null ? (
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
                    {t<string>(`workflow.runs.status.${WorkflowRunStatus[task.workflowStatus]}`)}
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
              <div className="flex min-w-0 items-start gap-1">
                <Button
                  className="h-auto min-w-0 justify-start overflow-hidden px-0 py-1"
                  color="primary"
                  size="sm"
                  variant="light"
                  onPress={() => BApi.gui.openUrlInDefaultBrowser({ url: task.link })}
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
            {(task.link || hasContent || (!task.workflowRunId && !hasResults(task))) && (
              <ActionGroup stage="source">
                {task.link && (
                  <Button
                    color={needsUnlock ? "warning" : "default"}
                    size="sm"
                    startContent={<AiOutlineLink aria-hidden />}
                    variant="light"
                    onPress={() => BApi.gui.openUrlInDefaultBrowser({ url: task.link })}
                  >
                    {t(
                      needsUnlock
                        ? "postParser.action.openPostToUnlock"
                        : "postParser.action.openPost",
                    )}
                  </Button>
                )}
                {hasContent && (
                  <Tooltip content={t("postParser.action.viewContent")}>
                    <Button
                      isIconOnly
                      aria-label={t("postParser.action.viewContent")}
                      className="min-w-7 w-7"
                      size="sm"
                      variant="light"
                      onPress={() => showContent(task)}
                    >
                      <AiOutlineFileText aria-hidden />
                    </Button>
                  </Tooltip>
                )}
                {!task.workflowRunId && !hasResults(task) && !task.error && (
                  <Button
                    isDisabled={!!busy}
                    size="sm"
                    startContent={<AiOutlinePlayCircle aria-hidden />}
                    variant="light"
                    onPress={() =>
                      action(`fetch-${task.id}`, async () => {
                        const response = await BApi.postParser.reParsePostParserTask(task.id);

                        return response.code ? response : BApi.postParser.startAllPostParserTasks();
                      })
                    }
                  >
                    {t("postParser.action.fetchPost")}
                  </Button>
                )}
              </ActionGroup>
            )}
            {(hasResults(task) ||
              (!isRunning(task) && (task.error || task.workflowRunId || task.contentSnapshot))) && (
              <ActionGroup stage="analysis">
                {!isRunning(task) && (
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
                        : action(`reparse-${task.id}`, async () => {
                            const response = await BApi.postParser.reParsePostParserTask(task.id);

                            return response.code
                              ? response
                              : BApi.postParser.startAllPostParserTasks();
                          })
                    }
                  >
                    {t(
                      needsUnlock
                        ? "postParser.action.refreshAfterUnlock"
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
                <Button
                  color="primary"
                  size="sm"
                  startContent={<AiOutlineCloudDownload aria-hidden />}
                  variant="light"
                  onPress={() => createPortal(AddToAcquisitionModal, { task })}
                >
                  {t("postParser.action.addToAcquisition")}
                </Button>
              </ActionGroup>
            )}
            {canImport && (
              <ActionGroup stage="processing">
                <Button
                  size="sm"
                  startContent={<AiOutlineFolderOpen aria-hidden />}
                  variant="light"
                  onPress={() => createPortal(LocalProcessingModal, { task })}
                >
                  {t("postParser.action.processLocal")}
                </Button>
              </ActionGroup>
            )}
            <ActionGroup stage="task">
              {task.workflowDefinitionId && (
                <Button
                  size="sm"
                  startContent={<AiOutlineHistory aria-hidden />}
                  variant="light"
                  onPress={() => setRuns(task)}
                >
                  {t("postParser.action.viewRuns")}
                </Button>
              )}
              <Tooltip content={t("postParser.action.delete")}>
                <Button
                  isIconOnly
                  aria-label={t("postParser.action.delete")}
                  className="min-w-7 w-7"
                  color="danger"
                  isDisabled={!!busy}
                  size="sm"
                  variant="light"
                  onPress={() =>
                    action(`delete-${task.id}`, () => BApi.postParser.deletePostParserTask(task.id))
                  }
                >
                  <AiOutlineDelete aria-hidden />
                </Button>
              </Tooltip>
            </ActionGroup>
          </div>
        </div>
      </>
    );
  };

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
            variant="light"
            onPress={() => downloadInstructions(tasks)}
          >
            {t("postParser.action.exportInstructions")}
          </Button>
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
      <TaskSearch
        shown={shownTasks.length}
        total={tasks.length}
        value={keyword}
        onChange={setKeyword}
      />
      {shownTasks.length > 0 ? (
        <TaskList renderTask={renderTask} search={search} tasks={shownTasks} />
      ) : (
        <div className="rounded-xl border border-dashed border-default-200 px-4 py-12 text-center">
          <p className="text-sm text-default-500" role="status">
            {t<string>(tasks.length ? "postParser.search.empty" : "postParser.page.empty")}
          </p>
          {tasks.length > 0 && (
            <Button className="mt-3" size="sm" variant="flat" onPress={() => setKeyword("")}>
              {t<string>("postParser.search.clear")}
            </Button>
          )}
        </div>
      )}
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

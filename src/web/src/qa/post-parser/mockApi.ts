import type { Api } from "@/sdk/Api";
import type { PostParserTask } from "@/core/models/PostParserTask";
import type { PostParserSource } from "@/sdk/constants";

import { completeResult, createFixtures } from "./fixtures";

import { PostParseTarget, UiTheme, WorkflowRunStatus } from "@/sdk/constants";
import { usePostParserTasksStore } from "@/stores/postParserTasks";

export type PreviewScenario = "all" | "waiting" | "complete" | "failure" | "empty";
interface PreviewRun {
  id: number;
  postParserTaskId: number;
  workflowDefinitionId: number;
  status: WorkflowRunStatus;
  startedAt: string;
  completedAt?: string | null;
  errorMessage?: string;
  payloadSummary?: string;
  currentStepIndex: number;
  totalSteps: number;
  inputCount: number;
  outputCount: number;
  failedItemCount: number;
  logs: never[];
  outputPreview: null;
}
let records = createFixtures();
let scenario: PreviewScenario = "all";
let generation = 0;
let runId = 2000;
const externallyUnlocked = new Set<number>();
const options = {
  app: { language: "zh-CN", uiTheme: UiTheme.Light, enableAnonymousDataTracking: false },
  thirdParty: { automaticallyParsingPosts: false },
  soulPlus: { autoBuyThreshold: 5, minimumRemainingCoins: 50, accounts: [] },
};
const clone = <T>(value: T): T => structuredClone(value);
const ok = <T>(data?: T) => Promise.resolve({ code: 0, data: clone(data) });
const asRun = (task: PostParserTask): PreviewRun => ({
  id: task.workflowRunId!,
  postParserTaskId: task.id,
  workflowDefinitionId: task.workflowDefinitionId || 1,
  status: task.workflowStatus || WorkflowRunStatus.Pending,
  startedAt: task.createdAt || new Date().toISOString(),
  completedAt: task.completedAt,
  errorMessage: task.error,
  payloadSummary: task.title,
  currentStepIndex: task.error ? 0 : task.workflowStatus === WorkflowRunStatus.Success ? 4 : 1,
  totalSteps: 4,
  inputCount: 1,
  outputCount: task.workflowStatus === WorkflowRunStatus.Success ? 1 : 0,
  failedItemCount: task.error ? 1 : 0,
  logs: [],
  outputPreview: null,
});
const seedRuns = () => {
  const current = records.filter((task) => task.workflowRunId).map(asRun);
  const completed = current.find((run) => run.postParserTaskId === 4)!;

  return [
    ...current,
    {
      ...completed,
      id: 904,
      workflowDefinitionId: 2,
      status: WorkflowRunStatus.Failed,
      startedAt: "2026-10-04T02:00:00Z",
      completedAt: "2026-10-04T02:01:00Z",
      errorMessage: "示例：上一次获取超时，随后重新解析成功。",
      currentStepIndex: 0,
      outputCount: 0,
      failedItemCount: 1,
    },
  ];
};
let runs = seedRuns();
const syncRun = (task: PostParserTask) => {
  if (!task.workflowRunId) return;
  const previous = runs.find((run) => run.id === task.workflowRunId);
  const current = asRun(task);

  current.startedAt = previous?.startedAt || new Date().toISOString();
  runs = [...runs.filter((run) => run.id !== current.id), current];
};
const searchRuns = (
  query: {
    taskId?: number;
    workflowDefinitionId?: number;
    pageIndex?: number;
    pageSize?: number;
  } = {},
) => {
  const matches = runs
    .filter((run) => query.taskId == null || run.postParserTaskId === query.taskId)
    .filter(
      (run) =>
        query.workflowDefinitionId == null ||
        run.workflowDefinitionId === query.workflowDefinitionId,
    )
    .sort((left, right) => right.id - left.id);
  const size = Math.max(1, query.pageSize || 20);
  const offset = (Math.max(1, query.pageIndex || 1) - 1) * size;

  return Promise.resolve({
    code: 0,
    totalCount: matches.length,
    data: clone(matches.slice(offset, offset + size)),
  });
};
const isActive = (task: PostParserTask) =>
  task.workflowRunId != null &&
  (task.workflowStatus === WorkflowRunStatus.Pending ||
    task.workflowStatus === WorkflowRunStatus.Running);
const isPending = (task: PostParserTask) =>
  !task.isDeleted && !task.error && task.workflowRunId == null;
const visible = () =>
  records.filter(
    (task) =>
      !task.isDeleted &&
      (scenario === "all" ||
        (scenario === "waiting" && task.workflowStatus === WorkflowRunStatus.Waiting) ||
        (scenario === "complete" && task.workflowStatus === WorkflowRunStatus.Success) ||
        (scenario === "failure" && !!task.error)),
  );
const publish = () => usePostParserTasksStore.getState().setTasks(clone(visible()));
const announce = (message: string) =>
  window.dispatchEvent(new CustomEvent("post-parser-preview-action", { detail: message }));
const update = (id: number, transform: (task: PostParserTask) => PostParserTask) => {
  records = records.map((task) => (task.id === id ? transform(clone(task)) : task));
  const current = records.find((task) => task.id === id);

  if (current) syncRun(current);
  publish();
};

export const resetPreview = () => {
  generation++;
  externallyUnlocked.clear();
  records = createFixtures();
  runs = seedRuns();
  runId = 2000;
  publish();
  announce("示例已重置，全部操作仅影响当前预览。");
};
export const setPreviewScenario = (value: PreviewScenario) => {
  scenario = value;
  publish();
};
export const getPreviewOptions = () => clone(options);
const finish = (id: number) =>
  update(id, (task) => ({
    ...task,
    error: undefined,
    workflowStatus: WorkflowRunStatus.Success,
    parsingState: "complete",
    parsingMessage: undefined,
    completedAt: new Date().toISOString(),
    contentSnapshot: task.contentSnapshot
      ? {
          ...task.contentSnapshot,
          capturedAt: new Date().toISOString(),
          locks: task.contentSnapshot.locks.map((lock) => ({ ...lock, isBought: true })),
        }
      : {
          title: task.title,
          mainHtml: "<p>已获取完整帖子，下载与解压说明已提取。</p>",
          capturedAt: new Date().toISOString(),
          scope: task.text ? "pastedText" : "firstPage",
          locks: [],
        },
    results: { [PostParseTarget.DownloadInfo]: completeResult(task.title || "新帖子") },
  }));
const retry = (id: number, reparse = false) => {
  const task = records.find((item) => item.id === id);

  if (!task || task.isDeleted) return Promise.resolve({ code: 404, message: "示例帖子不存在。" });
  if (isActive(task)) return ok();
  if (!reparse && task.workflowStatus === WorkflowRunStatus.Success) return ok();
  const epoch = generation;
  const currentRunId = reparse ? ++runId : task.workflowRunId || ++runId;
  const locked =
    task.contentSnapshot?.locks.some((lock) => !lock.isBought) && !externallyUnlocked.has(id);

  if (reparse && task.workflowRunId) {
    runs = runs.map((run) =>
      run.id === task.workflowRunId && run.status === WorkflowRunStatus.Waiting
        ? { ...run, status: WorkflowRunStatus.Cancelled, completedAt: new Date().toISOString() }
        : run,
    );
  }
  update(id, (current) => ({
    ...current,
    error: undefined,
    completedAt: undefined,
    revision: (current.revision || 1) + (reparse ? 1 : 0),
    workflowRunId: currentRunId,
    workflowDefinitionId: 1,
    workflowStatus: WorkflowRunStatus.Running,
    parsingState: "snapshotSaved",
    parsingMessage: "正在读取内容并提取下载说明…",
  }));
  window.setTimeout(() => {
    if (
      generation === epoch &&
      records.find((item) => item.id === id)?.workflowRunId === currentRunId
    ) {
      if (locked) {
        update(id, (current) => ({
          ...current,
          workflowStatus: WorkflowRunStatus.Waiting,
          parsingState:
            task.parsingState === "possiblyExpired" ? "possiblyExpired" : "awaitingPurchase",
          parsingMessage: "示例原帖仍有未解锁内容。点击原帖可模拟站外解锁，再刷新解析。",
        }));
        announce("已模拟重新检查，原帖内容尚未解锁。");

        return;
      }
      finish(id);
      announce("模拟解析完成，结果已更新。");
    }
  }, 1000);

  return ok();
};
const group = (methods: Record<string, (...args: any[]) => any>, name: string) =>
  new Proxy(methods, {
    get(target, key: string) {
      if (key in target) return target[key];
      if (key === "then") return undefined;
      if (key.endsWith("Url")) return () => "#preview";

      return (..._args: unknown[]) => {
        if (!/^(get|search|list)/i.test(key)) announce(`已模拟 ${name}.${key}，不会调用真实服务。`);

        return ok([]);
      };
    },
  });
const mockApi = {
  request: () => ok([]),
  postParser: group(
    {
      getAllPostParserTasks: () => ok(visible()),
      searchPostParserWorkflowRuns: searchRuns,
      retryPostParserTaskWorkflow: (id: number) => retry(id),
      reParsePostParserTask: (id: number) => retry(id, true),
      startAllPostParserTasks: async () => {
        for (const task of records.filter(isPending)) await retry(task.id);

        return { code: 0 };
      },
      addPostParserTasks: async (input: {
        links?: string[];
        sourceLinksMap?: Record<string, string[]>;
        text?: string;
        title?: string;
        targets?: PostParseTarget[];
      }) => {
        const links = [
          ...(input.links || []).map((link) => ({ source: 0 as PostParserTask["source"], link })),
          ...Object.entries(input.sourceLinksMap || {}).flatMap(([source, sourceLinks]) =>
            sourceLinks.map((link) => ({ source: Number(source) as PostParserSource, link })),
          ),
        ];
        const additions = Array.from(
          new Map(
            links
              .map((item) => ({ ...item, link: item.link.trim() }))
              .filter((item) => item.link)
              .map((item) => [`${item.source}:${item.link}`, item]),
          ).values(),
        ).map((item, index) => ({
          ...item,
          title: input.title || `新增帖子 ${index + 1}`,
          text: undefined as string | undefined,
        }));

        if (input.text?.trim())
          additions.push({
            source: 0,
            link: "",
            text: input.text.trim(),
            title: input.title || "粘贴的帖子内容",
          });
        const submittedIds = new Set<number>();

        for (const addition of additions) {
          const existing = addition.link
            ? records.find(
                (item) =>
                  item.link === addition.link && item.source === addition.source && !item.text,
              )
            : undefined;
          const id = existing?.id || Math.max(0, ...records.map((task) => task.id)) + 1;

          submittedIds.add(id);
          if (existing && (isPending(existing) || isActive(existing))) continue;
          if (existing?.workflowRunId)
            runs = runs.map((run) =>
              run.id === existing.workflowRunId && run.status === WorkflowRunStatus.Waiting
                ? {
                    ...run,
                    status: WorkflowRunStatus.Cancelled,
                    completedAt: new Date().toISOString(),
                  }
                : run,
            );
          records = [
            ...records.filter((task) => task.id !== id),
            {
              id,
              revision: (existing?.revision || 0) + 1,
              createdAt: existing?.createdAt || new Date().toISOString(),
              targets: input.targets || [PostParseTarget.DownloadInfo],
              autoBuyThreshold: options.soulPlus.autoBuyThreshold,
              minimumRemainingCoins: options.soulPlus.minimumRemainingCoins,
              ...addition,
            },
          ];
        }
        publish();
        if (options.thirdParty.automaticallyParsingPosts)
          for (const task of records.filter((item) => submittedIds.has(item.id) && isPending(item)))
            await retry(task.id);
        announce("帖子已添加到示例列表。");

        return { code: 0 };
      },
      deletePostParserTask: (id: number) => {
        records = records.filter((task) => task.id !== id);
        publish();

        return ok();
      },
      deleteAllPostParserTasks: () => {
        records = [];
        publish();

        return ok();
      },
      importPostParserTaskToAcquisition: (id: number, input: { resourceIndices?: number[] }) => {
        announce("已模拟加入待获取列表，没有创建真实资源或下载任务。");

        return ok({
          resourceId: 8000 + id,
          created: true,
          leadCount: input.resourceIndices?.length || 1,
        });
      },
    },
    "postParser",
  ),
  options: group(
    {
      getAppOptions: () => ok(options.app),
      getThirdPartyOptions: () => ok(options.thirdParty),
      getSoulPlusOptions: () => ok(options.soulPlus),
      patchThirdPartyOptions: async (patch: Partial<typeof options.thirdParty>) => {
        Object.assign(options.thirdParty, patch);
        (await import("@/stores/options")).useThirdPartyOptionsStore.getState().update(patch);

        return { code: 0 };
      },
      patchSoulPlusOptions: async (patch: Partial<typeof options.soulPlus>) => {
        Object.assign(options.soulPlus, patch);
        (await import("@/stores/options")).useSoulPlusOptionsStore.getState().update(patch);
        records = records.map((task) => ({
          ...task,
          autoBuyThreshold: options.soulPlus.autoBuyThreshold,
          minimumRemainingCoins: options.soulPlus.minimumRemainingCoins,
        }));
        publish();
        announce("示例设置已更新。");

        return { code: 0 };
      },
    },
    "options",
  ),
  remoteAccess: group(
    {
      getRemoteAccessContext: () =>
        ok({
          isLocal: true,
          mode: 0,
          clientMode: 0,
          serverReachable: true,
          cookieCaptureAvailable: false,
        }),
    },
    "remoteAccess",
  ),
  gui: group(
    {
      openUrlInDefaultBrowser: ({ url }: { url: string }) => {
        const task = records.find((record) => record.link === url);

        if (task?.contentSnapshot?.locks.some((lock) => !lock.isBought)) {
          externallyUnlocked.add(task.id);
          announce("已模拟在原帖解锁内容。现在点击“刷新解析”或“重试”查看完整结果。");
        } else announce("已模拟打开链接，没有访问外部站点。");

        return ok();
      },
      showOpenDialog: () => ok(["/Users/demo/Downloads/秋日场景素材包"]),
      selectDirectory: () => ok("/Users/demo/Downloads/秋日场景素材包"),
      openFileOrDirectory: () => {
        announce("已模拟打开目录，不读取本机文件。");

        return ok();
      },
    },
    "gui",
  ),
  workflow: group(
    {
      searchWorkflows: () =>
        ok([
          {
            id: 20,
            name: "按已解析说明处理本地文件",
            triggerKind: "fs.processingPlan",
            enabled: true,
          },
        ]),
      runWorkflowManually: (_id: number, _input: unknown) => {
        announce("已模拟创建本地处理任务，不修改磁盘上的文件。");

        return ok({ id: ++runId, status: WorkflowRunStatus.Pending });
      },
      searchWorkflowRuns: (id: number, query: { pageIndex?: number; pageSize?: number } = {}) =>
        searchRuns({ ...query, workflowDefinitionId: id }),
      getWorkflowTriggers: () =>
        ok([
          {
            kind: "postParser.manual",
            displayName: "帖子解析",
            sourceModule: "postParser",
            supportsManualRun: true,
            requiresManualPayload: true,
          },
          {
            kind: "fs.processingPlan",
            displayName: "本地文件处理",
            sourceModule: "fileSystem",
            supportsManualRun: true,
            requiresManualPayload: true,
          },
        ]),
    },
    "workflow",
  ),
  ai: group(
    {
      getAllAiProviders: () =>
        ok([
          {
            id: 1,
            name: "示例 AI（离线）",
            kind: 1,
            isEnabled: true,
            llmEnabled: true,
            endpoint: "https://example.invalid",
            capabilities: 1,
          },
        ]),
      getAiProviderKinds: () =>
        ok([
          {
            kind: 1,
            displayName: "示例兼容接口",
            defaultEndpoint: "https://example.invalid",
            capabilities: 1,
          },
        ]),
      getAiFeatureConfig: (feature: number) =>
        ok({
          feature,
          providerConfigId: 1,
          providerConfigName: "示例 AI（离线）",
          modelId: "preview-model",
          useDefault: false,
        }),
      getAiProviderLlmModels: () => ok([{ id: "preview-model", displayName: "Preview model" }]),
    },
    "ai",
  ),
  tool: group({ getTlsPresets: () => Promise.resolve([]) }, "tool"),
};

export default new Proxy(mockApi, {
  get(target, key: string) {
    return key in target ? target[key as keyof typeof target] : group({}, key);
  },
}) as unknown as Api<unknown>;

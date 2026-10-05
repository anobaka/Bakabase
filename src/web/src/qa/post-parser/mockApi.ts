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
const purchaseQuote = (task: PostParserTask) => {
  if (!task.contentSnapshot) return null;
  const quote = {
    eligibleLockUrls: [] as string[],
    eligibleTotal: 0,
    excludedTotal: 0,
    excludedCount: 0,
    unknownPriceCount: 0,
  };
  const groups = new Map<string, typeof task.contentSnapshot.locks>();

  for (const [index, lock] of task.contentSnapshot.locks.entries()) {
    if (lock.isBought) continue;
    const key = lock.url || `missing-url:${index}`;

    groups.set(key, [...(groups.get(key) || []), lock]);
  }
  let balance = task.contentSnapshot.balance;

  for (const group of groups.values()) {
    const { price, url } = group[0];
    const unknown =
      price == null ||
      !Number.isFinite(price) ||
      price < 0 ||
      group.some((lock) => lock.price !== price);
    const minimum = task.minimumRemainingCoins || 0;
    const canAfford =
      balance == null ? price === 0 && minimum === 0 : balance - (price || 0) >= minimum;

    if (unknown || !url || price! > (task.autoBuyThreshold || 0) || !canAfford) {
      quote.excludedCount++;
      if (unknown) quote.unknownPriceCount++;
      else quote.excludedTotal += price!;
      continue;
    }
    quote.eligibleLockUrls.push(url);
    quote.eligibleTotal += price!;
    if (balance != null) balance -= price!;
  }

  return quote;
};
const withQuote = (task: PostParserTask) => ({ ...task, purchaseQuote: purchaseQuote(task) });
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
  records
    .filter(
      (task) =>
        !task.isDeleted &&
        (scenario === "all" ||
          (scenario === "waiting" && task.workflowStatus === WorkflowRunStatus.Waiting) ||
          (scenario === "complete" && task.workflowStatus === WorkflowRunStatus.Success) ||
          (scenario === "failure" && !!task.error)),
    )
    .map(withQuote);
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
  records = createFixtures().map((task) => ({
    ...task,
    autoBuyThreshold: options.soulPlus.autoBuyThreshold,
    minimumRemainingCoins: options.soulPlus.minimumRemainingCoins,
  }));
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
  update(id, (task) => {
    const locked = task.contentSnapshot?.locks.some((lock) => !lock.isBought);

    return {
      ...task,
      error: undefined,
      workflowStatus: locked ? WorkflowRunStatus.Waiting : WorkflowRunStatus.Success,
      parsingState: locked
        ? task.availability?.status === "expired"
          ? "possiblyExpired"
          : "awaitingPurchase"
        : "complete",
      parsingMessage: locked ? "仍有超出购买限制的内容，已保留可读取部分的解析结果。" : undefined,
      completedAt: locked ? undefined : new Date().toISOString(),
      contentSnapshot: task.contentSnapshot
        ? {
            ...task.contentSnapshot,
            capturedAt: new Date().toISOString(),
          }
        : {
            title: task.title,
            mainHtml: "<p>已获取完整帖子，下载与解压说明已提取。</p>",
            capturedAt: new Date().toISOString(),
            scope: task.text ? "pastedText" : "firstPage",
            locks: [],
          },
      results: {
        [PostParseTarget.DownloadInfo]: {
          ...completeResult(task.title || "新帖子"),
          isComplete: !locked,
          warnings: locked ? ["尚有未解锁内容，部分密码或处理步骤可能缺失。"] : [],
        },
      },
    };
  });
const unlockEligible = (id: number, selectedUrls?: string[]) => {
  update(id, (task) => {
    const quote = purchaseQuote(task);
    const urls = new Set(
      quote?.eligibleLockUrls.filter((url) => !selectedUrls || selectedUrls.includes(url)),
    );
    const charged = new Set<string>();
    let cost = 0;
    const snapshot = task.contentSnapshot;

    if (!snapshot) return task;
    const locks = snapshot.locks.map((lock) => {
      if (lock.isBought || !lock.url || !urls.has(lock.url)) return lock;
      if (!charged.has(lock.url)) {
        charged.add(lock.url);
        cost += lock.price || 0;
      }

      return { ...lock, isBought: true };
    });

    return {
      ...task,
      contentSnapshot: {
        ...snapshot,
        balance: snapshot.balance == null ? snapshot.balance : snapshot.balance - cost,
        locks,
      },
    };
  });
};
const retry = (id: number, reparse = false) => {
  const task = records.find((item) => item.id === id);

  if (!task || task.isDeleted) return Promise.resolve({ code: 404, message: "示例帖子不存在。" });
  if (isActive(task)) return ok();
  if (!reparse && task.workflowStatus === WorkflowRunStatus.Success) return ok();
  const epoch = generation;
  const currentRunId = reparse ? ++runId : task.workflowRunId || ++runId;

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
      if (externallyUnlocked.has(id)) {
        update(id, (current) => ({
          ...current,
          contentSnapshot: current.contentSnapshot && {
            ...current.contentSnapshot,
            locks: current.contentSnapshot.locks.map((lock) => ({ ...lock, isBought: true })),
          },
        }));
      } else if (task.availability?.status !== "expired") {
        unlockEligible(id);
      }
      if (
        records
          .find((item) => item.id === id)
          ?.contentSnapshot?.locks.some((lock) => !lock.isBought)
      ) {
        update(id, (current) => ({
          ...current,
          workflowStatus: WorkflowRunStatus.Waiting,
          parsingState:
            task.parsingState === "possiblyExpired" ? "possiblyExpired" : "awaitingPurchase",
          parsingMessage: "示例原帖仍有未解锁内容，可在原帖购买后重新解析。",
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
const purchase = (
  id: number,
  input: { revision: number; lockUrls: string[]; maxTotalCost: number },
) => {
  const task = records.find((item) => item.id === id);

  if (!task || task.isDeleted) return Promise.resolve({ code: 404, message: "示例帖子不存在。" });
  if (isActive(task)) return ok();
  if (task.revision !== input.revision)
    return Promise.resolve({ code: 409, message: "帖子版本已变化，请重新查看购买报价。" });
  const quote = purchaseQuote(task);
  const alreadyBought =
    input.lockUrls.length > 0 &&
    input.lockUrls.every((url) => {
      const locks = task.contentSnapshot?.locks.filter((lock) => lock.url === url);

      return locks?.length && locks.every((lock) => lock.isBought);
    });

  if (alreadyBought) return ok();
  if (
    !quote?.eligibleLockUrls.length &&
    !task.contentSnapshot?.locks.some((lock) => !lock.isBought)
  )
    return ok();
  if (
    task.availability?.status !== "expired" ||
    !input.lockUrls.length ||
    input.lockUrls.some((url) => !quote?.eligibleLockUrls.includes(url))
  )
    return Promise.resolve({ code: 400, message: "购买项不符合当前购买金额或余额限制。" });
  const selectedCost = (item: PostParserTask) => {
    const prices = new Map(
      item.contentSnapshot?.locks
        .filter((lock) => !lock.isBought && lock.url && input.lockUrls.includes(lock.url))
        .map((lock) => [lock.url!, lock.price || 0]),
    );

    return [...prices.values()].reduce((sum, price) => sum + price, 0);
  };

  if (!Number.isFinite(input.maxTotalCost) || input.maxTotalCost < selectedCost(task))
    return Promise.resolve({ code: 409, message: "当前价格超出确认的总价，请重新查看购买报价。" });
  const epoch = generation;
  const currentRunId = task.workflowRunId || ++runId;
  const current = () =>
    generation === epoch && records.find((item) => item.id === id)?.workflowRunId === currentRunId;

  update(id, (item) => ({
    ...item,
    workflowRunId: currentRunId,
    workflowDefinitionId: item.workflowDefinitionId || 1,
    workflowStatus: WorkflowRunStatus.Pending,
    error: undefined,
    parsingMessage: "解锁已排队，等待发送购买请求…",
  }));
  window.setTimeout(() => {
    if (!current()) return;
    update(id, (item) => ({
      ...item,
      workflowStatus: WorkflowRunStatus.Running,
      parsingMessage: "正在购买并解锁内容…",
    }));
  }, 300);
  window.setTimeout(() => {
    if (!current()) return;
    const latest = records.find((item) => item.id === id)!;

    if (selectedCost(latest) > input.maxTotalCost) {
      update(id, (item) => ({
        ...item,
        workflowStatus: WorkflowRunStatus.Waiting,
        parsingMessage: "当前价格超出确认的总价，没有发送购买请求。",
      }));

      return;
    }
    unlockEligible(id, input.lockUrls);
    update(id, (item) => ({
      ...item,
      parsingMessage: "已解锁符合条件的内容，正在通过 AI 重新提取下载与处理说明…",
    }));
  }, 1500);
  window.setTimeout(() => {
    if (!current() || !isActive(records.find((item) => item.id === id)!)) return;
    finish(id);
    announce("示例解锁与 AI 解析完成；实际账号没有扣款。");
  }, 3000);

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
      purchasePostParserTaskContent: purchase,
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
          announce("已模拟在原帖解锁内容。现在点击“重新解析”或“重试”查看完整结果。");
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

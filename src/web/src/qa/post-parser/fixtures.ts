import type { PostContentSnapshot, PostParserTask } from "@/core/models/PostParserTask";

import { PostParserSource, PostParseTarget, WorkflowRunStatus } from "@/sdk/constants";

export const samplePlan = {
  requirement: "required" as const,
  steps: [
    {
      id: "rename-outer",
      op: "renameExtension",
      input: "download",
      selector: "*.bak",
      extension: ".7z",
    },
    { id: "extract-outer", op: "extractArchive", input: "rename-outer", password: "autumn2026" },
    {
      id: "rename-inner",
      op: "renameExtension",
      input: "extract-outer",
      selector: "*.data",
      extension: ".zip",
    },
    { id: "extract-inner", op: "extractArchive", input: "rename-inner", password: "maple-leaf" },
    {
      id: "rename-final",
      op: "renameExtension",
      input: "extract-inner",
      selector: "*.bin",
      extension: ".rar",
    },
    { id: "extract-final", op: "extractArchive", input: "rename-final", password: "scene-final" },
    {
      id: "name-readme",
      op: "renameFile",
      input: "extract-final",
      selector: "README.txt",
      targetName: "使用说明.txt",
    },
    {
      id: "organize-readme",
      op: "moveFile",
      input: "name-readme",
      targetDirectory: "秋日场景/说明",
    },
  ],
  evidence: [
    "下载后把 .bak 改成 .7z，用 autumn2026 解压。里面的 .data 改成 .zip，密码 maple-leaf；最后 .bin 改成 .rar，密码 scene-final。",
    "将最后解压出的 README.txt 重命名为 使用说明.txt，再移动到 秋日场景/说明 目录。",
  ],
};

export const completeResult = (title: string) => ({
  schemaVersion: 2,
  title,
  isComplete: true,
  warnings: [],
  resources: [
    {
      link: "https://pan.baidu.com/s/qa-autumn-scene",
      code: "q8m2",
      password: "autumn2026",
      driveKind: 2,
      linkHealth: {
        status: "available",
        checkedAt: "2026-10-05T02:20:00Z",
        reason: "publicFileMetadataAvailable",
      },
      extraction: samplePlan,
    },
  ],
});

const snapshot = (title: string, id: number, locked = false): PostContentSnapshot => ({
  title,
  sourceUrl: `https://www.north-plus.net/read.php?tid=${900000 + id}`,
  capturedAt: "2026-10-05T02:15:00Z",
  scope: "firstPage",
  balance: 120,
  mainHtml: `<p>分享 ${title}，百度网盘与 MEGA 两种下载方式。</p><p>提取码和解压说明在购买区。请按顺序处理，所有密码区分大小写。</p>`,
  comments: [
    {
      id: "reply-1",
      floor: "1",
      author: "木叶",
      postedAt: "2026-10-04T01:00:00Z",
      html: "<p>这个链接似乎失效了，麻烦楼主确认一下。</p>",
    },
    {
      id: "reply-2",
      floor: "2",
      author: "分享者",
      postedAt: "2026-10-05T01:00:00Z",
      html: "<p>已更新文件与解压说明，请以主楼的新地址为准。</p>",
    },
    {
      id: "reply-3",
      floor: "3",
      author: "安静的猫",
      postedAt: "2026-10-05T02:00:00Z",
      html: "<p>补档可以使用，第二层密码需要保留中间的连字符。</p>",
    },
  ],
  locks: locked
    ? [
        {
          id: `lock-${id}-1`,
          floor: "0",
          price: 8,
          isBought: false,
          url: `https://www.north-plus.net/job.php?action=buytopic&tid=${900000 + id}&pid=1`,
        },
        {
          id: `lock-${id}-2`,
          floor: "2",
          price: 15,
          isBought: false,
          url: `https://www.north-plus.net/job.php?action=buytopic&tid=${900000 + id}&pid=2`,
        },
      ]
    : [],
});

const task = (id: number, title: string): PostParserTask => ({
  id,
  title,
  source: PostParserSource.SoulPlus,
  link: `https://www.north-plus.net/read.php?tid=${900000 + id}`,
  targets: [PostParseTarget.DownloadInfo],
  revision: 1,
  createdAt: `2026-10-05T02:${String(id).padStart(2, "0")}:00Z`,
  autoBuyThreshold: 5,
  minimumRemainingCoins: 50,
});

export const createFixtures = (): PostParserTask[] => {
  const success = task(4, "秋日场景素材包 · 三层解压说明");
  const multiple = task(5, "城市环境音效合集 · 四份独立资源");
  const paid = task(2, "山间小屋插画集 · 等待购买完整说明");
  const expired = task(3, "旧版游戏地图素材 · 链接可能已经失效");
  const partial = task(6, "角色动作参考集 · 已取得部分内容");
  const failed = task(7, "摄影光照参考图集 · 获取中断");
  const ai = task(8, "开源字体与排版参考 · 等待 AI 配置");
  const paidSnapshot = snapshot(paid.title!, paid.id, true);
  const partialSnapshot = snapshot(partial.title!, partial.id, true);

  partialSnapshot.locks[0].isBought = true;

  return [
    task(1, "周末自然纹理合集 · 待获取"),
    {
      ...paid,
      workflowRunId: 1002,
      workflowDefinitionId: 1,
      workflowStatus: WorkflowRunStatus.Waiting,
      parsingState: "awaitingPurchase",
      parsingMessage: "两项内容超出自动购买阈值。已保存第一页，购买后可继续提取下载与解压说明。",
      availability: {
        status: "restored",
        evidence: ["已更新文件与解压说明，请以主楼的新地址为准。"],
        reason: "回复中出现失效反馈，作者随后确认已补档。",
      },
      contentSnapshot: paidSnapshot,
    },
    {
      ...expired,
      workflowRunId: 1003,
      workflowDefinitionId: 1,
      workflowStatus: WorkflowRunStatus.Waiting,
      parsingState: "possiblyExpired",
      parsingMessage: "发现链接失效反馈，尚未找到补档说明。已暂停自动购买，可查看原文后自行决定。",
      availability: {
        status: "expired",
        evidence: ["这个链接似乎失效了，麻烦楼主确认一下。"],
        reason: "第一页回复报告了失效，未发现后续补档证据。",
      },
      contentSnapshot: {
        ...snapshot(expired.title!, expired.id, true),
        comments: [snapshot(expired.title!, expired.id).comments![0]],
      },
      results: {
        [PostParseTarget.DownloadInfo]: {
          ...completeResult(expired.title!),
          isComplete: false,
          warnings: ["下载密码与解压说明可能仍在未购买内容中。"],
          resources: [
            {
              link: "https://mega.nz/folder/qa-old-map",
              code: null,
              extraction: { requirement: "unknown", steps: [], evidence: [] },
              linkHealth: { status: "unavailable", reason: "providerReportsShareUnavailable" },
            },
          ],
        },
      },
    },
    {
      ...success,
      workflowRunId: 1004,
      workflowDefinitionId: 1,
      workflowStatus: WorkflowRunStatus.Success,
      parsingState: "complete",
      completedAt: "2026-10-05T02:20:00Z",
      contentSnapshot: snapshot(success.title!, success.id),
      results: { [PostParseTarget.DownloadInfo]: completeResult(success.title!) },
    },
    {
      ...multiple,
      workflowRunId: 1005,
      workflowDefinitionId: 1,
      workflowStatus: WorkflowRunStatus.Success,
      parsingState: "complete",
      completedAt: "2026-10-05T02:22:00Z",
      contentSnapshot: snapshot(multiple.title!, multiple.id),
      results: {
        [PostParseTarget.DownloadInfo]: {
          schemaVersion: 2,
          isComplete: true,
          title: multiple.title,
          resources: [
            {
              link: "https://pan.baidu.com/s/qa-city-morning",
              code: "am26",
              password: "city-morning",
              driveKind: 2,
              linkHealth: {
                status: "unknown",
                reason: "accessCodeOrInteractiveVerificationRequired",
              },
              extraction: {
                requirement: "required",
                evidence: ["第一份是清晨环境声，直接用 city-morning 解压。"],
                steps: [
                  {
                    id: "extract",
                    op: "extractArchive",
                    input: "download",
                    password: "city-morning",
                  },
                ],
              },
            },
            {
              link: "https://mega.nz/file/qaRain26#sample-demo-key",
              code: null,
              driveKind: 6,
              linkHealth: { status: "available" },
              extraction: {
                requirement: "notRequired",
                evidence: ["雨声是直接可用的 WAV 文件，不需要解压。"],
                steps: [],
              },
            },
            {
              link: "https://1drv.ms/f/qa-night-market",
              code: "night26",
              driveKind: 9,
              linkHealth: { status: "unknown", reason: "pageDidNotConfirmAvailability" },
              extraction: {
                requirement: "required",
                evidence: ["第三份将 .pack 改成 .zip 后解压。"],
                steps: [
                  {
                    id: "rename",
                    op: "renameExtension",
                    input: "download",
                    selector: "*.pack",
                    extension: ".zip",
                  },
                  {
                    id: "extract",
                    op: "extractArchive",
                    input: "rename",
                    password: "night-market",
                  },
                ],
              },
            },
            {
              link: "https://drive.google.com/file/d/qa-city-sound-guide/view",
              code: null,
              linkHealth: { status: "unknown", reason: "unsupportedProvider" },
              extraction: {
                requirement: "notRequired",
                evidence: ["第四份是音效使用说明 PDF，可直接阅读。"],
                steps: [],
              },
            },
          ],
        },
      },
    },
    {
      ...partial,
      workflowRunId: 1006,
      workflowDefinitionId: 1,
      workflowStatus: WorkflowRunStatus.Waiting,
      parsingState: "partial",
      parsingMessage: "已购买主楼内容；回复中的完整解压说明尚未购买，当前结果仅供查看。",
      contentSnapshot: partialSnapshot,
      availability: {
        status: "noExpiryReported",
        evidence: [],
        reason: "当前已读取内容中未发现失效反馈。",
      },
      results: {
        [PostParseTarget.DownloadInfo]: {
          ...completeResult(partial.title!),
          isComplete: false,
          warnings: ["还有 1 项未购买内容，内层解压密码可能缺失。"],
          resources: [
            {
              link: "https://pan.baidu.com/s/qa-motion-reference",
              code: "m9r4",
              extraction: { requirement: "unknown", evidence: [], steps: [] },
              linkHealth: { status: "unknown" },
            },
          ],
        },
      },
    },
    {
      ...failed,
      workflowRunId: 1007,
      workflowDefinitionId: 1,
      workflowStatus: WorkflowRunStatus.Failed,
      error:
        "页面获取超时（30 秒）。已保留之前读取的主楼与回复，可手动重试。\n原因：目标站点暂时未返回完整页面。",
      contentSnapshot: snapshot(failed.title!, failed.id),
    },
    {
      ...ai,
      workflowRunId: 1008,
      workflowDefinitionId: 1,
      workflowStatus: WorkflowRunStatus.Waiting,
      parsingState: "awaitingAi",
      parsingMessage: "第一页内容已保存。配置帖子解析使用的 AI 模型后，可点击重试继续。",
      contentSnapshot: snapshot(ai.title!, ai.id),
    },
  ];
};

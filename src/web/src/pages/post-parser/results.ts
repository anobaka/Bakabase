import type { PostAvailability, PostParserTask } from "@/core/models/PostParserTask";

import { getDownloadUrl } from "./downloadLinks";
import { groupDownloadResources } from "./resourceDeduplication";

import { PostParseTarget, PostParseTargetLabel, PostParserSource } from "@/sdk/constants";
import { copyTextToClipboard } from "@/core/clipboard";

export interface DownloadResource {
  [key: string]: unknown;
  link?: string;
  code?: string | null;
  password?: string | null;
  driveKind?: number;
  extraction?: ExtractionPlan | null;
  linkHealth?: {
    status: "available" | "unavailable" | "unknown";
    reason?: string | null;
    checkedAt?: string;
  } | null;
}

export interface ExtractionPlan {
  requirement: "required" | "notRequired" | "unknown";
  steps: {
    id: string;
    op: "renameExtension" | "renameFile" | "moveFile" | "extractArchive";
    input: string;
    selector?: string | null;
    extension?: string | null;
    targetName?: string | null;
    targetDirectory?: string | null;
    password?: string | null;
  }[];
  evidence: string[];
}

export interface DownloadInfoData {
  [key: string]: unknown;
  schemaVersion?: number;
  isComplete?: boolean;
  warnings?: string[];
  availability?: PostAvailability | null;
  title?: string;
  resources?: DownloadResource[] | null;
}

export interface ParsedResult {
  data?: Record<string, unknown>;
  error?: string;
  parsedAt?: string;
}

const asRecord = (value: unknown): Record<string, unknown> | undefined =>
  value != null && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;

/** Old records may wrap a target in data/error/parsedAt; current records store the data directly. */
export function normalizeResult(value: unknown): ParsedResult | undefined {
  const record = asRecord(value);

  if (!record) return undefined;
  if ("data" in record || "error" in record || "parsedAt" in record) {
    return {
      data: asRecord(record.data),
      error: typeof record.error === "string" ? record.error : undefined,
      parsedAt: typeof record.parsedAt === "string" ? record.parsedAt : undefined,
    };
  }

  return { data: record };
}

export function getTargetResult(task: PostParserTask, target: PostParseTarget) {
  return normalizeResult(task.results?.[target] ?? task.results?.[PostParseTargetLabel[target]]);
}

export function getDownloadInfo(task: PostParserTask): DownloadInfoData | undefined {
  const data = getTargetResult(task, PostParseTarget.DownloadInfo)?.data;

  if (!data) return undefined;

  return {
    ...data,
    title: typeof data.title === "string" ? data.title : undefined,
    // Keep each original position: acquisition imports use the persisted result's indices.
    resources: Array.isArray(data.resources)
      ? data.resources.map((value) => {
          const resource = asRecord(value);

          return {
            ...resource,
            link: typeof resource?.link === "string" ? resource.link : undefined,
            code:
              typeof resource?.code === "string" || resource?.code === null
                ? resource.code
                : undefined,
            password:
              typeof resource?.password === "string" || resource?.password === null
                ? resource.password
                : undefined,
            driveKind: typeof resource?.driveKind === "number" ? resource.driveKind : undefined,
            extraction:
              resource?.extraction === null
                ? null
                : (asRecord(resource?.extraction) as unknown as ExtractionPlan | undefined),
            linkHealth:
              resource?.linkHealth === null
                ? null
                : (asRecord(resource?.linkHealth) as DownloadResource["linkHealth"]),
          };
        })
      : [],
  };
}

export function buildExportRows(tasks: PostParserTask[], targetLabel: (key: string) => string) {
  const rows: Record<string, string | number>[] = [];

  for (const task of tasks) {
    const base = {
      ID: task.id,
      Source: PostParserSource[task.source] ?? "Automatic",
      Link: task.link,
      Title: task.title ?? "",
      CreatedAt: task.createdAt ?? "",
      CompletedAt: task.completedAt ?? "",
      State: task.parsingState ?? "",
      Target: "",
      "Resource Link": "",
      "Access Code": "",
      Password: "",
      Error: task.error ?? "",
      ParsedAt: "",
    };
    const entries = Object.entries(task.results ?? {});

    if (entries.length === 0) {
      rows.push(base);
      continue;
    }

    for (const [key, value] of entries) {
      const result = normalizeResult(value);
      const data = result?.data;
      const row = {
        ...base,
        Target: targetLabel(PostParseTargetLabel[Number(key) as PostParseTarget] ?? key),
        Error: result?.error ?? task.error ?? "",
        ParsedAt: result?.parsedAt ?? "",
      };

      const resources =
        key === "DownloadInfo" || key === String(PostParseTarget.DownloadInfo)
          ? groupDownloadResources(
              getDownloadInfo({ ...task, results: { [key]: value } })?.resources ?? [],
            ).map((group) => group.resource)
          : data?.resources;

      if (Array.isArray(resources) && resources.length > 0) {
        for (const resource of resources) {
          const link = asRecord(resource);

          rows.push({
            ...row,
            Title: typeof data?.title === "string" ? data.title : base.Title,
            "Resource Link": getDownloadUrl(
              typeof link?.link === "string" ? link.link : "",
              typeof link?.code === "string" ? link.code : null,
            ),
            "Access Code": typeof link?.code === "string" ? link.code : "",
            Password: typeof link?.password === "string" ? link.password : "",
            Complete:
              data?.isComplete === false ? "false" : data?.isComplete === true ? "true" : "",
            Warnings: JSON.stringify(data?.warnings ?? []),
            Availability: JSON.stringify(data?.availability ?? null),
            "Extraction Plan": JSON.stringify(link?.extraction ?? null),
            "Link Health": JSON.stringify(link?.linkHealth ?? null),
          });
        }
      } else {
        const fields = Object.fromEntries(
          Object.entries(data ?? {}).map(([name, field]) => [
            name,
            typeof field === "object" ? JSON.stringify(field) : String(field ?? ""),
          ]),
        );

        rows.push({ ...row, ...fields });
      }
    }
  }

  return rows;
}

export const copyParserText = copyTextToClipboard;

/** Keep the complete versioned contract, including fields introduced by newer servers. */
export const buildInstructionsJson = (tasks: PostParserTask[]) =>
  JSON.stringify(
    {
      schemaVersion: 2,
      tasks: tasks.map((task) => ({
        id: task.id,
        sourceUrl: task.link,
        title: task.title,
        parsingState: task.parsingState,
        error: task.error,
        downloadInfo: (() => {
          const data = getDownloadInfo(task);

          return data
            ? {
                ...data,
                resources: groupDownloadResources(data.resources ?? []).map(({ resource }) => ({
                  ...resource,
                  link: getDownloadUrl(resource.link, resource.code),
                })),
              }
            : undefined;
        })(),
      })),
    },
    null,
    2,
  );

export function downloadInstructions(tasks: PostParserTask[]) {
  const url = URL.createObjectURL(
    new Blob([buildInstructionsJson(tasks)], { type: "application/json" }),
  );
  const anchor = document.createElement("a");

  anchor.href = url;
  anchor.download =
    tasks.length === 1 ? `post-${tasks[0].id}-instructions.json` : "post-instructions.json";
  anchor.click();
  URL.revokeObjectURL(url);
}

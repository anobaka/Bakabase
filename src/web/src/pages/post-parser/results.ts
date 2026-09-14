import type { PostParserTask } from "@/core/models/PostParserTask";

import { PostParseTarget, PostParseTargetLabel, PostParserSource } from "@/sdk/constants";

export interface DownloadResource {
  link?: string;
  code?: string | null;
  password?: string | null;
  driveKind?: number;
}

export interface DownloadInfoData {
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
    title: typeof data.title === "string" ? data.title : undefined,
    // Keep each original position: acquisition imports use the persisted result's indices.
    resources: Array.isArray(data.resources)
      ? data.resources.map((value) => {
          const resource = asRecord(value);

          return {
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

      if (Array.isArray(data?.resources) && data.resources.length > 0) {
        for (const resource of data.resources) {
          const link = asRecord(resource);

          rows.push({
            ...row,
            Title: typeof data.title === "string" ? data.title : base.Title,
            "Resource Link": typeof link?.link === "string" ? link.link : "",
            "Access Code": typeof link?.code === "string" ? link.code : "",
            Password: typeof link?.password === "string" ? link.password : "",
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

export async function copyParserText(text: string) {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    const textarea = document.createElement("textarea");

    textarea.value = text;
    textarea.style.position = "fixed";
    textarea.style.opacity = "0";
    document.body.appendChild(textarea);
    textarea.select();
    try {
      if (!document.execCommand("copy")) throw new Error("Copy failed");
    } finally {
      textarea.remove();
    }
  }
}

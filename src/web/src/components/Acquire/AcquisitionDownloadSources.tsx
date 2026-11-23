import { useState } from "react";
import { useTranslation } from "react-i18next";
import { FiLink } from "react-icons/fi";
import { Button, Popover } from "@/components/bakaui";
import { DownloadSourceIcon, downloadSourceKey, downloadSourceLabel } from "@/components/DownloadSource";
import ExternalLink from "@/components/ExternalLink";
import { copyTextToClipboard } from "@/core/clipboard";
import { RuntimeSourceEvidence } from "./PlanTask/RuntimeOverviewSummary";
import type { RuntimeOverview, RuntimeSource } from "./PlanTask/runtimeOverview";

function sourceAddress(url: string) {
  try {
    const parsed = new URL(url);
    return {
      host: parsed.hostname,
      web: ["https:", "http:"].includes(parsed.protocol),
      // Access tokens and passwords in query strings/fragments are not list labels.
      label: parsed.host ? `${parsed.host}${parsed.pathname}` : parsed.protocol.replace(/:$/, ""),
    };
  } catch {
    return { host: "", web: false, label: "" };
  }
}

function SourceLink({ source }: { source: RuntimeSource }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  const [copyFailed, setCopyFailed] = useState(false);
  const address = sourceAddress(source.url);
  return (
    <div className="space-y-1">
      {!source.selected && <p className="text-xs text-default-500">{t("acquisition.sourceDetails.alternativeSource")}</p>}
      <div className="flex min-w-0 items-start gap-2">
        {address.web ? (
          <ExternalLink href={source.url} className="min-w-0 flex-1 break-all text-xs">
            {address.label}
          </ExternalLink>
        ) : <span className="min-w-0 flex-1 break-all text-xs">{address.label || t("acquisition.sourceDetails.url")}</span>}
        {!!source.url && <Button size="sm" variant="light" className="h-6 min-w-0 shrink-0 px-2 text-xs"
          aria-label={t("acquisition.sourceDetails.copy.url")}
          onPress={async () => {
            try {
              await copyTextToClipboard(source.url);
              setCopied(true);
              setCopyFailed(false);
            } catch {
              setCopied(false);
              setCopyFailed(true);
            }
          }}>
          {t(copied ? "common.state.copied" : "acquisition.sourceDetails.copy")}
        </Button>}
      </div>
      {copyFailed && <p role="alert" className="text-xs text-danger">{t("acquisition.sources.copyFailed")}</p>}
    </div>
  );
}

/** Uses the page's existing batch snapshot, including saved sources before a run exists. */
export default function AcquisitionDownloadSources({ task, overview }: {
  task: { id: number; workflowRunId?: number | null };
  overview?: RuntimeOverview;
}) {
  const { t } = useTranslation();
  if (!overview || overview.taskId !== task.id ||
      (overview.workflowRunId ?? null) !== (task.workflowRunId ?? null) || !overview.sources?.length) return null;
  const groups = new Map<string, { sourceKey?: string; label: string; sources: RuntimeSource[] }>();
  for (const source of overview.sources) {
    const sourceKey = downloadSourceKey(source.providerId);
    const host = sourceAddress(source.url).host;
    const label = sourceKey ? downloadSourceLabel(sourceKey, t)
      : source.providerId && source.providerId !== "unknown" ? source.providerId
      : host || t("acquisition.sources.unknown");
    const key = sourceKey ?? `${source.providerId}:${host}`;
    const group = groups.get(key) ?? { sourceKey, label, sources: [] };
    group.sources.push(source);
    groups.set(key, group);
  }
  return (
    <div role="group" aria-label={t("acquisition.sources.label")} className="inline-flex flex-wrap items-center gap-1">
      {[...groups].map(([key, group]) => (
        <Popover key={`${task.id}:${task.workflowRunId ?? ""}:${key}`} placement="bottom-start" trigger={
          <Button isIconOnly size="sm" variant="light" className="h-8 min-w-8 px-1"
            aria-label={t("acquisition.sources.open", { provider: group.label, count: group.sources.length })}
            title={group.label}>
            <span aria-hidden="true" className="inline-flex items-center gap-1">
              {group.sourceKey ? <DownloadSourceIcon source={group.sourceKey} /> : <FiLink size={17} />}
              {group.sources.length > 1 && <span className="text-[10px]">{group.sources.length}</span>}
            </span>
          </Button>
        }>
          <section aria-label={group.label} className="max-h-[60vh] w-[min(28rem,85vw)] space-y-2 overflow-y-auto p-2">
            <h3 className="text-sm font-medium">{group.label}</h3>
            {group.sources.map((source, index) => (
              <RuntimeSourceEvidence key={`${source.partId}:${source.sourceId}:${index}`} source={source}
                details={<SourceLink key={source.url} source={source} />} />
            ))}
            {overview.sourcesTruncated && <p className="text-xs text-default-500">{t("acquisition.runtime.sourcesTruncated")}</p>}
          </section>
        </Popover>
      ))}
    </div>
  );
}

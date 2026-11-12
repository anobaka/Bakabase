import { Accordion, AccordionItem, Button, Chip } from "@/components/bakaui";
import { useState } from "react";
import { bytesToSize } from "@/components/utils";
import { workflowNodeDisplayName } from "@/components/Workflow/nodeDisplayNames";
import { useTranslation } from "react-i18next";
import type { AcquisitionDeletionPreview } from "./acquisitionDeletionApi";

export default function AcquisitionDeletionCleanupSummary({ cleanup, reason }: {
  cleanup: NonNullable<AcquisitionDeletionPreview["rows"][number]["cleanup"]>;
  reason: (code?: string | null) => string;
}) {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const keyOf = (group: typeof cleanup.groups[number], index: number) => `${group.artifactId}:${group.kind}:${index}`;
  return <div className="min-w-0 w-full max-w-full space-y-1">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <div className="space-y-1 text-xs text-default-600">
        <p>{t("acquisition.delete.cleanup.taskIdentified", {count: cleanup.identifiedFileCount ?? 0, size: bytesToSize(cleanup.identifiedBytes ?? 0)})}</p>
        <p>{t("acquisition.delete.cleanup.taskTotal", {
        count: cleanup.fileCount, size: bytesToSize(cleanup.bytes),
        })}</p>
        {cleanup.sizeIncomplete && <p className="text-warning-600">{t("acquisition.delete.cleanup.incompleteSize")}</p>}
      </div>
      {cleanup.groups.length > 0 && <Button size="sm" variant="light" onPress={() => setExpanded(
        expanded.size === cleanup.groups.length ? new Set() : new Set(cleanup.groups.map(keyOf)),
      )}>{t(expanded.size === cleanup.groups.length ? "acquisition.delete.cleanup.collapseAll" : "acquisition.delete.cleanup.expandAll")}</Button>}
    </div>
    {cleanup.reasonCode && <p className="text-xs text-warning-600">{reason(cleanup.reasonCode)}</p>}
    {!!cleanup.supersededRuns?.length && <div className="rounded-lg bg-warning-50 p-2 text-xs text-warning-700">
      <p>{t("acquisition.delete.cleanup.supersededHint")}</p>
      {cleanup.supersededRuns.map(run => <p key={run.runId}>{t("acquisition.delete.cleanup.supersededRun", {
        run: run.runId, revision: run.revision, replacedBy: run.replacedByRevision,
      })}</p>)}
    </div>}
    <Accordion isCompact selectionMode="multiple" className="min-w-0 max-w-full px-0"
      itemClasses={{
        base: "min-w-0 max-w-full",
        heading: "min-w-0 max-w-full",
        trigger: "min-w-0 max-w-full",
        titleWrapper: "min-w-0 flex-1",
        title: "whitespace-normal [overflow-wrap:anywhere]",
        subtitle: "whitespace-normal [overflow-wrap:anywhere]",
        content: "min-w-0 max-w-full",
      }} selectedKeys={expanded}
      onSelectionChange={keys => setExpanded(keys === "all" ? new Set(cleanup.groups.map(keyOf)) : new Set(Array.from(keys, String)))}>
      {cleanup.groups.map((group, index) => <AccordionItem
        key={keyOf(group, index)}
        aria-label={group.directory}
        title={<div className="flex min-w-0 max-w-full flex-wrap items-center gap-2 text-xs">
          {group.nodes.map(node => <span key={`${node.runId}:${node.nodeId}`} title={node.nodeId} className="min-w-0 [overflow-wrap:anywhere] font-medium">
            {workflowNodeDisplayName(t, node, t("acquisition.delete.cleanup.nodeFallback"))}
          </span>)}
          <span className={group.kind === "publishedFiles" ? "font-medium text-danger" : undefined}>{t(`acquisition.delete.cleanup.kind.${["download", "processedFiles", "intermediate", "publishedFiles"].includes(group.kind) ? group.kind : "intermediate"}`)}</span>
          <span className="text-default-500">{group.sizeUnknown ? t("acquisition.delete.cleanup.sizeUnknown") : t("acquisition.delete.cleanup.groupSize", {
            count: group.fileCount, size: bytesToSize(group.bytes),
          })}</span>
          {!group.eligible && <Chip size="sm" variant="flat" color="warning">{t("acquisition.delete.cleanup.retained")}</Chip>}
          {group.shared && <Chip size="sm" variant="flat">{t("acquisition.delete.cleanup.shared")}</Chip>}
        </div>}
        subtitle={<span className="block whitespace-normal [overflow-wrap:anywhere] text-xs text-default-500">
          {group.kind === "intermediate" ? t("acquisition.delete.cleanup.intermediateLocations") : group.directory}
        </span>}
      >
        <div className="min-w-0 max-w-full space-y-2 text-xs [overflow-wrap:anywhere]">
          <p className="whitespace-normal text-default-600">
            {group.kind === "intermediate" && <span>{t("acquisition.delete.cleanup.relatedResultDirectory")} </span>}
            {group.directory}
          </p>
          {group.nodes.map(node => <p key={`${node.runId}:${node.nodeId}`} title={node.nodeId} className="whitespace-normal text-default-500">
            {t("acquisition.delete.cleanup.node", { run: node.runId })}{" · "}
            {workflowNodeDisplayName(t, node, t("acquisition.delete.cleanup.nodeFallback"))}
          </p>)}
          {group.reasonCode && <p className={group.reasonCode === "AlreadyAbsent" ? "text-default-600" : "text-warning-600"}>{reason(group.reasonCode)}</p>}
          <div className="min-w-0 max-w-full max-h-60 overflow-y-auto divide-y divide-default-100">
            {group.files.map(file => <div key={file.path} className="grid grid-cols-[minmax(0,1fr)_auto] items-start gap-3 py-1">
              <span className="min-w-0 whitespace-normal [overflow-wrap:anywhere]">{file.path}</span>
              <span className="whitespace-nowrap text-default-500">{bytesToSize(file.bytes)}</span>
            </div>)}
          </div>
          {group.filesTruncated && <p className="text-default-500">{t("acquisition.delete.cleanup.truncated", {
            shown: group.files.length, total: group.fileCount,
          })}</p>}
        </div>
      </AccordionItem>)}
    </Accordion>
  </div>;
}

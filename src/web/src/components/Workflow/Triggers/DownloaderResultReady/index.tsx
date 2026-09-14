import type { WorkflowTriggerUI } from "../types";

import { useTranslation } from "react-i18next";

import { Select } from "@/components/bakaui";
import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";
import { DownloadResultKind } from "@/sdk/constants";

type Filter = { kinds: number[] };

const FilterForm: WorkflowTriggerUI<Filter>["FilterForm"] = ({ value, onChange }) => {
  const { t } = useTranslation();

  return (
    <div className="space-y-3">
      <p className="text-xs leading-relaxed text-default-500">
        {t("workflow.trigger.downloaderResultReady.description")}
      </p>
      <Select
        dataSource={[
          {
            value: String(DownloadResultKind.TorrentMetadata),
            label: t("workflow.trigger.downloaderResultReady.kind.torrent"),
          },
          {
            value: String(DownloadResultKind.LocalFiles),
            label: t("workflow.trigger.downloaderResultReady.kind.files"),
          },
        ]}
        description={t("workflow.trigger.downloaderResultReady.kinds.description")}
        label={t("workflow.trigger.downloaderResultReady.kinds.label")}
        selectedKeys={value.kinds.map(String)}
        selectionMode="multiple"
        size="sm"
        onSelectionChange={(keys) => onChange({ kinds: Array.from(keys).map(Number) })}
      />
    </div>
  );
};

const FilterSummary: WorkflowTriggerUI<Filter>["FilterSummary"] = ({ filter }) => {
  const { t } = useTranslation();

  return (
    <span className="text-xs text-default-500">
      {filter.kinds.length === 0
        ? t("workflow.trigger.downloaderResultReady.kinds.all")
        : filter.kinds
            .map((kind) =>
              t(
                `workflow.trigger.downloaderResultReady.kind.${kind === DownloadResultKind.TorrentMetadata ? "torrent" : "files"}`,
              ),
            )
            .join(" · ")}
    </span>
  );
};

export const DownloaderResultReadyTriggerUI: WorkflowTriggerUI<Filter> = {
  kind: "downloader.resultReady",
  displayNameKey: "workflow.trigger.downloaderResultReady.displayName",
  defaultFilter: () => ({ kinds: [] }),
  parseFilter: (json) => {
    try {
      const parsed = JSON.parse(json || "{}");

      return { kinds: Array.isArray(parsed?.kinds) ? parsed.kinds : [] };
    } catch {
      return { kinds: [] };
    }
  },
  serializeFilter: (filter) => (filter.kinds.length === 0 ? null : JSON.stringify(filter)),
  isValid: (filter) =>
    filter.kinds.every(
      (kind) =>
        kind === DownloadResultKind.TorrentMetadata || kind === DownloadResultKind.LocalFiles,
    ),
  resolveOutputItemType: () => WorkflowItemTypes.DownloaderResult,
  FilterForm,
  FilterSummary,
};

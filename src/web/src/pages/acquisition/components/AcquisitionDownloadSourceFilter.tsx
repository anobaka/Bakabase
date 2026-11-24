import { useTranslation } from "react-i18next";
import { Button, Tooltip } from "@/components/bakaui";
import { DownloadSourceIcon, downloadSourceLabel } from "@/components/DownloadSource";
import { acquisitionFilterCount, showAcquisitionFilter } from "../acquisitionFilters";
import {
  acquisitionDownloadSources,
  normalizeAcquisitionDownloadSource,
} from "../downloadSourceFilter";

export default function AcquisitionDownloadSourceFilter({
  counts,
  value,
  onChange,
}: {
  counts?: Record<string, number> | null;
  value?: string;
  onChange: (value: string | undefined) => void;
}) {
  const { t } = useTranslation();
  const selected = normalizeAcquisitionDownloadSource(value);
  const sources = acquisitionDownloadSources.filter(source =>
    showAcquisitionFilter(acquisitionFilterCount(counts, source), selected === source));
  if (!sources.length) return null;

  const choices = [
    { value: "all", label: t("downloader.filter.allSources") },
    ...sources.map(source => ({ value: source, label: downloadSourceLabel(source, t) })),
  ];
  return (
    <div
      data-acquisition-download-source-filter
      role="group"
      aria-label={t("downloader.filter.source")}
      className="flex min-w-0 max-w-full flex-wrap items-center gap-1 text-xs text-default-500"
    >
      <Tooltip content={t("acquisition.filters.sourceCountsHint")} delay={1000} placement="bottom">
        <span className="mr-1 shrink-0">{t("downloader.filter.source")}</span>
      </Tooltip>
      {choices.map((choice) => {
        const isAll = choice.value === "all";
        const isSelected = isAll ? !selected : selected === choice.value;
        const count = acquisitionFilterCount(counts, choice.value);
        return (
          <Button
            key={choice.value}
            aria-pressed={isSelected}
            aria-label={`${choice.label} ${count ?? "—"}`}
            className="h-7 min-w-0 gap-1.5 px-2 text-xs"
            color={isSelected ? "primary" : "default"}
            size="sm"
            variant={isSelected ? "flat" : "light"}
            startContent={
              !isAll && (
                <span
                  aria-hidden
                  className="flex size-4 shrink-0 items-center justify-center [&>*]:!size-4"
                >
                  <DownloadSourceIcon source={choice.value} />
                </span>
              )
            }
            onPress={() => onChange(isAll || isSelected ? undefined : choice.value)}
          >
            <span>{choice.label}</span>
            <span className="tabular-nums">{count ?? "—"}</span>
          </Button>
        );
      })}
    </div>
  );
}

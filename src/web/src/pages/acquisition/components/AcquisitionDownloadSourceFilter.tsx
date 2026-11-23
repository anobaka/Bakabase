import { useTranslation } from "react-i18next";
import { Button } from "@/components/bakaui";
import { DownloadSourceIcon, downloadSourceLabel } from "@/components/DownloadSource";
import {
  acquisitionDownloadSources,
  normalizeAcquisitionDownloadSource,
} from "../downloadSourceFilter";

export default function AcquisitionDownloadSourceFilter({
  value,
  onChange,
}: {
  value?: string;
  onChange: (value: string | undefined) => void;
}) {
  const { t } = useTranslation();
  const choices = [
    { value: "all", label: t("downloader.filter.allSources") },
    ...acquisitionDownloadSources.map((source) => ({
      value: source,
      label: downloadSourceLabel(source, t),
    })),
  ];
  const selected = normalizeAcquisitionDownloadSource(value);
  return (
    <div
      data-acquisition-download-source-filter
      role="group"
      aria-label={t("downloader.filter.source")}
      className="flex min-w-0 max-w-full flex-wrap items-center gap-1 text-xs text-default-500"
    >
      <span className="mr-1 shrink-0">{t("downloader.filter.source")}</span>
      {choices.map((choice) => {
        const isAll = choice.value === "all";
        const isSelected = isAll ? !selected : selected === choice.value;
        return (
          <Button
            key={choice.value}
            aria-pressed={isSelected}
            aria-label={choice.label}
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
            onPress={() => onChange(isAll ? undefined : choice.value)}
          >
            {choice.label}
          </Button>
        );
      })}
    </div>
  );
}

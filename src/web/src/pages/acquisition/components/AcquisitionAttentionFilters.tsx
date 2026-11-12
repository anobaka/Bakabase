import { useTranslation } from "react-i18next";
import { Button, Tooltip } from "@/components/bakaui";
import {
  acquisitionFactFilters,
  toggleAcquisitionFilter,
  type AcquisitionFilterSelection,
} from "../acquisitionFilters";

export default function AcquisitionAttentionFilters({
  counts,
  factCounts,
  value,
  onChange,
  disabled,
  matchingCount,
}: {
  counts: Record<string, number>;
  factCounts?: Record<string, number> | null;
  value: AcquisitionFilterSelection;
  onChange: (value: AcquisitionFilterSelection) => void;
  disabled?: boolean;
  matchingCount?: number;
}) {
  const { t } = useTranslation();
  return (
    <div
      role="group"
      aria-label={t("acquisition.filters.title")}
      className="flex flex-wrap items-center gap-x-4 gap-y-1"
    >
      <div role="group" aria-label={t("acquisition.attention.title")} className="flex flex-wrap items-center gap-1">
      <Tooltip content={t("acquisition.attention.countsHint")} delay={1000} placement="bottom">
        <span className="mr-1 text-xs text-default-500">{t("acquisition.attention.title")}</span>
      </Tooltip>
      {Object.entries(counts).map(([tag, count]) => (
        <Tooltip
          key={tag}
          content={t(`acquisition.attention.hint.${tag}`)}
          delay={1000}
          placement="bottom"
        >
          <Button
            aria-label={`${t(`acquisition.attention.tag.${tag}`)} ${count}`}
            aria-pressed={value.attentionTags.includes(tag)}
            className="h-7 min-w-0 gap-1.5 px-2 text-xs"
            size="sm"
            variant={value.attentionTags.includes(tag) ? "flat" : "light"}
            color={value.attentionTags.includes(tag) ? "primary" : "default"}
            isDisabled={disabled}
            onPress={() => onChange({ ...value, attentionTags: toggleAcquisitionFilter(value.attentionTags, tag) })}
          >
            <span>{t(`acquisition.attention.tag.${tag}`)}</span>
            <span className="tabular-nums">{count}</span>
          </Button>
        </Tooltip>
      ))}
      </div>
      <div role="group" aria-label={t("acquisition.filters.factsTitle")} className="flex flex-wrap items-center gap-1">
        <Tooltip content={t("acquisition.filters.factsHint")} delay={1000} placement="bottom">
          <span className="mr-1 text-xs text-default-500">{t("acquisition.filters.factsTitle")}</span>
        </Tooltip>
        {acquisitionFactFilters.map(fact => {
          const selected = value.facts.includes(fact);
          const count = factCounts?.[fact] ?? "—";
          return <Tooltip key={fact} content={t(`acquisition.filters.hint.${fact}`)} delay={1000} placement="bottom">
            <Button aria-label={`${t(`acquisition.filters.fact.${fact}`)} ${count}`} aria-pressed={selected}
              className="h-7 min-w-0 gap-1.5 px-2 text-xs" size="sm"
              variant={selected ? "flat" : "light"} color={selected ? "primary" : "default"}
              isDisabled={disabled || !factCounts}
              onPress={() => onChange({ ...value, facts: toggleAcquisitionFilter(value.facts, fact) })}>
              <span>{t(`acquisition.filters.fact.${fact}`)}</span><span className="tabular-nums">{count}</span>
            </Button>
          </Tooltip>;
        })}
      </div>
      <div className="flex items-center gap-1 text-xs text-default-500">
        <Tooltip content={t("acquisition.filters.matchAllHint")} delay={1000} placement="bottom">
          <span>{t("acquisition.filters.matchAll")}</span>
        </Tooltip>
        {!!(value.attentionTags.length || value.facts.length) && <Button size="sm" variant="light"
          className="h-7 min-w-0 px-2 text-xs" isDisabled={disabled}
          onPress={() => onChange({ attentionTags: [], facts: [] })}>{t("acquisition.filters.clear")}</Button>}
      </div>
      {value.facts.length > 1 && <span role="status"
        className={`text-xs ${matchingCount === 0 ? "text-warning-600" : "text-default-500"}`}>
        {t("acquisition.filters.matchCount", { count: matchingCount ?? "—" })}
      </span>}
    </div>
  );
}

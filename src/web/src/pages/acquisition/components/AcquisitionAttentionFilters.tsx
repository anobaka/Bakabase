import { useTranslation } from "react-i18next";
import { Button, Tooltip } from "@/components/bakaui";
import { taskTag } from "@/components/Acquire/taskTags";
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
  const tags = [
    ...acquisitionFactFilters.map(id => ({ ...taskTag("facts", id), count: factCounts?.[id] })),
    ...Object.entries(counts).map(([id, count]) => ({ ...taskTag("attentionTags", id), count })),
  ];
  const selectedCount = value.attentionTags.length + value.facts.length;

  return (
    <div role="group" aria-label={t("acquisition.filters.title")} className="flex flex-wrap items-center gap-1">
      <Tooltip content={t("acquisition.filters.countsHint")} delay={1000} placement="bottom">
        <span className="mr-1 text-xs text-default-500">{t("acquisition.filters.title")}</span>
      </Tooltip>
      {tags.map(({ kind, id, label, hint, count }) => {
        const selected = value[kind].includes(id);
        return (
          <Tooltip key={`${kind}:${id}`} content={t(hint)} delay={1000} placement="bottom">
            <Button
              aria-label={`${t(label)} ${count ?? "—"}`}
              aria-pressed={selected}
              className="h-7 min-w-0 gap-1.5 px-2 text-xs"
              size="sm"
              variant={selected ? "flat" : "light"}
              color={selected ? "primary" : "default"}
              isDisabled={disabled || count == null}
              onPress={() => onChange({ ...value, [kind]: toggleAcquisitionFilter(value[kind], id) })}
            >
              <span>{t(label)}</span>
              <span className="tabular-nums">{count ?? "—"}</span>
            </Button>
          </Tooltip>
        );
      })}
      {selectedCount > 1 && (
        <span role="status" className={`ml-1 flex gap-1 text-xs ${matchingCount === 0 ? "text-warning-600" : "text-default-500"}`}>
          <span>{t("acquisition.filters.matchAll")}</span>
          {matchingCount != null && <span>{t("acquisition.filters.matchCount", { count: matchingCount })}</span>}
        </span>
      )}
      {selectedCount > 0 && (
        <Button size="sm" variant="light" className="h-7 min-w-0 px-2 text-xs" isDisabled={disabled}
          onPress={() => onChange({ attentionTags: [], facts: [] })}>{t("acquisition.filters.clear")}</Button>
      )}
    </div>
  );
}

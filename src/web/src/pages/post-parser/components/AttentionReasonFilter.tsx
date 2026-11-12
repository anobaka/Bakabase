import { useTranslation } from "react-i18next";
import { Button } from "@/components/bakaui";
import { attentionReasonLabel } from "../attentionReasons";
import Tooltip from "./PostParserTooltip";

export default function AttentionReasonFilter({
  counts,
  value,
  onChange,
  disabled,
}: {
  counts?: Record<string, number>;
  value?: string;
  onChange: (code?: string) => void;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  const entries = Object.entries(counts ?? {})
    .filter(([, count]) => count > 0)
    .sort(([a], [b]) => a.localeCompare(b));
  if (value && !entries.some(([code]) => code === value)) entries.push([value, 0]);
  return (
    <div className="w-full space-y-1">
      <div
        aria-label={t("postParser.filters.reason")}
        role="group"
        className="flex w-full flex-wrap items-center gap-1 text-xs text-default-500"
      >
        <Tooltip content={t("postParser.filters.reasonHint")}>
          <Button size="sm" variant="light" className="h-7 min-w-0 px-2 text-xs text-default-500">
            {t("postParser.filters.reason")}
          </Button>
        </Tooltip>
        <Button
          aria-pressed={!value}
          data-attention-reason-filter="all"
          isDisabled={disabled || !counts}
          className="h-7 min-w-0 px-2 text-xs"
          color={!value ? "primary" : "default"}
          size="sm"
          variant={!value ? "flat" : "light"}
          onPress={() => onChange(undefined)}
        >
          {t("postParser.filters.anyReason")}
        </Button>
        {entries.map(([code, count]) => (
          <Button
            key={code}
            aria-pressed={value === code}
            data-attention-reason-filter={code}
            isDisabled={disabled || !counts}
            className="h-auto min-h-7 min-w-0 max-w-full gap-1.5 whitespace-normal px-2 py-1 text-xs"
            color={value === code ? "primary" : "default"}
            size="sm"
            variant={value === code ? "flat" : "light"}
            onPress={() => onChange(code)}
          >
            {attentionReasonLabel(t, code)}
            <span className="font-medium tabular-nums text-foreground">{counts ? count : "—"}</span>
          </Button>
        ))}
      </div>
    </div>
  );
}

import { useTranslation } from "react-i18next";
import { millisecondsUntil, parseServerTime } from "@/core/serverTime";
import { Button, Chip, Popover } from "@/components/bakaui";
import type { SourceHealth } from "./models";

const knownStatuses = new Set([
  "available",
  "confirmedUnavailable",
  "accessRequired",
  "temporarilyUnavailable",
  "unknown",
  "unsupported",
]);
export default function SourceHealthBadge({
  health,
  disabled = false,
}: {
  health?: SourceHealth | null;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  if (!health && !disabled) return null;
  const expired = !!health?.expiresAt && millisecondsUntil(health.expiresAt) <= 0;
  const status = !expired && knownStatuses.has(health?.status ?? "") ? health!.status : "unknown";
  const label = t(`sourceAvailability.status.${status}`);
  const evidence = Object.entries(health?.evidence ?? {}).filter(
    ([key, value]) => key !== "response" || value !== health?.code,
  );
  return (
    <div className="inline-flex flex-wrap items-center gap-1">
      {disabled && (
        <Chip size="sm" variant="flat">
          {t("sourceAvailability.disabled")}
        </Chip>
      )}
      {health && (
        <Popover
          trigger={
            <Button
              size="sm"
              variant="light"
              className="h-6 min-w-0 px-1 text-xs"
              color={
                status === "available"
                  ? "success"
                  : status === "confirmedUnavailable"
                    ? "danger"
                    : "default"
              }
              aria-label={t("sourceAvailability.healthDetails", { status: label })}
            >
              {label}
            </Button>
          }
        >
          <div className="max-w-sm space-y-2 p-2 text-xs">
            <p className="font-medium">{label}</p>
            {expired && (
              <p className="text-warning-600">{t("sourceAvailability.expiredObservation")}</p>
            )}
            {health.message && <p className="whitespace-pre-wrap break-words">{health.message}</p>}
            {evidence.length > 0 && (
              <dl className="space-y-1">
                {evidence.map(([key, value]) => (
                  <div key={key} className="flex gap-2">
                    <dt className="text-default-500">
                      {t(`sourceAvailability.evidence.${key}`, { defaultValue: key })}
                    </dt>
                    <dd className="break-all">
                      {t(`sourceAvailability.reason.${value}`, { defaultValue: value })}
                    </dd>
                  </div>
                ))}
              </dl>
            )}
            {health.code && (
              <p className="break-words">
                {t(`sourceAvailability.reason.${health.code}`, { defaultValue: health.code })}
              </p>
            )}
            {health.checkedAt && (
              <p>
                {t("sourceAvailability.checkedAt", {
                  time: parseServerTime(health.checkedAt)?.toLocaleString() ?? health.checkedAt,
                })}
              </p>
            )}
            {health.accountScope && (
              <p className="break-all">
                {t("sourceAvailability.accountScope", { scope: health.accountScope })}
              </p>
            )}
            <p className="text-default-500">{t("sourceAvailability.observationHint")}</p>
          </div>
        </Popover>
      )}
    </div>
  );
}

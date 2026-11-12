import { useTranslation } from "react-i18next";

import { Chip, Tooltip } from "@/components/bakaui";
import { CloudDownloadSources, DownloadMethodAvailability } from "@/sdk/constants";

type Selection = { providerId?: string; methodId?: string };

/** Integration availability is separate from accounts, dependencies and share validity. */
export function getDownloadMethodRestrictions({ providerId, methodId }: Selection) {
  return DownloadMethodAvailability.filter((entry) =>
    methodId ? entry.methodId === methodId : !!providerId && entry.providerId === providerId,
  );
}

export function isDownloadMethodUnavailable(selection: Selection): boolean {
  const restrictions = getDownloadMethodRestrictions(selection);

  if (selection.methodId) return restrictions.length > 0;
  const methods = CloudDownloadSources.find(
    (source) => source.providerId === selection.providerId,
  )?.methods;

  return (
    !!methods?.length &&
    methods.every((method) =>
      restrictions.some((restriction) => restriction.methodId === method.id),
    )
  );
}

export function DownloadMethodStatusChip(selection: Selection) {
  const { t } = useTranslation();
  const restrictions = getDownloadMethodRestrictions(selection);

  if (!restrictions.length) return null;
  const partial = !isDownloadMethodUnavailable(selection);

  return (
    <Tooltip
      content={
        <div className="max-w-sm space-y-1 text-xs leading-relaxed">
          {restrictions.map((entry) => (
            <p key={entry.methodId}>{t(entry.descriptionKey)}</p>
          ))}
        </div>
      }
      delay={1000}
      placement="bottom"
    >
      <Chip
        as="span"
        className="h-5 shrink-0 px-1"
        classNames={{ content: "px-0.5 text-[10px] leading-4" }}
        color="warning"
        size="sm"
        variant="flat"
      >
        {t(partial ? "cloudDownload.integration.partial" : "cloudDownload.integration.unavailable")}
      </Chip>
    </Tooltip>
  );
}

export function DownloadMethodAvailabilityNotice(selection: Selection) {
  const { t } = useTranslation();
  const restrictions = getDownloadMethodRestrictions(selection);

  if (!restrictions.length) return null;

  return (
    <div
      className="space-y-2 rounded-lg border border-warning-200 bg-warning-50/40 p-3"
      role="note"
    >
      {restrictions.map((entry) => (
        <div key={entry.methodId} className="space-y-1">
          <p className="text-xs font-medium text-warning-700 dark:text-warning-400">
            {t(`cloudDownload.integration.status.${entry.status}`, {
              defaultValue: t("cloudDownload.integration.unavailable"),
            })}
          </p>
          <p className="text-xs leading-relaxed text-default-600">{t(entry.descriptionKey)}</p>
        </div>
      ))}
    </div>
  );
}

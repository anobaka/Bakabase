import type { DataSyncEntityStatusView } from "../api";

import { useTranslation } from "react-i18next";

import { entityBadge, isTooLargeToSync } from "../viewModels";

import { toneDot, toneText } from "./common";

/**
 * How one definition syncs, in a few words: "Synced · from NAS", "Only on this device",
 * "Definition only", "2 need you"… Shown next to the definition wherever it is listed.
 */
export default function EntitySyncBadge({ entity }: { entity: DataSyncEntityStatusView }) {
  const { t } = useTranslation();
  const badge = entityBadge(entity);
  const text = t(`dataSync.entity.badge.${badge.code}`, badge.values);

  return (
    <span
      className={`inline-flex max-w-full items-center gap-1.5 rounded-md bg-default-100 px-2 py-0.5 text-xs ${toneText[badge.tone]}`}
      data-badge={badge.code}
      data-testid="data-sync-entity-badge"
      title={isTooLargeToSync(entity) ? t("dataSync.entity.tooManyOptions") : text}
    >
      <span aria-hidden className={`h-1.5 w-1.5 shrink-0 rounded-full ${toneDot[badge.tone]}`} />
      <span className="truncate">{text}</span>
    </span>
  );
}

import { useTranslation } from "react-i18next";
import { Chip } from "@heroui/react";
import { CloudDownloadSources, ThirdPartyId } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import type { PreparedCandidate } from "./models";

/** Show the effective source without replacing the address supplied by the user or source module. */
export default function SourceResolution({
  source,
}: {
  source?: PreparedCandidate["resolvedSource"];
}) {
  const { t } = useTranslation();
  if (!source?.originalUrl || source.originalUrl === source.url) return null;
  const site = CloudDownloadSources.find((entry) => entry.providerId === source.providerId);
  const provider = site
    ? t(getEnumKey("ThirdPartyId", ThirdPartyId[site.thirdPartyId]))
    : t(`acquire.source.${source.providerId}`, { defaultValue: source.providerId });
  return (
    <div
      className="min-w-0 space-y-1 rounded-lg bg-default-50 p-2 text-xs"
      aria-label={t("acquire.resolution.label")}
    >
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-default-500">{t("acquire.resolution.label")}</span>
        <Chip size="sm" variant="flat">
          {provider}
        </Chip>
      </div>
      <p className="break-all select-text">{source.url}</p>
      <p className="text-default-500">{t("acquire.resolution.recheck")}</p>
    </div>
  );
}

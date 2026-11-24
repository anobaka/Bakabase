import { useTranslation } from "react-i18next";
import { Chip } from "@heroui/react";
import { downloadSourceProviderLabel } from "@/components/DownloadSource";
import type { PreparedCandidate } from "./models";

/** Show the effective source without replacing the address supplied by the user or source module. */
export default function SourceResolution({
  source,
}: {
  source?: PreparedCandidate["resolvedSource"];
}) {
  const { t } = useTranslation();
  if (!source?.originalUrl || source.originalUrl === source.url) return null;
  const provider = downloadSourceProviderLabel(source.providerId, t) ?? t("acquire.source.unknown");
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

import type { TFunction } from "i18next";
import { FiGlobe } from "react-icons/fi";
import { TbMagnet } from "react-icons/tb";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { CloudDownloadSources, ThirdPartyId } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";

/** Resolve the provider reported by the server; unknown web pages are not direct downloads. */
export function downloadSourceKey(providerId: string): string | undefined {
  if (providerId === "http") return "http";
  if (["bt", "magnet", "torrent"].includes(providerId)) return "bt";
  const site = CloudDownloadSources.find(source => source.providerId === providerId)?.thirdPartyId ??
    (providerId === "exHentai" ? ThirdPartyId.ExHentai : undefined);
  return site == null ? undefined : `site:${site}`;
}

/** Download Center source keys identify protocols or sites, not the tool used to download. */
export function downloadSourceLabel(source: string, t: TFunction): string {
  if (source === "http" || source === "bt") return t(`downloadCenter.provider.${source}`);
  const id = source.startsWith("site:") ? Number(source.slice(5)) : NaN;
  return Number.isInteger(id) && ThirdPartyId[id] ? t(getEnumKey("ThirdPartyId", ThirdPartyId[id])) : source;
}

/** A recognized provider may have a display name even when it has no Download Center adapter. */
export function downloadSourceProviderLabel(providerId: string | null | undefined, t: TFunction): string | undefined {
  if (!providerId || providerId === "unknown") return;
  const source = downloadSourceKey(providerId);
  if (source) return downloadSourceLabel(source, t);
  for (const prefix of ["downloadSource", "acquire.source"]) {
    const key = `${prefix}.${providerId}`;
    const label = t<string>(key);
    if (label && label !== key) return label;
  }
  return providerId;
}

export function DownloadSourceIcon({ source }: { source: string }) {
  if (source === "http") return <FiGlobe size={17} />;
  if (source === "bt") return <TbMagnet size={17} />;
  const id = source.startsWith("site:") ? Number(source.slice(5)) : NaN;
  return Number.isInteger(id) && ThirdPartyId[id] ? <ThirdPartyIcon thirdPartyId={id} /> : null;
}

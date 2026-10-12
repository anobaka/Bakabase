import type { ThirdPartyId } from "@/sdk/constants";

import { useEffect, useState } from "react";

import BApi from "@/sdk/BApi";
import { useDownloaderGlobalOptionsStore } from "@/stores/options";

/** Resolve inheritance on the server using its filesystem rules, without saving an override. */
export default function useEffectiveDownloadPath(
  thirdPartyId: ThirdPartyId,
  configuredPath?: string | null,
) {
  const globalPath = useDownloaderGlobalOptionsStore((s) => s.data?.defaultDownloadPath);
  const [inherited, setInherited] = useState<{
    thirdPartyId: ThirdPartyId;
    globalPath: string;
    path?: string | null;
  }>();
  const hasOverride = !!configuredPath?.trim();

  useEffect(() => {
    let active = true;

    if (!hasOverride && globalPath?.trim()) {
      void BApi.downloadTask
        .getDownloaderOptions(thirdPartyId)
        .then((response) => {
          if (active) {
            setInherited({ thirdPartyId, globalPath, path: response.data?.effectiveDefaultPath });
          }
        })
        .catch(() => {
          // The API client reports failures; never present a guessed destination.
        });
    }

    return () => {
      active = false;
    };
  }, [thirdPartyId, globalPath, hasOverride]);

  if (hasOverride) return configuredPath!;

  return inherited?.thirdPartyId === thirdPartyId && inherited.globalPath === globalPath
    ? (inherited.path ?? undefined)
    : undefined;
}

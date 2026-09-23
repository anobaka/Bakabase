import { useEffect, useRef } from "react";
import { useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { federationPeerApi } from "@/features/federation/peerApi";
import { useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";

/** Entered only after choosing to manage a resource in the original local-library route. */
export function useLocalResourceDeepLink() {
  const [params] = useSearchParams();
  const { createPortal } = useBakabaseContext();
  const local = useRemoteAccessStore((state) => state.isLocal);
  const pureClient = useIsPureClient();
  const { t } = useTranslation();
  const opened = useRef<string>();
  const inspect = params.get("inspect");
  const owner = params.get("node");
  const epoch = params.get("epoch");
  const key = JSON.stringify([inspect, owner, epoch]);

  useEffect(() => {
    if (!inspect || !owner || !epoch || !local || pureClient || opened.current === key) return;
    const id = Number(inspect);

    if (!Number.isSafeInteger(id) || id <= 0) return;
    let cancelled = false;
    const controller = new AbortController();

    void (async () => {
      try {
        const { identity } = await federationPeerApi.status(controller.signal);

        if (cancelled) return;
        if (identity.nodeId !== owner || identity.libraryEpoch !== epoch) {
          opened.current = key;
          toast.error(t("federation.error.LibraryEpochChanged"));

          return;
        }
        const { default: DetailModal } = await import(
          "@/components/Resource/components/DetailModal"
        );

        if (!cancelled) {
          opened.current = key;
          createPortal(DetailModal, { id });
        }
      } catch {
        if (!cancelled) toast.error(t("federation.error.network"));
      }
    })();

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [key, local, pureClient, createPortal]);
}

import type { BakabaseAbstractionsComponentsAppDesktopProfilesView } from "@/sdk/Api";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/** One catalog request for the current workspace and its management controls. */
export function useDesktopProfiles() {
  const { t } = useTranslation();
  const localDesktop = useRemoteAccessStore(
    (s) => s.context === "known" && s.clientMode === ClientMode.AllInOne && s.isLocal,
  );
  const [state, setState] = useState<BakabaseAbstractionsComponentsAppDesktopProfilesView>();
  const [error, setError] = useState<string>();
  const request = useRef(0);
  const translate = useRef(t);

  translate.current = t;
  const load = useCallback(async () => {
    if (!localDesktop) return;
    const id = ++request.current;

    try {
      const response = await BApi.app.getDesktopProfiles();

      if (id !== request.current) return;
      if (response.code)
        throw new Error(response.message || translate.current("profiles.error.load"));
      setState(response.data);
      setError(undefined);
    } catch (reason) {
      if (id === request.current) throw reason;
    }
  }, [localDesktop]);

  useEffect(() => {
    setState(undefined);
    setError(undefined);
    void load().catch((reason) => {
      if (localDesktop)
        setError(
          reason instanceof Error ? reason.message : translate.current("profiles.error.load"),
        );
    });

    return () => {
      ++request.current;
    };
  }, [load, localDesktop]);

  return {
    state: localDesktop ? state : undefined,
    error: localDesktop ? error : undefined,
    localDesktop,
    load,
  };
}

export type DesktopProfilesController = ReturnType<typeof useDesktopProfiles>;

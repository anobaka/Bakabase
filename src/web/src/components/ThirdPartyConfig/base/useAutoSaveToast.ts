import { useCallback } from "react";
import { useTranslation } from "react-i18next";

import { toast } from "@/components/bakaui";

const TOAST_DELAY_MS = 600;

type SaveState = {
  latestRequest: number;
  toastTimer?: ReturnType<typeof setTimeout>;
};

// Switching configuration tabs unmounts their panels. The API method is stable across
// mounts, so it also identifies an ongoing run of saves and its pending confirmation.
const saveStates = new WeakMap<object, SaveState>();

function getSaveState(patchApi: object): SaveState {
  let state = saveStates.get(patchApi);

  if (!state) {
    state = { latestRequest: 0 };
    saveStates.set(patchApi, state);
  }

  return state;
}

/** Show one confirmation for the latest successful change made through a stable API method. */
export function useAutoSaveToast<TPatch>(
  patchApi: (patch: TPatch) => Promise<{ code?: number }>,
): (patch: TPatch) => void {
  const { t } = useTranslation();

  return useCallback(
    (patch: TPatch) => {
      const state = getSaveState(patchApi);
      const request = ++state.latestRequest;

      clearTimeout(state.toastTimer);

      try {
        void patchApi(patch)
          .then((response) => {
            // The API client resolves HTTP 200 responses with a nonzero business code.
            // Only the latest confirmed save should announce success.
            if (request !== state.latestRequest || response?.code !== 0) {
              return;
            }

            state.toastTimer = setTimeout(() => {
              if (request === state.latestRequest) {
                toast.success(t("thirdPartyConfig.success.saved"));
              }
            }, TOAST_DELAY_MS);
          })
          .catch(() => {
            // The API client already reports request failures.
          });
      } catch {
        // A synchronous failure also must not produce a success notification.
      }
    },
    [patchApi, t],
  );
}

export default useAutoSaveToast;

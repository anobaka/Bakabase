import type { TFunction } from "i18next";
import type { DataSyncDialogActions } from "../hooks/useDataSyncActions";

import { useTranslation } from "react-i18next";

import { DataSyncProblemError, DataSyncRequestError } from "../api";
import { errorText } from "../components/common";

import { MessageError } from "@/features/federation/components/common";

/** A failure of data sync's own, in data sync's words; anything else as it is. */
const worded = (t: TFunction, cause: unknown) =>
  cause instanceof DataSyncProblemError || cause instanceof DataSyncRequestError
    ? new MessageError(errorText(t, cause))
    : cause;

/**
 * The host's actions, with data sync's failures said in data sync's words. The device map shows
 * a failure by its message, which for a problem the server answered is only its code: every
 * operation run or confirmed through these turns such a failure into a message of its own before
 * the host sees it. Everything else — one action at a time, what is read again, where focus
 * goes, what the action said — stays the host's.
 */
export function useWordedActions(actions: DataSyncDialogActions): DataSyncDialogActions {
  const { t } = useTranslation();
  const saying =
    (operation: () => Promise<unknown>): (() => Promise<unknown>) =>
    async () => {
      try {
        return await operation();
      } catch (cause) {
        throw worded(t, cause);
      }
    };

  return {
    busy: actions.busy,
    mounted: actions.mounted,
    setNotice: (value) => actions.setNotice(value),
    run: (operation, refresh, onError) => actions.run(saying(operation), refresh, onError),
    confirm: (confirmation) =>
      actions.confirm({ ...confirmation, action: saying(confirmation.action) }),
  };
}

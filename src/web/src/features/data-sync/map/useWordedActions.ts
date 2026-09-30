import type { DataSyncDialogActions } from "../hooks/useDataSyncActions";

import { useTranslation } from "react-i18next";

import { wordedError } from "../components/common";

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
        throw wordedError(t, cause);
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

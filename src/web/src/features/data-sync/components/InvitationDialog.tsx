import type { DataSyncInvitationView } from "../api";
import type { DataSyncDialogActions } from "../hooks/useDataSyncActions";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { dataSyncApi } from "../api";
import { minutesLeft } from "../times";

import { buttonClass, DataSyncErrorNotice, primaryClass } from "./common";
import DataSyncDialog from "./DataSyncDialog";

/*
 * A one-time code another device redeems to read this device's definitions (spec §7.2.3). It
 * needs sharing on and remote access not off; the page offers it only then, so a code shown
 * here always works. Whether the device that uses it may also ask this one to keep in step is
 * decided now, when the code is created, and the consent says what that means.
 */

export default function InvitationDialog({
  actions,
  forName,
  onClose,
  now,
}: {
  actions: DataSyncDialogActions;
  /** The device the code is meant for, when it was asked for from its details. */
  forName?: string;
  onClose: () => void;
  now?: number;
}) {
  const { t } = useTranslation();
  const [allowTwoWay, setAllowTwoWay] = useState(false);
  const [invitation, setInvitation] = useState<DataSyncInvitationView>();
  const [error, setError] = useState<Error>();
  const [, setTick] = useState(0);
  const minutes = invitation ? minutesLeft(invitation.expiresAt, now) : 0;

  // The time left counts down while the code is shown.
  useEffect(() => {
    if (!invitation) return;
    const timer = setInterval(() => setTick((tick) => tick + 1), 15_000);

    return () => clearInterval(timer);
  }, [invitation]);

  const create = () =>
    void actions.run(
      async () => {
        const created = await dataSyncApi.createInvitation(allowTwoWay);

        if (actions.mounted.current) setInvitation(created);
      },
      ["dataSync"],
      setError,
    );

  return (
    <DataSyncDialog
      busy={actions.busy}
      footer={
        invitation ? (
          <button className={primaryClass} type="button" onClick={onClose}>
            {t("dataSync.done")}
          </button>
        ) : (
          <>
            <button className={buttonClass} disabled={actions.busy} type="button" onClick={onClose}>
              {t("dataSync.cancel")}
            </button>
            <button
              className={primaryClass}
              data-testid="data-sync-invitation-create"
              disabled={actions.busy}
              type="button"
              onClick={create}
            >
              {t("dataSync.invitation.create")}
            </button>
          </>
        )
      }
      testId="data-sync-invitation"
      title={
        forName
          ? t("dataSync.invitation.createFor", { name: forName })
          : t("dataSync.invitation.title")
      }
      onClose={onClose}
    >
      {invitation ? (
        <div className="space-y-3 text-sm">
          <p>{t("dataSync.invitation.use")}</p>
          <p
            className="rounded-lg bg-default-100 py-3 text-center font-mono text-3xl tracking-[0.3em]"
            data-testid="data-sync-invitation-code"
          >
            {invitation.code}
          </p>
          <p className="text-xs text-default-500">
            {minutes > 0
              ? t("dataSync.invitation.expiresIn", { count: minutes })
              : t("dataSync.invitation.expired")}
          </p>
          {invitation.addresses.length > 0 && (
            <div className="space-y-1">
              <p className="text-xs text-default-500">{t("dataSync.invitation.addresses")}</p>
              <ul className="space-y-0.5">
                {invitation.addresses.map((address) => (
                  <li key={address} className="break-all font-mono text-xs">
                    {address}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {invitation.allowTwoWay && (
            <p className="text-xs text-default-500">{t("dataSync.twoWay.consentCode")}</p>
          )}
        </div>
      ) : (
        <div className="space-y-3 text-sm">
          <p>{t("dataSync.invitation.intro")}</p>
          <label className="flex items-start gap-2">
            <input
              checked={allowTwoWay}
              className="mt-0.5"
              data-testid="data-sync-invitation-two-way"
              type="checkbox"
              onChange={(event) => setAllowTwoWay(event.target.checked)}
            />
            <span>{t("dataSync.invitation.allowTwoWay")}</span>
          </label>
          {allowTwoWay && (
            <p className="rounded-lg border border-warning/40 bg-warning/10 p-3 text-xs">
              {t("dataSync.twoWay.consentCode")}
            </p>
          )}
          <DataSyncErrorNotice error={error} />
        </div>
      )}
    </DataSyncDialog>
  );
}

"use client";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import AppDataMaintenanceButton from "../Import";

import { Button, Modal, Snippet } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useRelocationPendingStore } from "@/stores/relocationPending";
import { useIsPureClient } from "@/stores/remoteAccess";

/** Target selection and confirmation belong to the common maintenance page. */
export const RelocationButton: React.FC = () => <AppDataMaintenanceButton operation="relocate" />;

/**
 * Subscribes to <code>RelocationPending</code> hub events and renders a non-dismissable
 * "restart now" modal. Call once near the AppInfo / settings page root.
 */
export const RelocationRestartGate: React.FC = () => {
  const { t } = useTranslation();
  const pending = useRelocationPendingStore((s) => s.pending);
  // The data being moved is the server's, and so is the process that restarts to
  // finish the move. For a managed server shown in this window, "restart Bakabase"
  // would otherwise read as "restart this window", which is not what the button does.
  const isPureClient = useIsPureClient();
  const [open, setOpen] = useState(false);
  const [restarting, setRestarting] = useState(false);

  useEffect(() => {
    if (pending) setOpen(true);
  }, [pending]);

  if (!pending) return null;

  const triggerRestart = async () => {
    if (restarting) return;
    setRestarting(true);
    try {
      await BApi.app.restartApp();
    } catch {
      // restart endpoint best-effort — even if the server lost the response, the actual
      // process spawn happens server-side. Reload anyway.
    }
    // Give the server a moment to spawn the replacement before we reload.
    setTimeout(() => window.location.reload(), 800);
  };

  return (
    <Modal
      hideCloseButton
      isKeyboardDismissDisabled
      footer={{ actions: [] }}
      isDismissable={false}
      size="md"
      title={t("configuration.dataPath.restart.title")}
      visible={open}
      onClose={() => {}}
    >
      <div className="flex flex-col gap-4">
        <p>
          {t(
            isPureClient
              ? "configuration.dataPath.restart.body.server"
              : "configuration.dataPath.restart.body",
          )}
        </p>
        <Snippet hideSymbol size="sm" variant="bordered">
          {pending.target}
        </Snippet>
        <Button
          color="primary"
          isDisabled={restarting}
          isLoading={restarting}
          onPress={triggerRestart}
        >
          {t(
            isPureClient
              ? "configuration.dataPath.restart.button.server"
              : "configuration.dataPath.restart.button",
          )}
        </Button>
      </div>
    </Modal>
  );
};

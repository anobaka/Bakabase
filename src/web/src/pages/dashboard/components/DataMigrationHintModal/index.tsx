"use client";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";

import { Button, Modal } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

const DISMISSAL_FLAG_KEY = "bakabase.dataMigrationHint.v23.dismissed";

/**
 * One-time dashboard hint pointing users at the AppData path migration UI on first 2.3
 * launch when the backend reports a legacy install AppData lying around. Dismissal lives in
 * <code>localStorage</code> per machine + browser; intentional that it doesn't sync server-side
 * — the legacy notice banner in the configuration page has its own server-side dismissal.
 */
export const DataMigrationHintModal: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (localStorage.getItem(DISMISSAL_FLAG_KEY) === "1") return;

    let cancelled = false;
    BApi.app.getAppInfo().then((r) => {
      if (cancelled) return;
      const info = (r as any).data;
      if (!info) return;

      const isV23 = typeof info.coreVersion === "string" && info.coreVersion.startsWith("2.3");
      const mayHaveLegacy = info.mayHaveLegacyData === true;
      if (isV23 && mayHaveLegacy) setOpen(true);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  if (!open) return null;

  const dismiss = () => {
    localStorage.setItem(DISMISSAL_FLAG_KEY, "1");
    setOpen(false);
  };

  const goToSettings = () => {
    dismiss();
    navigate("/configuration");
  };

  return (
    <Modal
      size="md"
      title={t("dashboard.dataMigrationHint.title")}
      visible={open}
      onClose={dismiss}
      footer={{ actions: [] }}
    >
      <div className="flex flex-col gap-4">
        <p>{t("dashboard.dataMigrationHint.body")}</p>
        <div className="flex gap-2 justify-end">
          <Button variant="light" onPress={dismiss}>
            {t("dashboard.dataMigrationHint.dismiss")}
          </Button>
          <Button color="primary" onPress={goToSettings}>
            {t("dashboard.dataMigrationHint.goToSettings")}
          </Button>
        </div>
      </div>
    </Modal>
  );
};

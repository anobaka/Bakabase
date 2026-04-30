"use client";

import React from "react";
import { useTranslation } from "react-i18next";

import { Button, Snippet } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useLegacyInstallNoticeStore } from "@/stores/legacyInstallNotice";

/**
 * Persistent banner shown to users who upgraded from beta.64–66 and still have data
 * stranded under <code>&lt;install&gt;/current/AppData</code>. Never auto-dismisses; the
 * user clicks "got it" to mark it dismissed forever.
 */
export const LegacyAppDataNoticeBanner: React.FC = () => {
  const { t } = useTranslation();
  const notice = useLegacyInstallNoticeStore((s) => s.notice);
  const clear = useLegacyInstallNoticeStore((s) => s.clear);

  if (!notice) return null;

  const open = () => BApi.tool.openFileOrDirectory({ path: notice.path });

  const dismiss = async () => {
    try {
      await BApi.app.dismissLegacyInstallNotice();
    } finally {
      clear();
    }
  };

  return (
    <div className="rounded-md border border-warning-200 bg-warning-50 p-3 mb-3 flex flex-col gap-2">
      <div className="font-semibold">{t("configuration.legacyNotice.title")}</div>
      <div className="text-sm">{t("configuration.legacyNotice.description")}</div>
      <Snippet hideSymbol size="sm" variant="bordered">{notice.path}</Snippet>
      <div className="flex gap-2">
        <Button size="sm" variant="flat" color="primary" onPress={open}>
          {t("configuration.legacyNotice.openButton")}
        </Button>
        <Button size="sm" variant="light" onPress={dismiss}>
          {t("configuration.legacyNotice.dismissButton")}
        </Button>
      </div>
    </div>
  );
};

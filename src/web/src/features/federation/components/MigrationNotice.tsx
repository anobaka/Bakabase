import { useTranslation } from "react-i18next";
import { useState } from "react";

import { parseConnectionHints } from "../migration";

import { buttonClass, ErrorNotice } from "./common";

import ExternalLink from "@/components/ExternalLink";
import { clientApi } from "@/core/clientApi";
import { useIsPureClient } from "@/stores/remoteAccess";

export default function MigrationNotice() {
  const { t } = useTranslation();
  const isPureClient = useIsPureClient();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();

  return (
    <aside className="rounded-xl border border-primary/20 bg-primary/5 p-4">
      <h2 className="font-medium">{t("federation.migration.fullDesktop")}</h2>
      <p className="mt-2 text-sm">{t("federation.migration.intro")}</p>
      <p className="mt-2 text-xs text-default-500">{t("federation.migration.security")}</p>
      <p className="mt-2 text-xs text-default-500">{t("federation.migration.independent")}</p>
      <ExternalLink
        className="mt-3 inline-block text-sm text-primary underline"
        href="https://github.com/anobaka/Bakabase/releases"
      >
        {t("federation.migration.download")}
      </ExternalLink>
      {isPureClient && (
        <div className="mt-3">
          <button
            className={buttonClass}
            disabled={busy}
            type="button"
            onClick={() =>
              void (async () => {
                setBusy(true);
                setError(undefined);
                try {
                  const hints = parseConnectionHints(await clientApi.migrationHints());
                  const url = URL.createObjectURL(
                    new Blob([JSON.stringify(hints, null, 2)], { type: "application/json" }),
                  );
                  const link = document.createElement("a");

                  link.href = url;
                  link.download = "bakabase-connection-hints.json";
                  document.body.appendChild(link);
                  link.click();
                  link.remove();
                  setTimeout(() => URL.revokeObjectURL(url), 1000);
                } catch (cause) {
                  setError(cause instanceof Error ? cause : new Error(String(cause)));
                } finally {
                  setBusy(false);
                }
              })()
            }
          >
            {t("federation.migration.export")}
          </button>
          <p className="mt-2 text-xs text-default-500">{t("federation.migration.exportTip")}</p>
          <ErrorNotice error={error} />
        </div>
      )}
    </aside>
  );
}

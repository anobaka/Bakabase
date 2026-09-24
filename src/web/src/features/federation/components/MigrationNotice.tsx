import { useTranslation } from "react-i18next";
import { useState } from "react";

import { parseConnectionHints } from "../migration";

import { buttonClass, ErrorNotice } from "./common";

import ExternalLink from "@/components/ExternalLink";
import { clientApi } from "@/core/clientApi";
import { useIsConsole, useIsPureClient } from "@/stores/remoteAccess";

/**
 * Bakabase Client is retired; this says what replaces it and what moving costs.
 *
 * Moving costs nothing on the same computer: the desktop app imports the client's
 * pairings — keys and path mappings included — so every server stays managed without
 * pairing again. The hint export stays for the one case that import cannot reach, a
 * desktop app installed on a *different* computer, and it never carries a key.
 *
 * Absent in the desktop app's console: that window already is the replacement.
 */
export default function MigrationNotice() {
  const { t } = useTranslation();
  const isPureClient = useIsPureClient();
  const isConsole = useIsConsole();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const [outcome, setOutcome] = useState<"saved" | "cancelled">();

  if (isConsole) return null;

  return (
    <aside className="rounded-xl border border-primary/20 bg-primary/5 p-4">
      <h2 className="font-medium">{t("federation.migration.fullDesktop")}</h2>
      <p className="mt-2 text-sm">{t("federation.migration.intro")}</p>
      <p className="mt-2 text-xs text-default-500">{t("federation.migration.automatic")}</p>
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
                setOutcome(undefined);
                try {
                  const native = await clientApi.exportMigrationHints();

                  if (native.outcome !== "unavailable") {
                    setOutcome(native.outcome);

                    return;
                  }
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
          {outcome && (
            <p className="mt-2 text-sm" role="status">
              {t(`federation.migration.export.${outcome}`)}
            </p>
          )}
          <ErrorNotice error={error} />
        </div>
      )}
    </aside>
  );
}

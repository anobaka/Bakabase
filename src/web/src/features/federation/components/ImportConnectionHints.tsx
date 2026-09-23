import type { ConnectionHints } from "../migration";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import {
  MAX_HINT_FILE_BYTES,
  clearConnectionHintDraft,
  loadConnectionHintDraft,
  mergeConnectionHints,
  saveConnectionHintDraft,
} from "../migration";
import { FederationError } from "../transport";

import { buttonClass, ErrorNotice, panelClass } from "./common";

export default function ImportConnectionHints({
  onSelect,
  busy,
}: {
  onSelect: (address: string) => void;
  busy: boolean;
}) {
  const { t } = useTranslation();
  const [initial] = useState(() => {
    try {
      return { hints: loadConnectionHintDraft(), error: undefined };
    } catch {
      return { hints: undefined, error: new FederationError("MigrationDraftUnavailable", "", 400) };
    }
  });
  const [hints, setHints] = useState<ConnectionHints | undefined>(initial.hints);
  const [error, setError] = useState<Error | undefined>(initial.error);
  const generation = useRef(0);

  useEffect(
    () => () => {
      generation.current += 1;
    },
    [],
  );

  return (
    <section className={panelClass}>
      <h2 className="font-semibold">{t("federation.migration.import")}</h2>
      <p className="mt-2 text-sm text-default-500">{t("federation.migration.importTip")}</p>
      <label className="mt-3 block space-y-2 text-sm">
        <span>{t("federation.migration.chooseFile")}</span>
        <input
          accept="application/json,.json"
          className="block w-full text-xs"
          disabled={busy}
          type="file"
          onChange={(event) => {
            const file = event.target.files?.[0];
            const current = ++generation.current;

            event.currentTarget.value = "";
            setError(undefined);
            if (!file) return;
            void (async () => {
              try {
                if (file.size > MAX_HINT_FILE_BYTES) throw new Error("InvalidConnectionHints");
                const contents = JSON.parse(await file.text());

                if (generation.current !== current) return;
                let previous;

                try {
                  previous = loadConnectionHintDraft();
                } catch {
                  throw new FederationError("MigrationDraftUnavailable", "", 400);
                }
                const merged = mergeConnectionHints(previous, contents);

                try {
                  saveConnectionHintDraft(merged);
                } catch {
                  throw new FederationError("MigrationDraftUnavailable", "", 400);
                }
                setHints(merged);
              } catch (cause) {
                if (generation.current === current)
                  setError(
                    cause instanceof FederationError
                      ? cause
                      : new FederationError("InvalidConnectionHints", "", 400),
                  );
              }
            })();
          }}
        />
      </label>
      <div className="mt-3">
        <ErrorNotice error={error} />
      </div>
      {(hints || error) && (
        <button
          className={buttonClass}
          disabled={busy}
          type="button"
          onClick={() => {
            generation.current += 1;
            try {
              clearConnectionHintDraft();
              setHints(undefined);
              setError(undefined);
            } catch {
              setError(new FederationError("MigrationDraftUnavailable", "", 400));
            }
          }}
        >
          {t("federation.migration.clearDraft")}
        </button>
      )}
      {hints && (
        <div className="mt-3 space-y-3">
          <p className="text-xs text-default-500" role="status">
            {t("federation.migration.draftSaved")}
          </p>
          {!hints.servers.length && (
            <p className="text-sm text-default-500">{t("federation.discovery.none")}</p>
          )}
          {hints.servers.map((server, index) => (
            <article key={`${server.address}:${index}`} className="rounded-lg bg-default-50 p-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="break-all text-sm">
                  {server.name || server.address}
                  {server.name && <span className="ml-2 text-default-500">{server.address}</span>}
                </p>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() => onSelect(server.address)}
                >
                  {t("federation.discovery.use")}
                </button>
              </div>
              {server.pathMappings.length > 0 && (
                <details className="mt-2 text-xs text-default-500">
                  <summary className="cursor-pointer">
                    {t("federation.migration.mappingHints")}
                  </summary>
                  <p className="mt-2">{t("federation.migration.rebind")}</p>
                  <ul className="mt-2 space-y-1">
                    {server.pathMappings.map((mapping, index) => (
                      <li key={index} className="break-all">
                        {mapping.serverPath} → {mapping.localPath}
                      </li>
                    ))}
                  </ul>
                </details>
              )}
            </article>
          ))}
        </div>
      )}
    </section>
  );
}

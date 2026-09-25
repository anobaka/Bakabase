import type { ManagedServer, ManagedServerPathMapping } from "../types";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { buttonClass, fieldClass, primaryClass } from "./common";

/**
 * Where a managed server's library folders are on this computer — what its relay uses to play
 * files and open folders here. Shown on the server's card on the devices page and in its
 * panel on the device map.
 */
export default function ManagedServerPathMappings({
  server,
  busy,
  onSave,
  className = "border-t border-default-200 pt-3",
}: {
  server: ManagedServer;
  busy: boolean;
  onSave: (mappings: ManagedServerPathMapping[]) => Promise<boolean>;
  className?: string;
}) {
  const { t } = useTranslation();
  const [mappings, setMappings] = useState(server.pathMappings);
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (!dirty) setMappings(server.pathMappings);
  }, [server.pathMappings, dirty]);

  const edit = (next: (rows: ManagedServerPathMapping[]) => ManagedServerPathMapping[]) => {
    setDirty(true);
    setMappings(next);
  };
  const save = async () => {
    // Sent whole rather than merged: a removed row has to stop mapping.
    const proposed = mappings.map((row) => ({
      serverPath: row.serverPath.trim(),
      localPath: row.localPath.trim(),
    }));

    if (await onSave(proposed)) {
      setMappings(proposed);
      setDirty(false);
    }
  };

  return (
    <details className={className}>
      <summary className="cursor-pointer text-sm font-medium">
        {t("federation.servers.mappings.title")}
        {server.pathMappings.length > 0 && (
          <span className="ml-2 text-xs text-default-500">{server.pathMappings.length}</span>
        )}
      </summary>
      <p className="mt-2 text-xs text-default-500">{t("federation.servers.mappings.tip")}</p>
      <div className="mt-3 space-y-2">
        {mappings.map((mapping, index) => (
          <div key={index} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
            <label className="space-y-1 text-xs">
              <span>{t("federation.servers.mappings.server")}</span>
              <input
                className={fieldClass}
                disabled={busy}
                placeholder={"D:\\Media"}
                value={mapping.serverPath}
                onChange={(event) =>
                  edit((rows) =>
                    rows.map((row, i) =>
                      i === index ? { ...row, serverPath: event.target.value } : row,
                    ),
                  )
                }
              />
            </label>
            <label className="space-y-1 text-xs">
              <span>{t("federation.servers.mappings.local")}</span>
              <input
                className={fieldClass}
                disabled={busy}
                placeholder="/Volumes/Media"
                value={mapping.localPath}
                onChange={(event) =>
                  edit((rows) =>
                    rows.map((row, i) =>
                      i === index ? { ...row, localPath: event.target.value } : row,
                    ),
                  )
                }
              />
            </label>
            <button
              aria-label={t("federation.servers.mappings.remove")}
              className={`${buttonClass} self-end`}
              disabled={busy}
              type="button"
              onClick={() => edit((rows) => rows.filter((_, i) => i !== index))}
            >
              ×
            </button>
          </div>
        ))}
      </div>
      <div className="mt-3 flex gap-2">
        <button
          className={buttonClass}
          disabled={busy}
          type="button"
          onClick={() => edit((rows) => [...rows, { serverPath: "", localPath: "" }])}
        >
          {t("federation.servers.mappings.add")}
        </button>
        <button
          className={primaryClass}
          disabled={
            busy ||
            !dirty ||
            mappings.some((row) => !row.serverPath.trim() || !row.localPath.trim())
          }
          type="button"
          onClick={() => void save()}
        >
          {t("federation.servers.mappings.save")}
        </button>
      </div>
    </details>
  );
}

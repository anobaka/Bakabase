import type { ManagedDeviceName } from "../serverApi";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { managedServerApi } from "../serverApi";
import { isAbort } from "../transport";

import { buttonClass, ErrorNotice, fieldClass, primaryClass } from "./common";

/** Renames the owner itself; the captured name and epoch prevent overwriting a concurrent edit. */
export default function ManagedDeviceNameEditor({
  serverId,
  busy,
  onSaved,
}: {
  serverId: string;
  busy: boolean;
  onSaved: (name: string) => void;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [revision, setRevision] = useState(0);
  const [snapshot, setSnapshot] = useState<ManagedDeviceName>();
  const [name, setName] = useState("");
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<Error>();
  const mounted = useRef(false);

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
    };
  }, []);
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();

    setLoading(true);
    setSnapshot(undefined);
    setError(undefined);
    void managedServerApi
      .getDeviceName(serverId, controller.signal)
      .then((current) => {
        if (controller.signal.aborted) return;
        setSnapshot(current);
        setName(current.name);
      })
      .catch((cause: unknown) => {
        if (!controller.signal.aborted && !isAbort(cause))
          setError(cause instanceof Error ? cause : new Error(String(cause)));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });

    return () => controller.abort();
  }, [open, revision, serverId]);

  const save = async (nextName: string | null) => {
    if (!snapshot || saving || busy || snapshot.environmentControlled) return;
    setSaving(true);
    setError(undefined);
    try {
      const changed = await managedServerApi.setDeviceName(serverId, snapshot, nextName);

      if (!mounted.current) return;
      onSaved(changed.name);
      setOpen(false);
    } catch (cause) {
      if (mounted.current) setError(cause instanceof Error ? cause : new Error(String(cause)));
    } finally {
      if (mounted.current) setSaving(false);
    }
  };

  if (!open)
    return (
      <button className={buttonClass} disabled={busy} type="button" onClick={() => setOpen(true)}>
        {t("federation.servers.name.edit")}
      </button>
    );

  return (
    <form
      className="space-y-2 rounded-lg bg-default-50 p-3"
      onSubmit={(event) => {
        event.preventDefault();
        void save(name.trim() || null);
      }}
    >
      <p className="text-xs text-default-500">{t("federation.servers.name.tip")}</p>
      {loading && (
        <p className="text-sm" role="status">
          {t("federation.loading")}
        </p>
      )}
      <ErrorNotice error={error} />
      {snapshot && (
        <label className="block space-y-1 text-sm">
          <span>{t("federation.name.label")}</span>
          <input
            className={fieldClass}
            disabled={saving || snapshot.environmentControlled}
            maxLength={64}
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        </label>
      )}
      {snapshot?.environmentControlled && (
        <p className="text-sm text-warning-600 dark:text-warning" role="status">
          {t("federation.error.DeviceNameEnvironmentControlled")}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <button
          className={primaryClass}
          disabled={
            busy || loading || saving || !snapshot || snapshot.environmentControlled || !name.trim()
          }
          type="submit"
        >
          {t("federation.save")}
        </button>
        {snapshot && !snapshot.environmentControlled && (
          <button
            className={buttonClass}
            disabled={busy || loading || saving}
            type="button"
            onClick={() => void save(null)}
          >
            {t("federation.name.reset")}
          </button>
        )}
        {error && (
          <button
            className={buttonClass}
            disabled={saving || loading}
            type="button"
            onClick={() => setRevision((current) => current + 1)}
          >
            {t("federation.servers.name.reload")}
          </button>
        )}
        <button
          className={buttonClass}
          disabled={saving}
          type="button"
          onClick={() => setOpen(false)}
        >
          {t("federation.cancel")}
        </button>
      </div>
    </form>
  );
}

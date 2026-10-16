import type { ManagedDeviceName } from "../federation/serverApi";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { managedServerApi } from "../federation/serverApi";
import { FederationError, isAbort } from "../federation/transport";
import {
  buttonClass,
  ErrorNotice,
  fieldClass,
  primaryClass,
} from "../federation/components/common";

import DeviceColorEditor from "./DeviceColorEditor";

import BApi from "@/sdk/BApi";

/** Name belongs to the owner; color belongs to the viewing server. Each saves independently. */
export default function DeviceMetadataEditor({
  serverId,
  name: displayName,
  local = false,
  busy = false,
  onSaved,
}: {
  serverId: string;
  name: string;
  local?: boolean;
  busy?: boolean;
  onSaved: (name: string) => void;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [revision, setRevision] = useState(0);
  const [snapshot, setSnapshot] = useState<ManagedDeviceName>();
  const [name, setName] = useState("");
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [colorSaving, setColorSaving] = useState(false);
  const [saved, setSaved] = useState(false);
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
    setSaved(false);
    setSnapshot(undefined);
    setError(undefined);
    const request = local
      ? BApi.management.getManagedDeviceName({ signal: controller.signal, showErrorToast: false })
      : managedServerApi.getDeviceName(serverId, controller.signal);

    void request
      .then((current) => {
        if (controller.signal.aborted) return;
        setSnapshot(current);
        setName(current.name);
      })
      .catch((cause: unknown) => {
        if (!controller.signal.aborted && !isAbort(cause)) setError(nameError(cause));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });

    return () => controller.abort();
  }, [open, revision, serverId, local]);

  const save = async (nextName: string | null) => {
    if (!snapshot || saving || busy || snapshot.environmentControlled) return;
    setSaving(true);
    setSaved(false);
    setError(undefined);
    try {
      const changed = local
        ? await BApi.management.setManagedDeviceName(
            {
              name: nextName ?? undefined,
              expectedName: snapshot.name,
              libraryEpoch: snapshot.libraryEpoch,
            },
            { showErrorToast: false },
          )
        : await managedServerApi.setDeviceName(serverId, snapshot, nextName);

      if (!mounted.current) return;
      setSnapshot(changed);
      setName(changed.name);
      setSaved(true);
      onSaved(changed.name);
    } catch (cause) {
      if (mounted.current) setError(nameError(cause));
    } finally {
      if (mounted.current) setSaving(false);
    }
  };

  if (!open)
    return (
      <button
        className={`${buttonClass} !px-2 !py-1 text-xs`}
        type="button"
        onClick={() => setOpen(true)}
      >
        {t("deviceAppearance.editMetadata")}
      </button>
    );

  return (
    <section
      aria-label={t("deviceAppearance.detailsFor", { name: displayName })}
      className="w-full max-w-xl space-y-3 rounded-lg border border-divider bg-default-50 p-3"
    >
      <form
        className="space-y-2"
        onSubmit={(event) => {
          event.preventDefault();
          void save(name.trim() || null);
        }}
      >
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
              onChange={(event) => {
                setName(event.target.value);
                setSaved(false);
              }}
            />
          </label>
        )}
        <p className="text-xs text-default-500">{t("federation.servers.name.tip")}</p>
        {saved && (
          <p className="text-xs text-success" role="status">
            {t("deviceAppearance.nameSaved")}
          </p>
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
              busy ||
              loading ||
              saving ||
              !snapshot ||
              snapshot.environmentControlled ||
              !name.trim()
            }
            type="submit"
          >
            {t("deviceAppearance.saveName")}
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
        </div>
      </form>
      <div className="border-t border-divider pt-3">
        <DeviceColorEditor
          embedded
          name={displayName}
          serverId={serverId}
          onSavingChange={setColorSaving}
        />
      </div>
      <button
        className={buttonClass}
        disabled={saving || colorSaving}
        type="button"
        onClick={() => setOpen(false)}
      >
        {t("deviceAppearance.close")}
      </button>
    </section>
  );
}

/** The SDK rejects with its HTTP response; preserve the owner's localizable refusal code. */
function nameError(cause: unknown): Error {
  if (cause instanceof Error) return cause;
  const response = cause as
    | { error?: { code?: unknown; message?: string }; status?: number }
    | undefined;

  if (typeof response?.error?.code === "string")
    return new FederationError(
      response.error.code,
      response.error.message ?? "",
      response.status ?? 0,
    );

  return new Error(String(cause));
}

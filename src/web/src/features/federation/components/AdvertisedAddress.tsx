import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { buttonClass, fieldClass, primaryClass } from "./common";

import BApi from "@/sdk/BApi";
import { normalizeRemoteAccessAddress } from "@/core/remoteAccessAddress";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { RemoteAccessMode } from "@/sdk/constants";

/** Shared by the device tab and configuration; a hint for other devices, never a reachability claim. */
export default function AdvertisedAddress({
  value,
  onSaved,
  disabled = false,
}: {
  value?: string | null;
  onSaved: () => Promise<unknown>;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  const id = useId();
  const canAdminister = useRemoteAccessStore(
    (state) =>
      state.context === "known" &&
      (state.isLocal || state.paired || state.mode === RemoteAccessMode.Unrestricted),
  );
  const [draft, setDraft] = useState(value ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const [saved, setSaved] = useState(false);
  const inFlight = useRef(false);

  useEffect(() => setDraft(value ?? ""), [value]);

  if (!canAdminister) return null;

  const save = async (text: string) => {
    if (inFlight.current || disabled) return;
    const address = text.trim() ? normalizeRemoteAccessAddress(text) : null;

    setSaved(false);
    if (address === undefined) {
      setError(t("federation.devices.advertisedAddress.invalid"));

      return;
    }
    inFlight.current = true;
    setSaving(true);
    setError(undefined);
    try {
      const response = await BApi.remoteAccess.setRemoteAccessAdvertisedAddress(
        // The generated optional property is omitted to restore the server's null default.
        { address: address ?? undefined },
        { showErrorToast: false },
      );

      if (response.code)
        throw new Error(response.message || t("federation.devices.advertisedAddress.failed"));
      setDraft(address ?? "");
      await onSaved();
      setSaved(true);
    } catch (cause) {
      const message =
        cause instanceof Error
          ? cause.message
          : (cause as { error?: { message?: string } } | undefined)?.error?.message;

      setError(message || t("federation.devices.advertisedAddress.failed"));
    } finally {
      inFlight.current = false;
      setSaving(false);
    }
  };

  return (
    <form
      className="space-y-2"
      onSubmit={(event) => {
        event.preventDefault();
        void save(draft);
      }}
    >
      <label className="block text-sm font-medium" htmlFor={id}>
        {t("federation.devices.advertisedAddress.label")}
      </label>
      <div className="flex flex-wrap items-center gap-2">
        <input
          aria-describedby={`${id}-tip`}
          aria-invalid={!!error}
          autoComplete="off"
          className={`${fieldClass} min-w-0 flex-1 basis-64`}
          disabled={disabled || saving}
          id={id}
          maxLength={2048}
          placeholder={t("federation.devices.advertisedAddress.automatic")}
          type="text"
          value={draft}
          onChange={(event) => {
            setDraft(event.target.value);
            setError(undefined);
            setSaved(false);
          }}
        />
        <button
          className={primaryClass}
          disabled={disabled || saving || draft.trim() === (value ?? "")}
          type="submit"
        >
          {t(saving ? "federation.devices.advertisedAddress.saving" : "federation.save")}
        </button>
        {value && (
          <button
            className={buttonClass}
            disabled={disabled || saving}
            type="button"
            onClick={() => void save("")}
          >
            {t("federation.devices.advertisedAddress.reset")}
          </button>
        )}
      </div>
      <p className="text-xs text-default-500" id={`${id}-tip`}>
        {t("federation.devices.advertisedAddress.tip")}
      </p>
      {error && (
        <p className="break-words text-sm text-danger" role="alert">
          {error}
        </p>
      )}
      {saved && (
        <p className="text-xs text-success" role="status">
          {t("federation.devices.advertisedAddress.saved")}
        </p>
      )}
    </form>
  );
}

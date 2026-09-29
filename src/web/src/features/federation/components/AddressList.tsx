import type { BakabaseServiceModelsViewRemoteAccessAddressViewModel as RemoteAccessAddress } from "@/sdk/Api";
import type { DeviceAddress } from "../devices/addresses";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";

import { deviceAddresses, isReachableKind } from "../devices/addresses";
import { devicesRoute } from "../switching";

import { buttonClass } from "./common";

/** What a row says about where it leads, beside the interface's own name. */
const kindLabel: Partial<Record<DeviceAddress["kind"], string>> = {
  vpn: "federation.devices.addresses.vpn",
  virtual: "federation.devices.addresses.virtual",
  linkLocal: "federation.devices.addresses.linkLocal",
};

function useCopy() {
  const [copied, setCopied] = useState<{ value: string; ok: boolean }>();
  const timer = useRef<ReturnType<typeof setTimeout>>();

  useEffect(() => () => clearTimeout(timer.current), []);

  const copy = async (value: string) => {
    let ok = true;

    try {
      await navigator.clipboard.writeText(value);
    } catch {
      ok = false;
    }
    setCopied({ value, ok });
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setCopied(undefined), 2000);
  };

  return { copied, copy };
}

/**
 * The addresses another device types to reach this one (or the server the window shows):
 * the recommended one first, the other networks after it, and the adapters another device
 * cannot reach folded away, each labelled. Every host stays in the API; only the list a
 * person reads is shortened (see `devices/addresses.ts`).
 *
 * `full` is the device tab's own list, with a title; `compact` sits beside a code, showing
 * the recommended address and everything else behind one disclosure.
 */
export default function AddressList({
  addresses,
  target,
  context,
  variant = "full",
  remoteOff = false,
}: {
  addresses: readonly RemoteAccessAddress[] | undefined;
  /** Whom the addresses reach, as the sentences name it: "this device" or a server's name. */
  target: string;
  context: "device" | "manage" | "sharing";
  variant?: "full" | "compact";
  /** Remote access is off: nobody can reach any of them, and the list says so. */
  remoteOff?: boolean;
}) {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);
  const { copied, copy } = useCopy();
  const rows = useMemo(() => deviceAddresses(addresses ?? []), [addresses]);
  const recommended = rows.find((row) => row.recommended) ?? rows[0];
  const rest = rows.filter((row) => row !== recommended);
  const shown = variant === "full" ? rest.filter((row) => isReachableKind(row.kind)) : [];
  const folded = rest.filter((row) => !shown.includes(row));

  const copyButton = (row: DeviceAddress) => (
    <button
      aria-label={t("federation.copyAddress", { address: row.url })}
      className={`${buttonClass} !px-2 !py-1 text-xs`}
      type="button"
      onClick={() => void copy(row.url)}
    >
      {t(
        copied?.value !== row.url
          ? "federation.copy"
          : copied.ok
            ? "federation.copied"
            : "federation.copyFailed",
      )}
    </button>
  );
  const note = (row: DeviceAddress) => {
    const key = kindLabel[row.kind];

    return (
      <span className="text-xs text-default-400">
        {row.interfaceName}
        {key ? ` · ${t(key)}` : ""}
      </span>
    );
  };
  const row = (item: DeviceAddress) => (
    <li key={item.url} className="flex flex-wrap items-center gap-2">
      <code className="break-all text-sm">{item.url}</code>
      {note(item)}
      {copyButton(item)}
    </li>
  );

  return (
    <div
      aria-label={t("federation.devices.addresses.title", { target })}
      className={`space-y-2 ${remoteOff ? "opacity-60" : ""}`}
      data-context={context}
      data-testid="device-addresses"
      role="group"
    >
      {variant === "full" && (
        <h3 className="text-sm font-medium">
          {t("federation.devices.addresses.title", { target })}
        </h3>
      )}
      {remoteOff && (
        <p className="text-sm text-warning-600 dark:text-warning">
          {t("federation.devices.addresses.remoteOff", { target })}
          {context !== "manage" && (
            <>
              {" "}
              <Link className="text-primary underline" to={devicesRoute("management")}>
                {t("federation.devices.openManagementAccess")}
              </Link>
            </>
          )}
        </p>
      )}
      {!recommended ? (
        <p className="text-sm text-warning-600 dark:text-warning">
          {t("federation.devices.addresses.none", { target })}
        </p>
      ) : (
        <>
          <div
            className="flex flex-wrap items-center gap-2 rounded-lg bg-default-50 p-2"
            data-testid="device-address-recommended"
          >
            <code className={`break-all ${variant === "full" ? "text-lg" : "text-base"}`}>
              {recommended.url}
            </code>
            {recommended.recommended && (
              <span className="rounded-md bg-success/10 px-2 py-0.5 text-xs text-success">
                {t("federation.devices.addresses.recommended")}
              </span>
            )}
            {note(recommended)}
            {copyButton(recommended)}
          </div>
          {shown.length > 0 && <ul className="space-y-1">{shown.map(row)}</ul>}
          {folded.length > 0 && (
            <div data-testid="device-address-more">
              <button
                aria-expanded={expanded}
                className="text-xs text-primary underline"
                type="button"
                onClick={() => setExpanded((open) => !open)}
              >
                {expanded
                  ? t("federation.devices.addresses.fewer")
                  : t("federation.devices.addresses.more", { count: folded.length })}
              </button>
              {expanded && <ul className="mt-2 space-y-1">{folded.map(row)}</ul>}
            </div>
          )}
          {variant === "full" && (
            <p className="text-xs text-default-500">{t("federation.devices.addresses.tip")}</p>
          )}
        </>
      )}
    </div>
  );
}

import type { ReactNode } from "react";
import type { DevicesSection } from "../switching";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { AiOutlineRight } from "react-icons/ai";

import {
  buttonClass,
  ErrorNotice,
  fieldClass,
  panelClass,
  primaryClass,
} from "../components/common";
import AddressList from "../components/AddressList";
import { revealClass } from "../hooks/useSectionReveal";
import { federationPeerApi } from "../peerApi";
import { devicesRoute } from "../switching";

import { useDevicesPage } from "./context";
import TabHeading from "./TabHeading";
import { troubledServers, waitingSharingRequests } from "./selectors";

import { RemoteAccessMode } from "@/sdk/constants";

/** One line of "Waiting for you" or "Status": what it says, and the place it is decided. */
function RowLink({ to, children }: { to: DevicesSection; children: ReactNode }) {
  return (
    <li>
      <Link
        className="flex items-center justify-between gap-3 rounded-lg px-2 py-1.5 text-sm hover:bg-default-100"
        to={devicesRoute(to)}
      >
        {children}
        <AiOutlineRight aria-hidden className="shrink-0 text-default-400" />
      </Link>
    </li>
  );
}

/**
 * The device tab: what this device is called, the address another device types, what is
 * waiting for an answer here, and the way to add another device — manage it, or browse it.
 * The everyday questions; everything they lead to is decided in the other tabs.
 */
export default function ThisDeviceTab() {
  const { t } = useTranslation();
  const { data, busy, run, mounted, revealed, tabs } = useDevicesPage();
  const [editingName, setEditingName] = useState<string>();
  const { status, access, servers } = data;
  const target = t<string>("federation.management.self");
  const mode = access?.mode;
  const saveName = (name: string) =>
    void run(async () => {
      await federationPeerApi.setName(name.trim() || null);
      if (mounted.current) setEditingName(undefined);
    });

  const waitingManage = access?.pendingRequests ?? [];
  const waitingBrowse = waitingSharingRequests(data);
  const troubled = troubledServers(data);
  const unrestricted = mode === RemoteAccessMode.Unrestricted;
  const waiting = waitingManage.length + waitingBrowse.length + troubled.length > 0 || unrestricted;
  const choosers = tabs.flatMap((tab) =>
    tab.chooser && !tab.chooser.hidden?.(data) ? [tab.chooser] : [],
  );
  const managementStatus = !access
    ? undefined
    : mode === RemoteAccessMode.Disabled
      ? "off"
      : mode === RemoteAccessMode.Unrestricted
        ? "unrestricted"
        : access.requirePairing
          ? "paired"
          : "open";
  const inbound = (status?.peers ?? []).filter((peer) => peer.inboundGrant).length;
  const outbound = (status?.peers ?? []).filter((peer) => peer.outboundGrant).length;

  return (
    <>
      <TabHeading />
      {status && (
        <section data-focus-section className={`${panelClass} space-y-2`}>
          <p className="text-xs text-default-500">{t("federation.thisDevice")}</p>
          {editingName === undefined ? (
            <div className="flex flex-wrap items-center gap-2">
              <h3 className="text-lg font-semibold outline-none" tabIndex={-1}>
                {status.identity.name}
              </h3>
              <button
                className={`${buttonClass} !px-2 !py-1 text-xs`}
                disabled={busy}
                type="button"
                onClick={() => setEditingName(status.identity.name)}
              >
                {t("federation.name.edit")}
              </button>
            </div>
          ) : (
            <form
              className="space-y-2"
              onSubmit={(event) => {
                event.preventDefault();
                saveName(editingName);
              }}
            >
              <label className="block space-y-1 text-sm">
                <span>{t("federation.name.label")}</span>
                <input
                  // eslint-disable-next-line jsx-a11y/no-autofocus
                  autoFocus
                  className={fieldClass}
                  maxLength={64}
                  value={editingName}
                  onChange={(event) => setEditingName(event.target.value)}
                />
              </label>
              <p className="text-xs text-default-500">{t("federation.name.tip")}</p>
              <div className="flex flex-wrap gap-2">
                <button className={primaryClass} disabled={busy} type="submit">
                  {t("federation.save")}
                </button>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() => saveName("")}
                >
                  {t("federation.name.reset")}
                </button>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() => setEditingName(undefined)}
                >
                  {t("federation.cancel")}
                </button>
              </div>
            </form>
          )}
        </section>
      )}
      <section
        className={`${panelClass} ${revealClass(revealed === "addresses")}`}
        data-highlighted={revealed === "addresses" || undefined}
        id="device-addresses"
        tabIndex={-1}
      >
        {access ? (
          <AddressList
            addresses={access.addresses}
            context="device"
            remoteOff={mode === RemoteAccessMode.Disabled}
            target={target}
          />
        ) : data.accessError ? (
          <ErrorNotice error={data.accessError} onRetry={() => void data.reload(["access"])} />
        ) : (
          <p className="text-sm">{t("federation.loading")}</p>
        )}
      </section>
      {waiting && (
        <section className={`${panelClass} space-y-2`} data-testid="devices-waiting">
          <h3 className="font-semibold">{t("federation.devices.waiting.title")}</h3>
          <ul className="space-y-1">
            {waitingManage.map((request) => (
              <RowLink key={`manage:${request.id}`} to="management">
                <span>{t("federation.devices.waiting.manage", { name: request.deviceName })}</span>
              </RowLink>
            ))}
            {waitingBrowse.map((request) => (
              <RowLink key={`browse:${request.requestId}`} to="sharing-requests">
                <span>{t("federation.devices.waiting.browse", { name: request.nodeName })}</span>
              </RowLink>
            ))}
            {troubled.map((server) => (
              <RowLink key={`server:${server.serverId}`} to="servers">
                <span>
                  {t("federation.devices.waiting.server", {
                    name: server.name || server.address,
                    state: t(`federation.servers.state.${server.state}`),
                  })}
                </span>
              </RowLink>
            ))}
            {unrestricted && (
              <RowLink to="management">
                <span className="text-warning-600 dark:text-warning">
                  {t("federation.devices.waiting.unrestricted")}
                </span>
              </RowLink>
            )}
          </ul>
        </section>
      )}
      {choosers.length > 0 && (
        <section className="space-y-2" data-testid="devices-chooser">
          <h3 className="font-semibold">{t("federation.devices.chooser.title")}</h3>
          <div className="grid gap-3 @xl:grid-cols-2">
            {choosers.map((chooser) => (
              <Link
                key={chooser.section}
                className={`${panelClass} block space-y-1 transition hover:border-primary`}
                to={devicesRoute(chooser.section)}
              >
                <span className="flex items-center justify-between gap-2 font-medium">
                  {t(chooser.titleKey)}
                  <AiOutlineRight aria-hidden className="text-default-400" />
                </span>
                <span className="block text-sm text-default-500">{t(chooser.descKey)}</span>
              </Link>
            ))}
          </div>
        </section>
      )}
      <section className={`${panelClass} space-y-2`} data-testid="devices-status">
        <h3 className="font-semibold">{t("federation.devices.status.title")}</h3>
        <ul className="space-y-1">
          {managementStatus && (
            <RowLink to="management">
              <span className="text-default-500">
                {t("federation.devices.status.remoteAccess")}
              </span>
              <span className="ml-auto">
                {t(`federation.management.status.${managementStatus}`)}
              </span>
            </RowLink>
          )}
          {access && (
            <RowLink to="management">
              <span className="text-default-500">{t("federation.map.panel.self.managedBy")}</span>
              <span className="ml-auto">{access.devices?.length ?? 0}</span>
            </RowLink>
          )}
          {servers?.available && (
            <RowLink to="servers">
              <span className="text-default-500">{t("federation.map.panel.self.manages")}</span>
              <span className="ml-auto">{servers.servers.length}</span>
            </RowLink>
          )}
          {status && (
            <RowLink to="share">
              <span className="text-default-500">{t("federation.devices.status.sharing")}</span>
              <span className="ml-auto">
                {t(status.sharingEnabled ? "federation.sharing.on" : "federation.sharing.off")} ·{" "}
                {t("federation.map.panel.self.sharesWith")} {inbound}
              </span>
            </RowLink>
          )}
          {status && (
            <RowLink to="browsing">
              <span className="text-default-500">{t("federation.devices.status.browsing")}</span>
              <span className="ml-auto">
                {t(status.browsingEnabled ? "federation.browsing.on" : "federation.browsing.off")} ·{" "}
                {t("federation.map.panel.self.browses")} {outbound}
              </span>
            </RowLink>
          )}
        </ul>
        <Link className="inline-block text-xs text-primary underline" to="/federation/map">
          {t("federation.devices.status.openMap")}
        </Link>
      </section>
    </>
  );
}

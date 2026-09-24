"use client";

import type React from "react";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { WarningOutlined } from "@ant-design/icons";
import { Link } from "react-router-dom";

import { compareAppVersions } from "@/core/versionComparison";
import { clientApi } from "@/core/clientApi";
import BApi from "@/sdk/BApi";
import { Tooltip } from "@/components/bakaui";
import { useIsLegacyClient, useRemoteAccessStore } from "@/stores/remoteAccess";

/**
 * Says, and keeps saying, that the two halves are not the same version.
 *
 * The protocol handshake refuses a pair that cannot talk at all. This is the other case:
 * they can talk, and one of them is missing something the other knows about — most often
 * a user-side endpoint the client has not learned, which otherwise appears as "this needs
 * a newer client" at the moment somebody tries to use the feature. Told beforehand, that
 * error has an explanation; told nowhere, it reads as a bug.
 *
 * Deliberately not a toast and not dismissable: a mismatch does not stop being true
 * because it was acknowledged once, and the thing it explains may not be hit for days.
 * It is quiet instead — one line, no buttons, the same shape as the update banner beside
 * it, and gone the moment the versions match.
 *
 * The retired thin client only. The desktop app's console answers as the same flavour,
 * but there the two versions are two independent installs — the device's own app and a
 * server it manages — with no client to update into step, and the connection page this
 * points at does not exist there.
 */
const ClientVersionNotice: React.FC<{ collapsed: boolean }> = ({ collapsed }) => {
  const { t } = useTranslation();
  const isPureClient = useIsLegacyClient();
  // A client that cannot reach its server has nothing to compare against, and the
  // disconnection is the thing worth the user's attention rather than this.
  const serverReachable = useRemoteAccessStore((state) => state.serverReachable);

  const [client, setClient] = useState<string>();
  const [server, setServer] = useState<string>();

  useEffect(() => {
    if (!isPureClient || !serverReachable) {
      return;
    }

    clientApi
      .appInfo()
      .then((info) => setClient(info.version))
      .catch(() => {});

    // The forwarded app info, which is the server's — the one value on it this needs.
    BApi.app
      .getAppInfo()
      .then((rsp) => setServer(rsp.data?.coreVersion ?? undefined))
      .catch(() => {});
  }, [isPureClient, serverReachable]);

  if (!isPureClient || !serverReachable || !client || !server) {
    return null;
  }

  const relation = compareAppVersions(client, server);

  if (relation === "same") {
    return null;
  }

  const message = t(`clientVersion.${relation}`, { client, server });

  return (
    <div className={collapsed ? "px-2 py-1.5 flex justify-center" : "px-3 py-1.5"}>
      <Tooltip
        className="max-w-[320px]"
        content={message}
        isDisabled={!collapsed}
        placement="right"
      >
        {/* The connection page is where the server's update lives, and where this client
            can be pointed at a different server entirely. */}
        <Link
          className="flex items-center gap-1.5 text-xs text-warning no-underline"
          to="/client-connection"
        >
          <WarningOutlined className="text-sm shrink-0" />
          {!collapsed && <span className="leading-snug">{message}</span>}
        </Link>
      </Tooltip>
    </div>
  );
};

ClientVersionNotice.displayName = "ClientVersionNotice";

export default ClientVersionNotice;

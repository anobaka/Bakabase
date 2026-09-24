"use client";

import type React from "react";

import { useTranslation } from "react-i18next";
import { InfoCircleOutlined } from "@ant-design/icons";
import { Link } from "react-router-dom";

import { Tooltip } from "@/components/bakaui";
import { useIsLegacyClient } from "@/stores/remoteAccess";

/**
 * Tells a Bakabase Client user that the client is retired, and what replaces it.
 *
 * Only in the retired client itself — told apart from the desktop app's console, which
 * answers as the same flavour but *is* the replacement. The move costs nothing: the
 * desktop app brings the client's pairings over on its own, so nothing is paired again,
 * and saying so up front is most of what makes the switch an easy one.
 *
 * Not dismissable, for the same reason as the version notice beside it: it stays true.
 * It stays quiet instead — one line in the sidebar, pointing at the downloads page.
 */
const LegacyClientNotice: React.FC<{ collapsed: boolean }> = ({ collapsed }) => {
  const { t } = useTranslation();
  const isLegacyClient = useIsLegacyClient();

  if (!isLegacyClient) {
    return null;
  }

  const message = t("federation.legacyClient.notice");

  return (
    <div
      className={collapsed ? "px-2 py-1.5 flex justify-center" : "px-3 py-1.5"}
      data-testid="legacy-client-notice"
    >
      <Tooltip
        className="max-w-[320px]"
        content={message}
        isDisabled={!collapsed}
        placement="right"
      >
        <Link
          aria-label={collapsed ? message : undefined}
          className="flex items-start gap-1.5 rounded-lg bg-primary/10 px-2 py-1.5 text-xs text-primary no-underline"
          to="/other-devices"
        >
          <InfoCircleOutlined className="mt-0.5 text-sm shrink-0" />
          {!collapsed && <span className="leading-snug">{message}</span>}
        </Link>
      </Tooltip>
    </div>
  );
};

LegacyClientNotice.displayName = "LegacyClientNotice";

export default LegacyClientNotice;

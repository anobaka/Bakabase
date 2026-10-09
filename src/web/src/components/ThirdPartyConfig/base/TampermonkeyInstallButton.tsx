"use client";

import type { FC, ReactNode } from "react";

import { useTranslation } from "react-i18next";
import { GrInstallOption } from "react-icons/gr";

import { Alert, Button } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { openExternalUrl } from "@/utils/openExternalUrl";

export interface TampermonkeyInstallButtonProps {
  /** Optional description items rendered inside the alert. */
  descriptions?: ReactNode[];
}

const TampermonkeyInstallButton: FC<TampermonkeyInstallButtonProps> = ({ descriptions }) => {
  const { t } = useTranslation();
  const clientMode = useRemoteAccessStore((state) => state.clientMode);

  const install = () => {
    if (clientMode === ClientMode.RemoteBrowser) {
      const url = new URL(BApi.tampermonkey.getTampermonkeyScriptUrl(), window.location.origin);

      // Preserve the browser-visible API origin across reverse proxies and separate web/API hosts.
      url.searchParams.set("apiEndpoint", url.origin);
      openExternalUrl(url.href);

      return;
    }

    return BApi.tampermonkey.installTampermonkeyScript();
  };

  return (
    <Alert
      color="success"
      description={
        <div className="space-y-3 mt-1">
          {descriptions && descriptions.length > 0 && (
            <div>
              {descriptions.map((desc, i) => (
                <div key={i}>
                  {i + 1}. {desc}
                </div>
              ))}
            </div>
          )}
          <Button color="primary" size="sm" onPress={install}>
            <GrInstallOption className="text-base" />
            {t<string>("thirdPartyIntegration.action.oneClickInstall")}
          </Button>
        </div>
      }
      title={t<string>("thirdPartyIntegration.tip.scriptModifies")}
      variant="flat"
    />
  );
};

export default TampermonkeyInstallButton;

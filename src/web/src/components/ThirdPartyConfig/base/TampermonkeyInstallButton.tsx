"use client";

import type { FC, ReactNode } from "react";

import { useTranslation } from "react-i18next";
import { GrInstallOption } from "react-icons/gr";

import { Alert, Button } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

export interface TampermonkeyInstallButtonProps {
  /** Optional description items rendered inside the alert. */
  descriptions?: ReactNode[];
}

const TampermonkeyInstallButton: FC<TampermonkeyInstallButtonProps> = ({ descriptions }) => {
  const { t } = useTranslation();

  return (
    <Alert
      color="success"
      variant="flat"
      title={t<string>("thirdPartyIntegration.tip.scriptModifies")}
      description={
        <div className="space-y-3 mt-1">
          {descriptions && descriptions.length > 0 && (
            <div>
              {descriptions.map((desc, i) => (
                <div key={i}>{i + 1}. {desc}</div>
              ))}
            </div>
          )}
          <Button
            color="primary"
            size="sm"
            onPress={() => BApi.tampermonkey.installTampermonkeyScript()}
          >
            <GrInstallOption className="text-base" />
            {t<string>("thirdPartyIntegration.action.oneClickInstall")}
          </Button>
        </div>
      }
    />
  );
};

export default TampermonkeyInstallButton;

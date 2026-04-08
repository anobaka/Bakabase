"use client";

import { useEffect, useMemo, useState, type FC } from "react";
import { useTranslation } from "react-i18next";
import { Button, Input } from "@heroui/react";

import { toast } from "@/components/bakaui";
import { CookieValidatorTarget } from "@/sdk/constants";
import { useSoulPlusOptionsStore } from "@/stores/options";

import AccountsPanel, { type AccountField } from "../base/AccountsPanel";
import ConfigurableThirdPartyPanel, { type ConfigFieldTab } from "../base/ConfigurableThirdPartyPanel";
import ThirdPartyConfigModal from "../base/ThirdPartyConfigModal";
import TampermonkeyInstallButton from "../base/TampermonkeyInstallButton";

export enum SoulPlusConfigField {
  Accounts = "accounts",
  Other = "other",
  Integration = "integration",
}

export interface SoulPlusConfigPanelProps {
  fields?: SoulPlusConfigField[] | "all";
}

export const SoulPlusConfigPanel: FC<SoulPlusConfigPanelProps> = ({ fields = "all" }) => {
  const { t } = useTranslation();
  const options = useSoulPlusOptionsStore((s) => s.data);
  const patch = useSoulPlusOptionsStore((s) => s.patch);

  const [tmpOther, setTmpOther] = useState({ autoBuyThreshold: options?.autoBuyThreshold ?? 10 });

  useEffect(() => {
    setTmpOther({ autoBuyThreshold: options?.autoBuyThreshold ?? 10 });
  }, [options?.autoBuyThreshold]);

  const accountFields: AccountField[] = useMemo(
    () => [
      {
        key: "cookie",
        label: t("resourceSource.accounts.cookie"),
        placeholder: t("resourceSource.accounts.cookiePlaceholder"),
        type: "textarea" as const,
        cookieValidatorTarget: CookieValidatorTarget.SoulPlus,
        cookieCaptureTarget: CookieValidatorTarget.SoulPlus,
      },
    ],
    [t],
  );

  const saveAccounts = async (accounts: any[]) => {
    await patch({ accounts });
    toast.success(t("thirdPartyConfig.success.saved"));
  };

  const saveAutoBuy = async () => {
    await patch({ autoBuyThreshold: tmpOther.autoBuyThreshold });
    toast.success(t("thirdPartyConfig.success.saved"));
  };

  const tabs: ConfigFieldTab<SoulPlusConfigField>[] = useMemo(
    () => [
      {
        field: SoulPlusConfigField.Accounts,
        key: "accounts",
        title: t("resourceSource.config.tab.accounts"),
        content: <AccountsPanel accounts={options?.accounts || []} fields={accountFields} onSave={saveAccounts} />,
      },
      {
        field: SoulPlusConfigField.Other,
        key: "other",
        title: t("thirdPartyConfig.group.other"),
        content: (
          <div className="space-y-4">
            <Input
              label={t<string>("thirdPartyConfig.label.autoBuyThreshold")}
              size="sm"
              type="number"
              value={String(tmpOther.autoBuyThreshold ?? 0)}
              onValueChange={(v) => setTmpOther({ ...tmpOther, autoBuyThreshold: Number(v) || 0 })}
            />
            <Button color="primary" size="sm" onPress={saveAutoBuy}>
              {t<string>("thirdPartyConfig.action.save")}
            </Button>
          </div>
        ),
      },
      {
        field: SoulPlusConfigField.Integration,
        key: "integration",
        title: t("thirdPartyIntegration.label.tampermonkeyScript"),
        content: (
          <TampermonkeyInstallButton
            descriptions={[t<string>("thirdPartyIntegration.tip.soulPlusClick")]}
          />
        ),
      },
    ],
    [options, tmpOther, accountFields, t, patch],
  );

  return <ConfigurableThirdPartyPanel fields={fields} tabs={tabs} />;
};

export interface SoulPlusConfigModalProps {
  onDestroyed?: () => void;
  onClose?: () => void;
  isOpen?: boolean;
  fields?: SoulPlusConfigField[] | "all";
}

export const SoulPlusConfigModal: FC<SoulPlusConfigModalProps> = ({ onDestroyed, onClose, isOpen, fields }) => {
  const handleClose = onClose ?? onDestroyed;
  return (
    <ThirdPartyConfigModal title="SoulPlus" isOpen={isOpen} onClose={handleClose}>
      <SoulPlusConfigPanel fields={fields} />
    </ThirdPartyConfigModal>
  );
};

const SoulPlusConfig = SoulPlusConfigModal;
export default SoulPlusConfig;

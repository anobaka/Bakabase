"use client";

import { useEffect, useMemo, useState, type FC } from "react";
import { useTranslation } from "react-i18next";
import { Button, Input } from "@heroui/react";

import { toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { CookieValidatorTarget } from "@/sdk/constants";
import { useBangumiOptionsStore } from "@/stores/options";

import AccountsPanel, { type AccountField } from "../base/AccountsPanel";
import ConfigurableThirdPartyPanel, { type ConfigFieldTab } from "../base/ConfigurableThirdPartyPanel";
import ThirdPartyConfigModal from "../base/ThirdPartyConfigModal";

export enum BangumiConfigField {
  Accounts = "accounts",
  DataFetch = "dataFetch",
}

export interface BangumiConfigPanelProps {
  fields?: BangumiConfigField[] | "all";
}

export const BangumiConfigPanel: FC<BangumiConfigPanelProps> = ({ fields = "all" }) => {
  const { t } = useTranslation();
  const bangumiOptions = useBangumiOptionsStore((s) => s.data);
  const patch = useBangumiOptionsStore((s) => s.patch);

  const [tmpHttp, setTmpHttp] = useState<any>(bangumiOptions || {});

  useEffect(() => {
    setTmpHttp(JSON.parse(JSON.stringify(bangumiOptions || {})));
  }, [bangumiOptions]);

  const accountFields: AccountField[] = useMemo(
    () => [
      {
        key: "cookie",
        label: t("resourceSource.accounts.cookie"),
        placeholder: t("resourceSource.accounts.cookiePlaceholder"),
        type: "textarea" as const,
        cookieValidatorTarget: CookieValidatorTarget.Bangumi,
        cookieCaptureTarget: CookieValidatorTarget.Bangumi,
      },
    ],
    [t],
  );

  const saveAccounts = async (accounts: any[]) => {
    await patch({ accounts });
    useBangumiOptionsStore.getState().update({ accounts });
    toast.success(t("thirdPartyConfig.success.saved"));
  };

  const saveHttp = async () => {
    await patch({
      maxConcurrency: tmpHttp.maxConcurrency,
      requestInterval: tmpHttp.requestInterval,
      userAgent: tmpHttp.userAgent,
      referer: tmpHttp.referer,
      headers: tmpHttp.headers,
    });
    toast.success(t<string>("thirdPartyConfig.success.saved"));
  };

  const tabs: ConfigFieldTab<BangumiConfigField>[] = useMemo(
    () => [
      {
        field: BangumiConfigField.Accounts,
        key: "accounts",
        title: t("resourceSource.config.tab.accounts"),
        content: (
          <AccountsPanel accounts={bangumiOptions?.accounts || []} fields={accountFields} onSave={saveAccounts} />
        ),
      },
      {
        field: BangumiConfigField.DataFetch,
        key: "http",
        title: t("thirdPartyConfig.group.dataFetch"),
        content: (
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <Input
                label={t<string>("thirdPartyConfig.label.maxConcurrency")}
                size="sm"
                type="number"
                value={String(tmpHttp.maxConcurrency || 1)}
                onValueChange={(v) => setTmpHttp({ ...tmpHttp, maxConcurrency: Number(v) || 1 })}
              />
              <Input
                label={t<string>("thirdPartyConfig.label.requestInterval")}
                size="sm"
                type="number"
                value={String(tmpHttp.requestInterval || 1000)}
                onValueChange={(v) => setTmpHttp({ ...tmpHttp, requestInterval: Number(v) || 1000 })}
              />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <Input
                label={t<string>("thirdPartyConfig.label.userAgent")}
                size="sm"
                value={tmpHttp.userAgent || ""}
                onValueChange={(v) => setTmpHttp({ ...tmpHttp, userAgent: v })}
              />
              <Input
                label={t<string>("thirdPartyConfig.label.referer")}
                size="sm"
                value={tmpHttp.referer || ""}
                onValueChange={(v) => setTmpHttp({ ...tmpHttp, referer: v })}
              />
            </div>
            <Button color="primary" size="sm" onPress={saveHttp}>
              {t<string>("thirdPartyConfig.action.save")}
            </Button>
          </div>
        ),
      },
    ],
    [bangumiOptions, tmpHttp, accountFields, t],
  );

  return <ConfigurableThirdPartyPanel fields={fields} tabs={tabs} />;
};

export interface BangumiConfigModalProps {
  onDestroyed?: () => void;
  onClose?: () => void;
  isOpen?: boolean;
  fields?: BangumiConfigField[] | "all";
}

export const BangumiConfigModal: FC<BangumiConfigModalProps> = ({ onDestroyed, onClose, isOpen, fields }) => {
  const handleClose = onClose ?? onDestroyed;
  return (
    <ThirdPartyConfigModal title="Bangumi" isOpen={isOpen} onClose={handleClose}>
      <BangumiConfigPanel fields={fields} />
    </ThirdPartyConfigModal>
  );
};

const BangumiConfig = BangumiConfigModal;
export default BangumiConfig;

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Select, SelectItem } from "@heroui/react";
import { Button } from "@/components/bakaui";
import type { BaiduAccount } from "@/components/ThirdPartyConfig/platforms/BaiduNetdiskConfig";
import BApi from "@/sdk/BApi";

export default function AccountSelect({
  value,
  onChange,
}: {
  value?: string;
  onChange: (id: string | undefined) => void;
}) {
  const { t } = useTranslation();
  const [accounts, setAccounts] = useState<BaiduAccount[]>([]);
  const [failed, setFailed] = useState(false);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let active = true;
    void BApi.baiduPcs
      .getBaiduPcsAccounts()
      .then((response) => {
        if (!active) return;
        setFailed(!!response.code);
        setAccounts(response.data ?? []);
      })
      .catch(() => {
        if (active) setFailed(true);
      });
    return () => {
      active = false;
    };
  }, [revision]);
  return (
    <div className="space-y-1">
      <Select
        label={t("cloudDownload.account.label")}
        placeholder={t("cloudDownload.account.useDefault")}
        selectedKeys={value ? [value] : []}
        disabledKeys={accounts
          .filter((account) => !account.isLoggedIn)
          .map((account) => account.id)}
        description={t(
          failed ? "cloudDownload.account.loadFailed" : "cloudDownload.account.selectionHint",
        )}
        onSelectionChange={(keys) => onChange(Array.from(keys)[0] as string | undefined)}
      >
        {accounts.map((account) => (
          <SelectItem
            key={account.id}
          >{`${account.name}${account.isDefault ? ` (${t("cloudDownload.account.default")})` : ""}${!account.isLoggedIn ? ` · ${t("cloudDownload.account.loggedOut")}` : ""}`}</SelectItem>
        ))}
      </Select>
      <Button size="sm" variant="light" onPress={() => setRevision((current) => current + 1)}>
        {t("cloudDownload.account.refresh")}
      </Button>
    </div>
  );
}

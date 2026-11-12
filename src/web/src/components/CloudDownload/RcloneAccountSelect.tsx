import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Select } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type { BakabaseServiceComponentsCloudDownloadsRcloneRcloneAccountViewModel as Account } from "@/sdk/Api";

export default function RcloneAccountSelect({
  providerId,
  value,
  onChange,
  isDisabled = false,
}: {
  providerId: string;
  value?: string;
  onChange: (id: string | undefined) => void;
  isDisabled?: boolean;
}) {
  const { t } = useTranslation();
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [error, setError] = useState(false);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let active = true;
    setAccounts([]);
    setError(false);
    void BApi.rclone
      .getRcloneAccounts({ providerId })
      .then((response) => {
        if (!active) return;
        setError(!!response.code);
        setAccounts(response.code ? [] : (response.data ?? []));
      })
      .catch(() => {
        if (active) setError(true);
      });
    return () => {
      active = false;
    };
  }, [providerId, revision]);
  return (
    <div className="space-y-1">
      <Select
        label={t("rclone.account.label")}
        placeholder={t("cloudDownload.account.useDefault")}
        description={t(error ? "cloudDownload.account.loadFailed" : "rclone.account.selectionHint")}
        selectedKeys={value ? [value] : []}
        disabledKeys={accounts
          .filter((account) => !account.isConfigured)
          .map((account) => account.id)}
        isDisabled={isDisabled}
        dataSource={accounts.map((account) => ({
          value: account.id,
          label: `${account.name}${account.isDefault ? ` · ${t("cloudDownload.account.default")}` : ""}${!account.isConfigured ? ` · ${t("rclone.account.notConfigured")}` : ""}`,
        }))}
        onSelectionChange={(keys) => onChange(Array.from(keys)[0] as string | undefined)}
      />
      <Button
        size="sm"
        variant="light"
        isDisabled={isDisabled}
        onPress={() => setRevision((value) => value + 1)}
      >
        {t("cloudDownload.account.refresh")}
      </Button>
    </div>
  );
}

"use client";

import type { BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel } from "@/sdk/Api";
import type { SettingItem } from "@/pages/configuration/components/SettingsSection";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { Button, Input, Modal, Switch } from "@/components/bakaui";
import SettingsSection from "@/pages/configuration/components/SettingsSection";
import BApi from "@/sdk/BApi";
import { useAppOptionsStore } from "@/stores/options";

const Backup: React.FC<{ query?: string }> = ({ query }) => {
  const { t } = useTranslation();
  const appOptions = useAppOptionsStore((state) => state.data);
  const updateAppOptions = useAppOptionsStore((state) => state.update);
  const enabled = appOptions.enableAutomaticBackup ?? true;
  const maxVersions = appOptions.maxBackupVersions ?? 7;
  const [draftVersions, setDraftVersions] = useState(String(maxVersions));
  const [saving, setSaving] = useState(false);
  const [confirmDisable, setConfirmDisable] = useState(false);
  const parsedVersions = Number(draftVersions);
  const validVersions =
    /^\d+$/.test(draftVersions) &&
    Number.isInteger(parsedVersions) &&
    parsedVersions >= 1 &&
    parsedVersions <= 2147483647;

  useEffect(() => {
    setDraftVersions(String(maxVersions));
  }, [maxVersions]);

  const save = async (
    patch: BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel,
  ) => {
    setSaving(true);
    try {
      const response = await BApi.options.patchAppOptions(patch);

      if (response.code) return false;

      updateAppOptions(patch);
      toast.success(t("common.success.saved"));

      return true;
    } catch {
      toast.error(t("common.error.failedToProcessData"));

      return false;
    } finally {
      setSaving(false);
    }
  };

  const settings: SettingItem[] = [
    {
      id: "enableAutomaticBackup",
      label: t("configuration.backup.enabled"),
      keywords: ["automatic", "upgrade", "recovery", "自动", "升级", "恢复"],
      render: () => (
        <Switch
          aria-label={t("configuration.backup.enabled")}
          isDisabled={saving}
          isSelected={enabled}
          size="sm"
          onValueChange={(selected) => {
            if (selected) {
              void save({ enableAutomaticBackup: true });
            } else {
              setConfirmDisable(true);
            }
          }}
        />
      ),
    },
    {
      id: "maxBackupVersions",
      label: t("configuration.backup.maxVersions"),
      keywords: ["retention", "limit", "space", "保留", "数量", "空间"],
      render: () => (
        <div className="flex flex-col gap-1.5">
          <div className="flex items-start gap-2">
            <Input
              aria-label={t("configuration.backup.maxVersions")}
              className="w-40"
              errorMessage={t("configuration.backup.invalidVersions")}
              isDisabled={!enabled || saving}
              isInvalid={!validVersions}
              max={2147483647}
              min={1}
              size="sm"
              step={1}
              type="number"
              value={draftVersions}
              onValueChange={setDraftVersions}
            />
            <Button
              color="primary"
              isDisabled={!enabled || saving || !validVersions || parsedVersions === maxVersions}
              isLoading={saving}
              size="sm"
              onPress={() => void save({ maxBackupVersions: parsedVersions })}
            >
              {t("common.action.save")}
            </Button>
          </div>
          <p className="text-xs text-foreground-500">{t("configuration.backup.retentionNote")}</p>
        </div>
      ),
    },
  ];

  return (
    <>
      <SettingsSection
        header={
          <p className="text-sm text-foreground-500">{t("configuration.backup.description")}</p>
        }
        items={settings}
        keywords={["backup", "backups", "备份"]}
        query={query}
        title={t("configuration.backup.title")}
      />
      <Modal
        footer={
          <>
            <Button isDisabled={saving} variant="flat" onPress={() => setConfirmDisable(false)}>
              {t("configuration.backup.disable.cancel")}
            </Button>
            <Button
              color="danger"
              isLoading={saving}
              onPress={async () => {
                if (await save({ enableAutomaticBackup: false })) {
                  setConfirmDisable(false);
                }
              }}
            >
              {t("configuration.backup.disable.confirm")}
            </Button>
          </>
        }
        isDismissable={!saving}
        isKeyboardDismissDisabled={saving}
        title={t("configuration.backup.disable.title")}
        visible={confirmDisable}
        onClose={() => {
          if (!saving) setConfirmDisable(false);
        }}
      >
        <p>{t("configuration.backup.disable.warning")}</p>
        <p className="text-sm text-foreground-500">{t("configuration.backup.disable.preserved")}</p>
      </Modal>
    </>
  );
};

export default Backup;

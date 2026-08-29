"use client";

import type { SettingItem } from "@/pages/configuration/components/SettingsSection";
import type { BakabaseServiceModelsViewRemoteAccessSettingsViewModel } from "@/sdk/Api";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { AiOutlineCopy } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { RemoteAccessMode } from "@/sdk/constants";
import { Button, Chip, Select, Switch } from "@/components/bakaui";
import SettingsSection from "@/pages/configuration/components/SettingsSection";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

interface RemoteAccessProps {
  query?: string;
}

const RemoteAccess: React.FC<RemoteAccessProps> = ({ query }) => {
  const { t } = useTranslation();
  const [settings, setSettings] = useState<BakabaseServiceModelsViewRemoteAccessSettingsViewModel>();
  const reloadClientContext = useRemoteAccessStore((state) => state.load);

  const load = useCallback(async () => {
    const rsp = await BApi.remoteAccess.getRemoteAccessSettings();

    if (!rsp.code && rsp.data) {
      setSettings(rsp.data);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const setMode = async (mode: RemoteAccessMode) => {
    const rsp = await BApi.remoteAccess.setRemoteAccessMode({ mode });

    if (!rsp.code) {
      toast.success(t("common.success.saved"));
      await load();
      // The banner and the play button read this, so refresh it rather than
      // waiting for a reload.
      await reloadClientContext();
    }
  };

  const copy = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      toast.success(t("configuration.remoteAccess.address.copied"));
    } catch {
      toast.error(t("configuration.remoteAccess.address.copyFailed"));
    }
  };

  const mode = settings?.mode ?? RemoteAccessMode.Disabled;

  const items: SettingItem[] = [
    {
      id: "mode",
      label: t("configuration.remoteAccess.mode.label"),
      tip: t("configuration.remoteAccess.mode.tip"),
      keywords: ["remote", "lan", "network", "phone", "远程", "局域网", "手机"],
      render: () => (
        <Select
          className="w-[280px]"
          dataSource={[
            {
              label: t("configuration.remoteAccess.mode.disabled"),
              value: RemoteAccessMode.Disabled.toString(),
            },
            {
              label: t("configuration.remoteAccess.mode.enabled"),
              value: RemoteAccessMode.Enabled.toString(),
            },
            {
              label: t("configuration.remoteAccess.mode.unrestricted"),
              value: RemoteAccessMode.Unrestricted.toString(),
            },
          ]}
          multiple={false}
          selectedKeys={[mode.toString()]}
          size="sm"
          onSelectionChange={(keys) => {
            const value = Array.from(keys)[0];

            if (value != undefined) {
              setMode(parseInt(value.toString(), 10) as RemoteAccessMode);
            }
          }}
        />
      ),
    },
  ];

  // Only worth showing once something can actually connect.
  if (mode !== RemoteAccessMode.Disabled) {
    items.push({
      id: "addresses",
      label: t("configuration.remoteAccess.address.label"),
      tip: t("configuration.remoteAccess.address.tip"),
      keywords: ["ip", "address", "url", "地址"],
      render: () =>
        settings?.addresses?.length ? (
          <div className="flex flex-col gap-1 items-end">
            {settings.addresses.map((a) => (
              <div key={a.url} className="flex items-center gap-2">
                <span className="text-sm font-mono">{a.url}</span>
                <Chip size="sm" variant="flat">
                  {a.interfaceName}
                </Chip>
                <Button isIconOnly size="sm" variant="light" onPress={() => copy(a.url!)}>
                  <AiOutlineCopy />
                </Button>
              </div>
            ))}
          </div>
        ) : (
          <span className="text-sm text-foreground-400">
            {t("configuration.remoteAccess.address.none")}
          </span>
        ),
    });

    items.push({
      id: "live-transcode",
      label: t("configuration.remoteAccess.liveTranscode.label"),
      tip: t("configuration.remoteAccess.liveTranscode.tip"),
      keywords: ["transcode", "ffmpeg", "video", "转码"],
      render: () => (
        <Switch
          isSelected={settings?.allowLiveTranscode ?? false}
          size="sm"
          onValueChange={async (checked) => {
            const rsp = await BApi.remoteAccess.setRemoteAccessLiveTranscode({ allow: checked });

            if (!rsp.code) {
              toast.success(t("common.success.saved"));
              await load();
            }
          }}
        />
      ),
    });

    if (mode === RemoteAccessMode.Unrestricted) {
      items.push({
        id: "unrestricted-warning",
        label: t("configuration.remoteAccess.unrestrictedWarning.label"),
        keywords: ["warning", "security", "安全"],
        render: () => (
          <span className="text-sm text-warning">
            {t("configuration.remoteAccess.unrestrictedWarning.description")}
          </span>
        ),
      });
    }
  }

  return (
    <SettingsSection
      items={items}
      keywords={["remote", "lan", "远程", "局域网"]}
      query={query}
      title={t("configuration.remoteAccess.title")}
    />
  );
};

RemoteAccess.displayName = "RemoteAccess";

export default RemoteAccess;

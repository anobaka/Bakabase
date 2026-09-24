"use client";

import type { SettingItem } from "@/pages/configuration/components/SettingsSection";
import type {
  BakabaseServiceModelsViewRemoteAccessDeviceViewModel,
  BakabaseServiceModelsViewRemoteAccessPendingRequestViewModel,
  BakabaseServiceModelsViewRemoteAccessSettingsViewModel,
} from "@/sdk/Api";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { AiOutlineCopy } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { RemoteAccessMode } from "@/sdk/constants";
import { Button, Chip, Input, Modal, Select, Snippet, Switch } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import SettingsSection from "@/pages/configuration/components/SettingsSection";
import { millisecondsUntil, minutesUntil } from "@/core/serverTime";
import { remoteDevicePlatformLabelKey } from "@/core/remoteDevicePlatform";
import { useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";

interface RemoteAccessProps {
  query?: string;
}

/**
 * A device asking to be let in expires in minutes, so the page refreshes itself while
 * there is anything to act on. Idle otherwise — no timer runs when the list is empty
 * and no code is outstanding.
 */
const LivePollInterval = 5000;

const platformLabelKey = remoteDevicePlatformLabelKey;

const RemoteAccess: React.FC<RemoteAccessProps> = ({ query }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [settings, setSettings] =
    useState<BakabaseServiceModelsViewRemoteAccessSettingsViewModel>();

  /**
   * The digits, held only in this tab. The server keeps a digest and will never hand
   * the code back, so leaving this page loses it — which is why the row says so.
   */
  const [issuedCode, setIssuedCode] = useState<{ code: string; expiresAt: string }>();
  const [now, setNow] = useState(() => Date.now());
  const reloadClientContext = useRemoteAccessStore((state) => state.load);
  const isPureClient = useIsPureClient();
  const loading = useRef(false);

  const load = useCallback(async () => {
    if (loading.current) {
      return;
    }
    loading.current = true;
    try {
      const rsp = await BApi.remoteAccess.getRemoteAccessSettings();

      if (!rsp.code && rsp.data) {
        setSettings(rsp.data);
      }
    } finally {
      loading.current = false;
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const mode = settings?.mode ?? RemoteAccessMode.Disabled;
  const pendingRequests = settings?.pendingRequests ?? [];
  const devices = settings?.devices ?? [];
  const hasLiveState = pendingRequests.length > 0 || !!settings?.pairingCode;

  useEffect(() => {
    if (!hasLiveState) {
      return;
    }

    const timer = setInterval(() => {
      setNow(Date.now());
      load();
    }, LivePollInterval);

    return () => clearInterval(timer);
  }, [hasLiveState, load]);

  // Drop the digits the moment they stop working, so nobody types a dead code. Read
  // through parseServerTime: the server's timestamps carry no zone, and taking them as
  // local time made a code look expired the instant it was issued — the page cleared it
  // before anyone could read it.
  useEffect(() => {
    if (issuedCode && millisecondsUntil(issuedCode.expiresAt, now) <= 0) {
      setIssuedCode(undefined);
    }
  }, [issuedCode, now]);

  const setMode = async (newMode: RemoteAccessMode) => {
    const rsp = await BApi.remoteAccess.setRemoteAccessMode({ mode: newMode });

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

  const issueCode = async () => {
    const rsp = await BApi.remoteAccess.issueRemoteAccessPairingCode();

    if (!rsp.code && rsp.data) {
      setIssuedCode({ code: rsp.data.code!, expiresAt: rsp.data.expiresAt! });
      setNow(Date.now());
      await load();
    }
  };

  const approve = async (request: BakabaseServiceModelsViewRemoteAccessPendingRequestViewModel) => {
    const rsp = await BApi.remoteAccess.approveRemoteDevicePairingRequest(request.id!);

    if (!rsp.code) {
      toast.success(t("configuration.remoteAccess.pairing.approved", { name: request.deviceName }));
    }
    await load();
  };

  const reject = async (request: BakabaseServiceModelsViewRemoteAccessPendingRequestViewModel) => {
    await BApi.remoteAccess.rejectRemoteDevicePairingRequest(request.id!);
    await load();
  };

  const revoke = (device: BakabaseServiceModelsViewRemoteAccessDeviceViewModel) => {
    createPortal(Modal, {
      size: "md",
      title: t("configuration.remoteAccess.devices.revoke.title"),
      children: t("configuration.remoteAccess.devices.revoke.description", { name: device.name }),
      defaultVisible: true,
      footer: { actions: ["ok", "cancel"] },
      onOk: async () => {
        await BApi.remoteAccess.revokeRemoteAccessDevice(device.id!);
        await load();
      },
    });
  };

  const rename = (device: BakabaseServiceModelsViewRemoteAccessDeviceViewModel) => {
    let name = device.name ?? "";

    createPortal(Modal, {
      size: "md",
      title: t("configuration.remoteAccess.devices.rename.title"),
      children: (
        <Input
          defaultValue={name}
          label={t("configuration.remoteAccess.devices.rename.label")}
          onValueChange={(v) => {
            name = v;
          }}
        />
      ),
      defaultVisible: true,
      footer: { actions: ["ok", "cancel"] },
      onOk: async () => {
        await BApi.remoteAccess.renameRemoteAccessDevice(device.id!, { name });
        await load();
      },
    });
  };

  const minutesLeft = (serverTime?: string | null) => minutesUntil(serverTime, now);

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

    // Unrestricted ignores pairing entirely, so offering the switch there would be a
    // control that does nothing.
    if (mode === RemoteAccessMode.Enabled) {
      items.push({
        id: "require-pairing",
        label: t("configuration.remoteAccess.requirePairing.label"),
        tip: t("configuration.remoteAccess.requirePairing.tip"),
        keywords: ["pair", "pairing", "device", "配对", "设备"],
        render: () => (
          <Switch
            isSelected={settings?.requirePairing ?? false}
            size="sm"
            onValueChange={async (checked) => {
              const rsp = await BApi.remoteAccess.setRemoteAccessRequirePairing({
                require: checked,
              });

              if (!rsp.code) {
                toast.success(t("common.success.saved"));
                await load();
              }
            }}
          />
        ),
      });
    }

    items.push({
      id: "pairing-code",
      label: t("configuration.remoteAccess.pairingCode.label"),
      tip: t("configuration.remoteAccess.pairingCode.tip"),
      keywords: ["pair", "code", "配对", "验证码"],
      render: () => (
        <div className="flex flex-col gap-1 items-end">
          {issuedCode ? (
            <>
              <Snippet
                hideSymbol
                classNames={{ pre: "text-xl font-mono tracking-[0.4em]" }}
                size="lg"
                variant="bordered"
              >
                {issuedCode.code}
              </Snippet>
              <span className="text-xs text-foreground-400">
                {t("configuration.remoteAccess.pairingCode.shownOnce", {
                  minutes: minutesLeft(issuedCode.expiresAt),
                })}
              </span>
            </>
          ) : settings?.pairingCode ? (
            <span className="text-xs text-foreground-400">
              {t("configuration.remoteAccess.pairingCode.outstanding", {
                minutes: minutesLeft(settings.pairingCode.expiresAt),
                attempts: settings.pairingCode.remainingAttempts,
              })}
            </span>
          ) : null}
          <Button size="sm" variant="flat" onPress={issueCode}>
            {t(
              settings?.pairingCode || issuedCode
                ? "configuration.remoteAccess.pairingCode.reissue"
                : "configuration.remoteAccess.pairingCode.issue",
            )}
          </Button>
        </div>
      ),
    });

    if (pendingRequests.length > 0) {
      items.push({
        id: "pending-requests",
        label: t("configuration.remoteAccess.pending.label"),
        tip: t("configuration.remoteAccess.pending.tip"),
        keywords: ["pair", "request", "approve", "配对", "请求", "批准"],
        render: () => (
          <div className="flex flex-col gap-2 items-end">
            {pendingRequests.map((r) => (
              <div key={r.id} className="flex items-center gap-2">
                <div className="flex flex-col items-end">
                  <span className="text-sm">{r.deviceName}</span>
                  <span className="text-xs text-foreground-400">
                    {t(platformLabelKey(r.platform))}
                    {r.remoteAddress ? ` · ${r.remoteAddress}` : ""} ·{" "}
                    {t("configuration.remoteAccess.pending.expiresIn", {
                      minutes: minutesLeft(r.expiresAt),
                    })}
                  </span>
                </div>
                <Button color="primary" size="sm" onPress={() => approve(r)}>
                  {t("configuration.remoteAccess.pending.approve")}
                </Button>
                <Button size="sm" variant="light" onPress={() => reject(r)}>
                  {t("configuration.remoteAccess.pending.reject")}
                </Button>
              </div>
            ))}
          </div>
        ),
      });
    }

    items.push({
      id: "devices",
      label: t("configuration.remoteAccess.devices.label"),
      tip: t("configuration.remoteAccess.devices.tip"),
      keywords: ["device", "paired", "revoke", "设备", "配对", "撤销"],
      render: () =>
        devices.length ? (
          <div className="flex flex-col gap-2 items-end">
            {devices.map((d) => (
              <div key={d.id} className="flex items-center gap-2">
                <div className="flex flex-col items-end">
                  <span className="text-sm">{d.name}</span>
                  <span className="text-xs text-foreground-400">
                    {t(platformLabelKey(d.platform))} ·{" "}
                    {d.lastSeenAt
                      ? t("configuration.remoteAccess.devices.lastSeen", {
                          time: new Date(d.lastSeenAt).toLocaleString(),
                        })
                      : t("configuration.remoteAccess.devices.neverSeen")}
                  </span>
                </div>
                <Button size="sm" variant="light" onPress={() => rename(d)}>
                  {t("configuration.remoteAccess.devices.rename.action")}
                </Button>
                <Button color="danger" size="sm" variant="light" onPress={() => revoke(d)}>
                  {t("configuration.remoteAccess.devices.revoke.action")}
                </Button>
              </div>
            ))}
          </div>
        ) : (
          <span className="text-sm text-foreground-400">
            {t("configuration.remoteAccess.devices.none")}
          </span>
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
      // Whose remote access this is. For a managed server shown in this window every
      // switch here is forwarded — "only this machine" means the machine holding the
      // library, not the one the window is open on.
      title={t(
        isPureClient
          ? "configuration.remoteAccess.title.server"
          : "configuration.remoteAccess.title",
      )}
    />
  );
};

RemoteAccess.displayName = "RemoteAccess";

export default RemoteAccess;

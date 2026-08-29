"use client";

import type { DevicePlatform } from "./playerSchemes";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { Resource as ResourceModel } from "@/core/models/Resource";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { AiOutlineCopy, AiOutlinePlayCircle } from "react-icons/ai";

import { detectPlatform, schemesForPlatform } from "./playerSchemes";

import { Button, Chip, Modal, Select } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

const PlatformPreferenceKey = "bakabase-remote-platform";

type Props = DestroyableProps & {
  resource: ResourceModel;
  /** Absolute path of the file to play, as the server knows it. */
  filePath: string;
  /** Opens the file in the built-in browser player instead. */
  onPlayInBrowser: () => void;
};

const platformOptions: DevicePlatform[] = ["android", "ios", "windows", "macos", "linux"];

const PlayOnThisDevice = ({ resource, filePath, onPlayInBrowser, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [platform, setPlatform] = useState<DevicePlatform>(() => {
    const stored = localStorage.getItem(PlatformPreferenceKey) as DevicePlatform | null;

    if (stored && platformOptions.includes(stored)) {
      return stored;
    }

    const detected = detectPlatform();

    return detected === "unknown" ? "windows" : detected;
  });

  /**
   * Absolute because the link leaves the browser: a native player has no page to
   * resolve a relative URL against.
   */
  const streamUrl = useMemo(
    () => `${window.location.origin}${BApi.file.getRawFileUrl({ fullname: filePath })}`,
    [filePath],
  );

  const schemes = useMemo(() => schemesForPlatform(platform), [platform]);

  const title = resource.displayName ?? filePath.split(/[/\\]/).pop() ?? "";

  const recordPlay = () => {
    // Fire and forget: the hand-off is a navigation, and history must not delay
    // or block it. The server only learns that playback was started, not how it
    // went — nothing about a native player is observable from here.
    BApi.resource.markResourceAsPlayed(resource.id, { item: filePath }).catch(() => {});
  };

  const openIn = (build: (url: string, title: string) => string) => {
    recordPlay();
    window.location.href = build(streamUrl, title);
  };

  const copyStreamUrl = async () => {
    try {
      await navigator.clipboard.writeText(streamUrl);
      toast.success(t("resource.playOnThisDevice.copied"));
    } catch {
      toast.error(t("resource.playOnThisDevice.copyFailed"));
    }
  };

  return (
    <Modal
      defaultVisible
      footer={false}
      size="lg"
      title={t("resource.playOnThisDevice.title")}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-2">
          <Button
            fullWidth
            color="primary"
            startContent={<AiOutlinePlayCircle />}
            onPress={() => {
              onPlayInBrowser();
              onDestroyed?.();
            }}
          >
            {t("resource.playOnThisDevice.playInBrowser")}
          </Button>
          <div className="text-xs text-foreground-400">
            {t("resource.playOnThisDevice.playInBrowserHint")}
          </div>
        </div>

        <div className="flex flex-col gap-2">
          <div className="flex items-center justify-between gap-2">
            <span className="text-sm font-medium">
              {t("resource.playOnThisDevice.nativePlayers")}
            </span>
            <Select
              className="w-[160px]"
              dataSource={platformOptions.map((p) => ({
                label: t(`resource.playOnThisDevice.platform.${p}`),
                value: p,
              }))}
              multiple={false}
              selectedKeys={[platform]}
              size="sm"
              onSelectionChange={(keys) => {
                const value = Array.from(keys)[0]?.toString() as DevicePlatform | undefined;

                if (value) {
                  setPlatform(value);
                  localStorage.setItem(PlatformPreferenceKey, value);
                }
              }}
            />
          </div>

          <div className="text-xs text-foreground-400">
            {t("resource.playOnThisDevice.nativePlayersHint")}
          </div>

          <div className="grid grid-cols-2 gap-2">
            {schemes.map((scheme) => (
              <Button
                key={scheme.id}
                className="justify-start"
                size="sm"
                variant="flat"
                onPress={() => openIn(scheme.build)}
              >
                <span className="truncate">{scheme.name}</span>
                {scheme.needsSetup && (
                  <Chip color="warning" size="sm" variant="flat">
                    {t("resource.playOnThisDevice.needsSetup")}
                  </Chip>
                )}
                {scheme.unofficial && (
                  <Chip size="sm" variant="flat">
                    {t("resource.playOnThisDevice.unofficial")}
                  </Chip>
                )}
              </Button>
            ))}
          </div>
        </div>

        <div className="flex flex-col gap-2">
          <Button
            fullWidth
            size="sm"
            startContent={<AiOutlineCopy />}
            variant="light"
            onPress={copyStreamUrl}
          >
            {t("resource.playOnThisDevice.copyStreamUrl")}
          </Button>
          <div className="text-xs text-foreground-400">
            {t("resource.playOnThisDevice.copyStreamUrlHint")}
          </div>
        </div>
      </div>
    </Modal>
  );
};

PlayOnThisDevice.displayName = "PlayOnThisDevice";

export default PlayOnThisDevice;

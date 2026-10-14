"use client";

import type { DevicePlatform } from "./playerSchemes";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { Resource as ResourceModel } from "@/core/models/Resource";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCopy, AiOutlinePlayCircle } from "react-icons/ai";

import { DesktopPlaybackGuideButton } from "../PlayControl/DesktopPlaybackGuide";
import { pathMediaType } from "../PlayControl/playbackPolicy";

import {
  preferredDevicePlatform,
  PlatformPreferenceKey,
  isNativePlayerMediaType,
  schemesForPlatform,
} from "./playerSchemes";

import { Button, Chip, Modal, Select, toast } from "@/components/bakaui";
import { copyTextToClipboard } from "@/core/clipboard";
import BApi from "@/sdk/BApi";
import { MediaType } from "@/sdk/constants";

const platformOptions: DevicePlatform[] = ["android", "ios", "windows", "macos", "linux"];

type Props = DestroyableProps & {
  resource: ResourceModel;
  filePath: string;
  isMixed?: boolean;
  onPlayVideos?: () => void;
  onPlayInBrowser: () => void;
  /** The desktop/relay's existing configured-player action, never a browser URI handler. */
  onPlayNative?: () => void;
  unavailableReason?: string;
  /** Only offer guidance after an actual file candidate has been discovered. */
  offerDesktopGuidance?: boolean;
};

const PlayOnThisDevice = ({
  resource,
  filePath,
  isMixed = false,
  onPlayVideos,
  onPlayInBrowser,
  onPlayNative,
  onDestroyed,
  unavailableReason,
  offerDesktopGuidance = false,
}: Props) => {
  const { t } = useTranslation();
  const [copyFeedback, setCopyFeedback] = useState<string>();
  const [platform, setPlatform] = useState<DevicePlatform>(preferredDevicePlatform);
  const mediaType = useMemo(
    () => (isMixed ? MediaType.Unknown : pathMediaType(filePath)),
    [filePath, isMixed],
  );
  const nativePlaybackSupported = isNativePlayerMediaType(mediaType);
  const streamUrl = useMemo(
    () => new URL(BApi.file.getRawFileUrl({ fullname: filePath }), window.location.origin).href,
    [filePath],
  );
  const schemes = useMemo(() => schemesForPlatform(platform, mediaType), [platform, mediaType]);
  const title = resource.displayName ?? filePath.split(/[/\\]/).pop() ?? "";
  const openIn = (build: (url: string, title: string, mediaType: MediaType) => string) => {
    BApi.resource.markResourceAsPlayed(resource.id, { item: filePath }).catch(() => {});
    // Keep custom protocol navigation within the click gesture.
    window.location.href = build(streamUrl, title, mediaType);
  };
  const copyStreamUrl = async () => {
    setCopyFeedback(undefined);
    try {
      await copyTextToClipboard(streamUrl);
      setCopyFeedback(t("resource.playOnThisDevice.copied"));
      toast.success(t("resource.playOnThisDevice.copied"));
    } catch {
      setCopyFeedback(t("resource.playOnThisDevice.copyFailed"));
      toast.danger(t("resource.playOnThisDevice.copyFailed"));
    }
  };

  return (
    <Modal
      defaultVisible
      footer={false}
      size="md"
      title={t("resource.playOnThisDevice.title")}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-3">
        {unavailableReason && (
          <p className="rounded-lg bg-warning-50 p-3 text-sm text-warning-700" role="alert">
            {unavailableReason}
          </p>
        )}
        <Button
          fullWidth
          color="primary"
          startContent={<AiOutlinePlayCircle />}
          onPress={() => {
            onPlayInBrowser();
            onDestroyed?.();
          }}
        >
          {t(
            nativePlaybackSupported
              ? "resource.playOnThisDevice.playInBrowser"
              : "resource.playOnThisDevice.viewInBrowser",
          )}
        </Button>
        {onPlayVideos && (
          <Button
            fullWidth
            variant="flat"
            onPress={() => {
              onPlayVideos();
              onDestroyed?.();
            }}
          >
            {t("resource.playback.playVideos")}
          </Button>
        )}
        {onPlayNative ? (
          <Button
            fullWidth
            variant="flat"
            onPress={() => {
              onPlayNative();
              onDestroyed?.();
            }}
          >
            {t("resource.playOnThisDevice.desktopPlayer")}
          </Button>
        ) : (
          !isMixed &&
          nativePlaybackSupported && (
            <div className="flex flex-col gap-2">
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-medium">
                  {t("resource.playOnThisDevice.nativePlayers")}
                </span>
                <Select
                  className="w-[140px]"
                  dataSource={platformOptions.map((p) => ({
                    label: t(`resource.playOnThisDevice.platform.${p}`),
                    value: p,
                  }))}
                  multiple={false}
                  placeholder={t("resource.playOnThisDevice.choosePlatform")}
                  selectedKeys={platform === "unknown" ? [] : [platform]}
                  size="sm"
                  onSelectionChange={(keys) => {
                    const value = Array.from(keys)[0]?.toString() as DevicePlatform | undefined;

                    if (value) {
                      setPlatform(value);
                      try {
                        localStorage.setItem(PlatformPreferenceKey, value);
                      } catch {
                        /* Best-effort system fallback. */
                      }
                    }
                  }}
                />
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
          )
        )}
        {!onPlayNative && offerDesktopGuidance && <DesktopPlaybackGuideButton />}
        <details className="rounded-lg border border-default-200 px-3 py-2">
          <summary className="cursor-pointer text-sm text-foreground-500">
            {t("resource.playOnThisDevice.moreOptions")}
          </summary>
          <div className="flex flex-col gap-2 pt-3">
            <p className="text-xs text-foreground-400">
              {t(
                isMixed
                  ? "resource.playback.mixedHint"
                  : nativePlaybackSupported
                    ? "resource.playOnThisDevice.nativePlayersHint"
                    : "resource.playOnThisDevice.fileBrowserHint",
              )}
            </p>
            {!isMixed && (
              <>
                <label className="flex flex-col gap-1 text-sm">
                  <span>
                    {t(
                      nativePlaybackSupported
                        ? "resource.playOnThisDevice.streamUrl"
                        : "resource.playOnThisDevice.fileUrl",
                    )}
                  </span>
                  <textarea
                    readOnly
                    className="w-full resize-none rounded-lg border border-default-200 bg-default-100 p-2 font-mono text-xs"
                    rows={2}
                    value={streamUrl}
                    onFocus={(event) => event.currentTarget.select()}
                  />
                </label>
                <Button
                  fullWidth
                  size="sm"
                  startContent={<AiOutlineCopy />}
                  variant="light"
                  onPress={copyStreamUrl}
                >
                  {t("resource.playOnThisDevice.copyStreamUrl")}
                </Button>
                <div aria-live="polite" className="text-xs">
                  {copyFeedback}
                </div>
              </>
            )}
          </div>
        </details>
      </div>
    </Modal>
  );
};

PlayOnThisDevice.displayName = "PlayOnThisDevice";
export default PlayOnThisDevice;

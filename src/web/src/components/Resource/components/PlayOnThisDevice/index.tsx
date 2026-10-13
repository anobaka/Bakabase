"use client";

import type { DevicePlatform } from "./playerSchemes";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { Resource as ResourceModel } from "@/core/models/Resource";
import type { PlaybackKind, PlaybackPreference } from "../PlayControl/playbackPreferences";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCopy, AiOutlinePlayCircle } from "react-icons/ai";

import { pathMediaType, playbackKindForPath } from "../PlayControl/playbackPreferences";

import {
  preferredDevicePlatform,
  PlatformPreferenceKey,
  isNativePlayerMediaType,
  schemesForPlatform,
} from "./playerSchemes";

import { Button, Checkbox, Chip, Modal, Select, toast } from "@/components/bakaui";
import { copyTextToClipboard } from "@/core/clipboard";
import BApi from "@/sdk/BApi";
import { MediaType } from "@/sdk/constants";

const platformOptions: DevicePlatform[] = ["android", "ios", "windows", "macos", "linux"];

type Props = DestroyableProps & {
  resource: ResourceModel;
  filePath: string;
  kind?: PlaybackKind;
  isMixed?: boolean;
  onPlayVideos?: () => void;
  onPlayInBrowser: () => void;
  /** The desktop/relay's existing configured-player action, never a browser URI handler. */
  onPlayNative?: () => void;
  onRememberChoice?: (kind: PlaybackKind, preference: PlaybackPreference) => Promise<void>;
};

const PlayOnThisDevice = ({
  resource,
  filePath,
  kind: kindOverride,
  isMixed = false,
  onPlayVideos,
  onPlayInBrowser,
  onPlayNative,
  onRememberChoice,
  onDestroyed,
}: Props) => {
  const { t } = useTranslation();
  const [copyFeedback, setCopyFeedback] = useState<string>();
  const [rememberChoice, setRememberChoice] = useState(false);
  const [platform, setPlatform] = useState<DevicePlatform>(preferredDevicePlatform);
  const mediaType = useMemo(
    () => (isMixed ? MediaType.Unknown : pathMediaType(filePath)),
    [filePath, isMixed],
  );
  const kind = kindOverride ?? playbackKindForPath(filePath);
  const nativePlaybackSupported = isNativePlayerMediaType(mediaType);
  const streamUrl = useMemo(
    () => new URL(BApi.file.getRawFileUrl({ fullname: filePath }), window.location.origin).href,
    [filePath],
  );
  const schemes = useMemo(() => schemesForPlatform(platform, mediaType), [platform, mediaType]);
  const title = resource.displayName ?? filePath.split(/[/\\]/).pop() ?? "";
  const remember = (preference: PlaybackPreference, force = false) => {
    if (onRememberChoice && (rememberChoice || force)) {
      void onRememberChoice(kind, preference).catch(() =>
        toast.danger(t("resource.playOnThisDevice.preferenceFailed")),
      );
    }
  };
  const openIn = (
    id: string,
    build: (url: string, title: string, mediaType: MediaType) => string,
  ) => {
    remember({ mode: "native", nativeSchemeId: id });
    BApi.resource.markResourceAsPlayed(resource.id, { item: filePath }).catch(() => {});
    // Keep custom protocol navigation within the click gesture; saving a preference must not delay it.
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
        <Button
          fullWidth
          color="primary"
          startContent={<AiOutlinePlayCircle />}
          onPress={() => {
            remember({ mode: "browser" });
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
        {!isMixed && onPlayNative ? (
          <Button
            fullWidth
            variant="flat"
            onPress={() => {
              remember({ mode: "native" });
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
                    onPress={() => openIn(scheme.id, scheme.build)}
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
        {onRememberChoice && (
          <Checkbox isSelected={rememberChoice} size="sm" onValueChange={setRememberChoice}>
            {t("resource.playOnThisDevice.rememberChoice", {
              type: t(`resource.playback.kind.${kind}`),
              place: t(
                onPlayNative ? "resource.playback.place.device" : "resource.playback.place.browser",
              ),
            })}
          </Checkbox>
        )}
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
            {onRememberChoice && (
              <div className="flex flex-wrap gap-1">
                <Button
                  size="sm"
                  variant="light"
                  onPress={() => {
                    remember({ mode: "ask" }, true);
                    onDestroyed?.();
                  }}
                >
                  {t("resource.playback.askEveryTime")}
                </Button>
                <Button
                  size="sm"
                  variant="light"
                  onPress={() => {
                    remember({ mode: "automatic" }, true);
                    onDestroyed?.();
                  }}
                >
                  {t("resource.playback.resetDefault")}
                </Button>
              </div>
            )}
          </div>
        </details>
      </div>
    </Modal>
  );
};

PlayOnThisDevice.displayName = "PlayOnThisDevice";
export default PlayOnThisDevice;

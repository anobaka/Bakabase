import { useState } from "react";
import { useTranslation } from "react-i18next";

import { playbackKinds, type PlaybackKind, type PlaybackPreference } from "./playbackPreferences";
import { usePlaybackPreferences } from "./usePlaybackPreferences";

import { Select, toast } from "@/components/bakaui";
import {
  playerSchemes,
  preferredDevicePlatform,
  schemesForPlatform,
} from "@/components/Resource/components/PlayOnThisDevice/playerSchemes";
import { MediaType } from "@/sdk/constants";

/** Reuses the viewer's existing browser/device defaults; never changes server resource profiles. */
const PlaybackPreferencesSettings = () => {
  const { t } = useTranslation();
  const playback = usePlaybackPreferences();
  const [saving, setSaving] = useState<PlaybackKind>();
  const platform = preferredDevicePlatform();

  const save = async (kind: PlaybackKind, value: string) => {
    const preference: PlaybackPreference = value.startsWith("native:")
      ? { mode: "native", nativeSchemeId: value.slice("native:".length) }
      : { mode: value as PlaybackPreference["mode"] };

    setSaving(kind);
    try {
      await playback.save(kind, preference);
    } catch {
      toast.danger(t("resource.playbackSettings.saveFailed"));
    } finally {
      setSaving(undefined);
    }
  };

  return (
    <div className="flex max-w-2xl flex-col gap-4">
      <div>
        <h3 className="text-sm font-medium">{t("resource.playbackSettings.title")}</h3>
        <p className="mt-1 text-xs text-default-500">
          {t(
            playback.desktop
              ? "resource.playbackSettings.deviceScope"
              : "resource.playbackSettings.browserScope",
          )}
        </p>
        <p className="mt-1 text-xs text-default-500">
          {t(
            playback.desktop
              ? "resource.playbackSettings.automaticDevice"
              : "resource.playbackSettings.automaticBrowser",
          )}
        </p>
      </div>
      <div className="divide-y divide-default-100 rounded-lg border border-default-200 px-3">
        {playbackKinds.map((kind) => {
          const preference = playback.preferences[kind];
          const value =
            preference.mode === "native" && !playback.desktop && preference.nativeSchemeId
              ? `native:${preference.nativeSchemeId}`
              : preference.mode;
          const options: { value: string; label: string; description?: string }[] = [
            { value: "automatic", label: t("resource.playbackSettings.automatic") },
            { value: "browser", label: t("resource.openingRules.browser") },
            { value: "ask", label: t("resource.playback.askEveryTime") },
          ];

          if (playback.desktop) {
            options.splice(2, 0, {
              value: "native",
              label: t("resource.openingRules.native"),
            });
          } else if (kind === "audio" || kind === "video") {
            options.push(
              ...schemesForPlatform(
                platform,
                kind === "audio" ? MediaType.Audio : MediaType.Video,
              ).map((scheme) => ({
                value: `native:${scheme.id}`,
                label: t("resource.playback.usePlayer", { player: scheme.name }),
                description: scheme.needsSetup
                  ? t("resource.playbackSettings.protocolSetup")
                  : undefined,
              })),
            );
          }
          // A saved protocol can become unavailable after a platform change. Keep
          // it visible and unchanged until the user explicitly selects a replacement.
          if (!options.some((option) => option.value === value)) {
            const name = playerSchemes.find(
              (scheme) => scheme.id === preference.nativeSchemeId,
            )?.name;

            options.push({
              value,
              label: name
                ? t("resource.playback.usePlayer", { player: name })
                : t("resource.openingRules.native"),
              description: t("resource.playbackSettings.unavailablePreference"),
            });
          }

          return (
            <div key={kind} className="flex flex-wrap items-center justify-between gap-3 py-2">
              <span className="text-sm">{t(`resource.playback.kind.${kind}`)}</span>
              <Select
                disallowEmptySelection
                aria-label={t("resource.playbackSettings.kind", {
                  type: t(`resource.playback.kind.${kind}`),
                })}
                className="w-56 max-w-full"
                dataSource={options}
                isDisabled={!playback.ready || !!saving}
                isLoading={saving === kind}
                selectedKeys={[value]}
                size="sm"
                onSelectionChange={(keys) => {
                  const next = Array.from(keys)[0]?.toString();

                  if (next && next !== value) void save(kind, next);
                }}
              />
            </div>
          );
        })}
      </div>
    </div>
  );
};

export default PlaybackPreferencesSettings;

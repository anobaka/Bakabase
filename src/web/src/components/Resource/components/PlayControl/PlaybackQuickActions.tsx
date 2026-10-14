import type { PlayControlPortalProps } from ".";

import { GlobalOutlined, MoreOutlined, VideoCameraOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import OriginalSourceMenu from "./OriginalSourceMenu";

import { Button, Tooltip } from "@/components/bakaui";
import { DataOrigin } from "@/sdk/constants";
import { PlaybackOverridesButton } from "@/features/playback/overrides";

/** Compact, explicit alternatives shared by resource cards and resource details. */
const PlaybackQuickActions = ({
  browserPlaybackAvailable,
  browserPlaybackIsDefault,
  playbackReady,
  sources,
  onPlayInBrowser,
  onChoosePlayback,
  onPlayVideos,
  openingRules,
  originalSourceActions,
  className,
}: PlayControlPortalProps & { className?: string }) => {
  const { t } = useTranslation();

  if (!browserPlaybackAvailable)
    return <OriginalSourceMenu actions={originalSourceActions} className={className} />;
  const mainAlreadyOpensBrowser =
    browserPlaybackIsDefault && sources[0]?.source === DataOrigin.FileSystem;

  return (
    <>
      {openingRules && <PlaybackOverridesButton {...openingRules} compact />}
      {!mainAlreadyOpensBrowser && onPlayInBrowser && (
        <Tooltip content={t("resource.playback.browserQuickAction")}>
          <Button
            isIconOnly
            aria-label={t("resource.playback.browserQuickAction")}
            className={className}
            isDisabled={playbackReady === false}
            size="sm"
            variant="light"
            onPress={onPlayInBrowser}
          >
            <GlobalOutlined className="text-lg" />
          </Button>
        </Tooltip>
      )}
      {onPlayVideos && (
        <Tooltip content={t("resource.playback.playVideos")}>
          <Button
            isIconOnly
            aria-label={t("resource.playback.playVideos")}
            className={className}
            isDisabled={playbackReady === false}
            size="sm"
            variant="light"
            onPress={onPlayVideos}
          >
            <VideoCameraOutlined className="text-lg" />
          </Button>
        </Tooltip>
      )}
      {onChoosePlayback && (
        <Tooltip content={t("resource.playback.chooseMethod")}>
          <Button
            isIconOnly
            aria-label={t("resource.playback.chooseMethod")}
            className={className}
            isDisabled={playbackReady === false}
            size="sm"
            variant="light"
            onPress={onChoosePlayback}
          >
            <MoreOutlined className="text-lg" />
          </Button>
        </Tooltip>
      )}
      <OriginalSourceMenu actions={originalSourceActions} className={className} />
    </>
  );
};

export default PlaybackQuickActions;

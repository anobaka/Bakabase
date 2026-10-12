import type { PlayControlPortalProps } from ".";

import { GlobalOutlined, MoreOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import { Button, Tooltip } from "@/components/bakaui";
import { DataOrigin } from "@/sdk/constants";

/** Compact, explicit alternatives shared by resource cards and resource details. */
const PlaybackQuickActions = ({
  browserPlaybackAvailable,
  browserPlaybackIsDefault,
  playbackReady,
  sources,
  onPlayInBrowser,
  onChoosePlayback,
  className,
}: PlayControlPortalProps & { className?: string }) => {
  const { t } = useTranslation();

  if (!browserPlaybackAvailable) return null;
  const mainAlreadyOpensBrowser =
    browserPlaybackIsDefault && sources[0]?.source === DataOrigin.FileSystem;

  return (
    <>
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
    </>
  );
};

export default PlaybackQuickActions;

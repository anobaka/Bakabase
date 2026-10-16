import type { PlayControlPortalProps } from ".";

import { VideoCameraOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import OriginalSourceMenu from "./OriginalSourceMenu";

import { Button, Tooltip } from "@/components/bakaui";

/** Resource-specific actions. Opening preferences belong in resource display options. */
const PlaybackQuickActions = ({
  playbackReady,
  onPlayVideos,
  originalSourceActions,
  className,
}: PlayControlPortalProps & { className?: string }) => {
  const { t } = useTranslation();

  return (
    <>
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
      <OriginalSourceMenu actions={originalSourceActions} className={className} />
    </>
  );
};

export default PlaybackQuickActions;

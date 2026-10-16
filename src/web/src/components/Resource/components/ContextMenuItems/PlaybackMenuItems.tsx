import type { Resource } from "@/core/models/Resource";
import type { PlayControlPortalProps } from "../PlayControl";

import { DesktopOutlined, ExportOutlined, VideoCameraOutlined } from "@ant-design/icons";
import { MenuItem } from "@szhsin/react-menu";
import { useTranslation } from "react-i18next";

import PlayControl from "../PlayControl";

const VideoMenuItem = ({
  onPlayVideos,
  playbackReady,
  originalSourceActions,
  onDesktopPlaybackGuide,
}: PlayControlPortalProps) => {
  const { t } = useTranslation();

  return (
    <>
      {onPlayVideos && (
        <MenuItem disabled={playbackReady === false} onClick={onPlayVideos}>
          <div className="flex items-center gap-2">
            <VideoCameraOutlined aria-hidden className="text-base" />
            <span className="resource-context-menu__label">
              {t("resource.playback.playVideos")}
            </span>
          </div>
        </MenuItem>
      )}
      {onDesktopPlaybackGuide && (
        <MenuItem onClick={onDesktopPlaybackGuide}>
          <div className="flex items-center gap-2">
            <DesktopOutlined aria-hidden className="text-base" />
            <span className="resource-context-menu__label">
              {t("resource.desktopPlayback.action")}
            </span>
          </div>
        </MenuItem>
      )}
      {originalSourceActions?.map((action) => (
        <MenuItem key={action.key} onClick={action.onOpen}>
          <div className="flex items-center gap-2">
            <ExportOutlined aria-hidden className="text-base" />
            <span className="resource-context-menu__label">{action.label}</span>
          </div>
        </MenuItem>
      ))}
    </>
  );
};

/** Lazy context menus use the same discovery and single-video selection as cover/details controls. */
const PlaybackMenuItems = ({ resource }: { resource: Resource }) => (
  <PlayControl PortalComponent={VideoMenuItem} resource={resource} />
);

export default PlaybackMenuItems;

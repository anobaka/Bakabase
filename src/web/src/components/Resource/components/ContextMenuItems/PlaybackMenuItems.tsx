import type { Resource } from "@/core/models/Resource";
import type { PlayControlPortalProps } from "../PlayControl";

import { DesktopOutlined, ExportOutlined, VideoCameraOutlined } from "@ant-design/icons";
import { MenuItem } from "@szhsin/react-menu";
import { useTranslation } from "react-i18next";

import PlayControl from "../PlayControl";

import ContextMenuRow from "./ContextMenuRow";

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
          <ContextMenuRow icon={<VideoCameraOutlined />}>
            {t("resource.playback.playVideos")}
          </ContextMenuRow>
        </MenuItem>
      )}
      {onDesktopPlaybackGuide && (
        <MenuItem onClick={onDesktopPlaybackGuide}>
          <ContextMenuRow icon={<DesktopOutlined />}>
            {t("resource.desktopPlayback.action")}
          </ContextMenuRow>
        </MenuItem>
      )}
      {originalSourceActions?.map((action) => (
        <MenuItem key={action.key} onClick={action.onOpen}>
          <ContextMenuRow icon={<ExportOutlined />}>{action.label}</ContextMenuRow>
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

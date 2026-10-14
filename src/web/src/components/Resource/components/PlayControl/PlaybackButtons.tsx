import type { PlayControlPortalProps } from ".";

import React from "react";
import { useTranslation } from "react-i18next";
import {
  FolderOpenOutlined,
  LoadingOutlined,
  PlayCircleOutlined,
  QuestionCircleOutlined,
} from "@ant-design/icons";
import { AiOutlineFolderOpen, AiOutlinePlayCircle } from "react-icons/ai";

import PlaybackQuickActions from "./PlaybackQuickActions";

import { Button, Tooltip } from "@/components/bakaui";
import { SteamIcon } from "@/components/SourceIcons";
import { DataOrigin } from "@/sdk/constants";

/** Render a DataOrigin-specific icon for playable items */
const renderSourceIcon = (origin: DataOrigin, className: string): React.ReactNode => {
  switch (origin) {
    case DataOrigin.Steam:
      return <SteamIcon className={className} />;
    case DataOrigin.FileSystem:
    default:
      return <PlayCircleOutlined className={className} />;
  }
};

/**
 * Variant of renderSourceIcon used as the *main* play-button trigger:
 * brand icons get a small green play-circle overlaid bottom-right so the
 * affordance is unmistakable. FileSystem is already a play icon, so it's
 * returned as-is.
 */
const renderPlayButtonMainIcon = (origin: DataOrigin, className: string): React.ReactNode => {
  if (origin === DataOrigin.FileSystem) {
    return renderSourceIcon(origin, className);
  }

  return (
    <span className={`relative inline-flex items-center justify-center ${className}`}>
      {renderSourceIcon(origin, "")}
      <AiOutlinePlayCircle
        className="absolute right-0 bottom-0 text-success bg-content1 rounded-full"
        style={{ fontSize: "0.625em" }}
      />
    </span>
  );
};

type MenuEntry = {
  key: string;
  type: "source" | "openFolder" | "notFound";
  source?: DataOrigin;
  icon: React.ReactNode;
  label: string;
  onClick: () => void;
  isLoading?: boolean;
  isDisabled?: boolean;
};

// PlayButton component defined outside Resource to maintain stable reference
export const ResourceCardPlaybackButton: React.FC<PlayControlPortalProps> = (props) => {
  const {
    status,
    sources,
    hasPath,
    fsDiscoveryStatus,
    onPlaySource,
    onOpenFolder,
    openFolderLabel,
    onNotFound,
    triggerFsDiscovery,
    playbackCaption,
    playbackReady,
    sourceActionLabel,
    discoveryFailed,
    onDiscoveryFailure,
  } = props;
  const { t } = useTranslation();

  const fsTriggeredRef = React.useRef(false);

  const iconClass = "text-2xl";
  const buttonClass = "!p-0";

  // Keep source actions in the order chosen by the current device's playback preference.
  const entries: MenuEntry[] = React.useMemo(() => {
    if (status === "idle" || status === "loading") return [];

    const result: MenuEntry[] = [];

    for (const { source } of sources) {
      const isFs = source === DataOrigin.FileSystem;

      result.push({
        key: `source-${source}`,
        type: "source",
        source,
        icon:
          isFs && fsDiscoveryStatus === "loading" ? (
            <LoadingOutlined spin />
          ) : (
            renderSourceIcon(source, "text-base")
          ),
        label:
          isFs && playbackCaption
            ? playbackCaption
            : (sourceActionLabel?.(source) ??
              t("resource.playControl.menu.openVia", {
                source: t(`resource.source.${DataOrigin[source]}`),
              })),
        onClick: () => onPlaySource(source),
        isDisabled: isFs && playbackReady === false,
      });
    }

    // If FileSystem has no items yet but discovery is pending/idle, add a loading entry
    const hasFsSource = sources.some((s) => s.source === DataOrigin.FileSystem);

    if (!hasFsSource && (fsDiscoveryStatus === "idle" || fsDiscoveryStatus === "loading")) {
      result.push({
        key: "source-fs-loading",
        type: "source",
        source: DataOrigin.FileSystem,
        icon: <LoadingOutlined spin />,
        label: t("resource.playControl.tooltip.discoveringFiles"),
        onClick: () => {},
        isDisabled: true,
        isLoading: true,
      });
    }

    if (discoveryFailed) {
      result.push({
        key: "discoveryFailed",
        type: "notFound",
        icon: <QuestionCircleOutlined className="text-base text-warning" />,
        label: t("resource.playbackActions.discoveryFailed"),
        onClick: onDiscoveryFailure ?? onNotFound,
      });
    }

    if (!discoveryFailed && sources.length === 0 && fsDiscoveryStatus === "ready") {
      result.push({
        key: "notFound",
        type: "notFound",
        icon: <QuestionCircleOutlined className="text-base text-warning" />,
        label: t("resource.playControl.tooltip.notFound"),
        onClick: onNotFound,
      });
    }

    if (hasPath) {
      result.push({
        key: "openFolder",
        type: "openFolder",
        icon: <FolderOpenOutlined className="text-base" />,
        label: openFolderLabel,
        onClick: onOpenFolder,
      });
    }

    return result;
  }, [
    status,
    sources,
    fsDiscoveryStatus,
    hasPath,
    t,
    onPlaySource,
    onOpenFolder,
    openFolderLabel,
    onNotFound,
    playbackCaption,
    playbackReady,
    sourceActionLabel,
    discoveryFailed,
    onDiscoveryFailure,
  ]);

  // Trigger FS discovery when FS button is visible
  const fsIsVisible = entries.some((e) => e.source === DataOrigin.FileSystem);

  React.useEffect(() => {
    if (fsIsVisible && fsDiscoveryStatus === "idle" && !fsTriggeredRef.current) {
      fsTriggeredRef.current = true;
      triggerFsDiscovery();
    }
  }, [fsIsVisible, fsDiscoveryStatus, triggerFsDiscovery]);

  React.useEffect(() => {
    if (fsDiscoveryStatus === "idle") {
      fsTriggeredRef.current = false;
    }
  }, [fsDiscoveryStatus]);

  // Overall loading/idle — no items yet, possibly discovering
  if (status === "idle" || status === "loading") {
    return (
      <div className="hidden group-hover/cover:flex group-focus-within/cover:flex has-[[data-open=true]]:flex absolute left-0 bottom-0 z-[1]">
        <Button
          isDisabled
          isIconOnly
          aria-label={t("resource.playControl.tooltip.discoveringFiles")}
          className={buttonClass}
        >
          <LoadingOutlined spin className={iconClass} />
        </Button>
        <PlaybackQuickActions {...props} className="!p-0" />
      </div>
    );
  }

  const mainEntry = entries[0];

  if (!mainEntry) return null;

  // Dropdown entries = all entries except the main one (which is the trigger button)
  const dropdownEntries = entries.slice(1).filter((e) => !e.isDisabled);

  return (
    <div className="hidden group-hover/cover:flex group-focus-within/cover:flex has-[[data-open=true]]:flex has-[[data-bakabase-tooltip-open]]:flex absolute left-0 bottom-0 z-[1] group/play">
      <Tooltip content={mainEntry.label}>
        <Button
          isIconOnly
          aria-label={mainEntry.label}
          className={buttonClass}
          isDisabled={mainEntry.isDisabled}
          onPress={mainEntry.onClick}
        >
          {mainEntry.isLoading ? (
            <LoadingOutlined spin className={iconClass} />
          ) : mainEntry.type === "openFolder" ? (
            <AiOutlineFolderOpen className={iconClass} />
          ) : (
            renderPlayButtonMainIcon(mainEntry.source ?? DataOrigin.FileSystem, iconClass)
          )}
        </Button>
      </Tooltip>

      <PlaybackQuickActions {...props} className="!p-0" />

      {dropdownEntries.length > 0 && (
        <div className="hidden group-hover/play:block group-focus-within/play:block absolute left-full top-0 pl-1 z-10">
          <div className="flex flex-col gap-0.5 bg-content1 rounded-lg shadow-medium p-1 min-w-max">
            {dropdownEntries.map((entry) => (
              <Button
                key={entry.key}
                className="justify-start gap-2 px-2"
                size="sm"
                startContent={entry.icon}
                variant="light"
                onPress={entry.onClick}
              >
                {entry.label}
              </Button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};

export const ResourceDetailPlaybackButton = (props: PlayControlPortalProps) => {
  const {
    status,
    sources,
    onPlaySource,
    onNotFound,
    triggerFsDiscovery,
    fsDiscoveryStatus,
    playbackCaption,
    playbackReady,
    sourceActionLabel,
    discoveryFailed,
    onDiscoveryFailure,
  } = props;
  const { t } = useTranslation();

  React.useEffect(() => {
    if (fsDiscoveryStatus === "idle") triggerFsDiscovery();
  }, [fsDiscoveryStatus, triggerFsDiscovery]);

  const mainSource = sources[0];

  return (
    <div className="flex items-center gap-0.5">
      <Tooltip
        content={
          mainSource?.source === DataOrigin.FileSystem && playbackCaption
            ? playbackCaption
            : mainSource
              ? (sourceActionLabel?.(mainSource.source) ?? t("common.action.play"))
              : status === "failed"
                ? t("resource.playbackActions.discoveryFailed")
                : t("common.action.play")
        }
      >
        <Button
          isIconOnly
          aria-label={
            mainSource
              ? (sourceActionLabel?.(mainSource.source) ?? t("common.action.play"))
              : t("common.action.play")
          }
          color="primary"
          isDisabled={
            status === "loading" ||
            status === "idle" ||
            (mainSource?.source === DataOrigin.FileSystem && playbackReady === false)
          }
          onPress={() => (mainSource ? onPlaySource(mainSource.source) : onNotFound())}
        >
          {status === "loading" || status === "idle" ? (
            <LoadingOutlined spin className="text-lg" />
          ) : status === "not-found" || status === "failed" ? (
            <QuestionCircleOutlined className="text-lg text-warning" />
          ) : (
            <PlayCircleOutlined className="text-lg" />
          )}
        </Button>
      </Tooltip>
      {discoveryFailed && mainSource && (
        <Tooltip content={t("resource.playbackActions.discoveryFailed")}>
          <Button
            isIconOnly
            aria-label={t("resource.playbackActions.discoveryFailed")}
            color="warning"
            variant="light"
            onPress={onDiscoveryFailure}
          >
            <QuestionCircleOutlined />
          </Button>
        </Tooltip>
      )}
      <PlaybackQuickActions {...props} />
    </div>
  );
};

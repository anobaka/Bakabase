import type { PlayControlPortalProps } from ".";

import React from "react";
import { ControlledMenu, MenuItem } from "@szhsin/react-menu";
import { useTranslation } from "react-i18next";
import {
  DesktopOutlined,
  ExportOutlined,
  FolderOpenOutlined,
  LoadingOutlined,
  MoreOutlined,
  PlayCircleOutlined,
  QuestionCircleOutlined,
  VideoCameraOutlined,
} from "@ant-design/icons";
import { AiOutlineFolderOpen, AiOutlinePlayCircle } from "react-icons/ai";

import PlaybackQuickActions from "./PlaybackQuickActions";

import "../ContextMenu/index.css";

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
  type: "source" | "openFolder" | "notFound" | "desktopGuide" | "videos" | "original";
  source?: DataOrigin;
  icon: React.ReactNode;
  label: string;
  onClick: () => void;
  isLoading?: boolean;
  isDisabled?: boolean;
};

const firstMenuItem = { position: "first" as const, alwaysUpdate: true };

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
    onDesktopPlaybackGuide,
    onPlayVideos,
    originalSourceActions,
  } = props;
  const { t } = useTranslation();

  const fsTriggeredRef = React.useRef(false);
  const [menuOpen, setMenuOpen] = React.useState(false);
  const [tooltipOpen, setTooltipOpen] = React.useState(false);
  const [keyboardMenu, setKeyboardMenu] = React.useState(false);
  const actionsId = React.useId();
  const anchorRef = React.useRef<HTMLDivElement>(null);
  const pointerFocus = React.useRef(false);
  const menuRef = React.useRef<HTMLUListElement>(null);
  const closeTimer = React.useRef<ReturnType<typeof setTimeout>>();
  const cancelClose = () => clearTimeout(closeTimer.current);
  const closeMenu = () => {
    cancelClose();
    setMenuOpen(false);
    setKeyboardMenu(false);
  };
  const openMenu = (keyboard = false) => {
    cancelClose();
    setKeyboardMenu(keyboard);
    setTooltipOpen(false);
    setMenuOpen(true);
  };
  const scheduleClose = () => {
    cancelClose();
    closeTimer.current = setTimeout(() => {
      if (!keyboardMenu || !menuRef.current?.contains(document.activeElement)) closeMenu();
    }, 140);
  };

  React.useEffect(() => () => clearTimeout(closeTimer.current), []);

  const iconClass = "text-2xl";
  const buttonClass = "!p-0";

  // Keep source actions in the order chosen by the current device's playback preference.
  const entries: MenuEntry[] = React.useMemo(() => {
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

    if (onPlayVideos) {
      result.push({
        key: "videos",
        type: "videos",
        icon: <VideoCameraOutlined className="text-base" />,
        label: t("resource.playback.playVideos"),
        onClick: onPlayVideos,
        isDisabled: playbackReady === false,
      });
    }

    if (onDesktopPlaybackGuide) {
      result.push({
        key: "desktopGuide",
        type: "desktopGuide",
        icon: <DesktopOutlined className="text-base" />,
        label: t("resource.desktopPlayback.action"),
        onClick: onDesktopPlaybackGuide,
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

    for (const action of originalSourceActions ?? []) {
      result.push({
        key: `original-${action.key}`,
        type: "original",
        icon: <ExportOutlined className="text-base" />,
        label: action.label,
        onClick: action.onOpen,
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
    onDesktopPlaybackGuide,
    onPlayVideos,
    originalSourceActions,
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

  const waiting = status === "idle" || status === "loading";
  const mainEntry = entries[0];

  if (!mainEntry && !waiting) return null;

  // Cover hover reveals only the primary action. Resource-specific alternatives
  // stay inside this button's hover/focus panel, without widening the cover overlay.
  const dropdownEntries = (waiting ? entries : entries.slice(1)).filter((entry) =>
    waiting ? entry.type !== "source" && !entry.isDisabled : !entry.isDisabled,
  );
  const mainLabel = waiting ? t("resource.playControl.tooltip.discoveringFiles") : mainEntry!.label;

  return (
    // Keep nested buttons accessible while the group also opens their keyboard menu.
    <div
      ref={anchorRef}
      aria-label={t("resource.playOnThisDevice.moreOptions")}
      className={`${menuOpen ? "flex" : "hidden"} group-hover/cover:flex group-focus-within/cover:flex has-[[data-open=true]]:flex has-[[data-bakabase-tooltip-open]]:flex [@media(hover:none)]:flex absolute left-0 bottom-0 z-[1] focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary`}
      role="group"
      tabIndex={(waiting || mainEntry?.isDisabled) && dropdownEntries.length > 0 ? 0 : undefined}
      onBlur={(event) => {
        const next = event.relatedTarget as Node | null;

        if (!anchorRef.current?.contains(next) && !menuRef.current?.contains(next)) closeMenu();
      }}
      onFocus={(event) => {
        // Pointer hover never moves focus. Keyboard focus reveals the alternatives,
        // then ArrowDown or Tab enters their ordinary keyboard-navigable menu.
        if (
          dropdownEntries.length &&
          !pointerFocus.current &&
          event.currentTarget.contains(event.target)
        )
          openMenu();
      }}
      onKeyDownCapture={(event) => {
        // HeroUI's Tooltip/Button consumes keydown during bubbling. Capture
        // the menu keys first, while leaving the primary Enter/Space action intact.
        if (!dropdownEntries.length || !event.currentTarget.contains(event.target as Node)) return;
        if (event.key === "Escape" && menuOpen) {
          event.preventDefault();
          event.stopPropagation();
          closeMenu();
        } else if (event.key === "ArrowDown" || (event.key === "Tab" && !event.shiftKey)) {
          event.preventDefault();
          event.stopPropagation();
          openMenu(true);
        }
      }}
      onPointerCancel={() => {
        pointerFocus.current = false;
      }}
      onPointerDownCapture={() => {
        pointerFocus.current = true;
      }}
      onPointerEnter={(event) => {
        if (event.pointerType === "mouse" && dropdownEntries.length) openMenu();
      }}
      onPointerLeave={(event) => {
        if (event.pointerType === "mouse") scheduleClose();
      }}
      onPointerUpCapture={() => {
        pointerFocus.current = false;
      }}
    >
      <Tooltip
        content={mainLabel}
        isDisabled={menuOpen}
        isOpen={!menuOpen && tooltipOpen}
        onOpenChange={setTooltipOpen}
      >
        <Button
          isIconOnly
          aria-controls={dropdownEntries.length ? actionsId : undefined}
          aria-expanded={dropdownEntries.length ? menuOpen : undefined}
          aria-haspopup={dropdownEntries.length ? "menu" : undefined}
          aria-label={mainLabel}
          className={buttonClass}
          isDisabled={waiting || mainEntry?.isDisabled}
          onPress={() => {
            closeMenu();
            mainEntry?.onClick();
          }}
        >
          {waiting || mainEntry?.isLoading ? (
            <LoadingOutlined spin className={iconClass} />
          ) : mainEntry?.type === "openFolder" ? (
            <AiOutlineFolderOpen className={iconClass} />
          ) : (
            renderPlayButtonMainIcon(mainEntry?.source ?? DataOrigin.FileSystem, iconClass)
          )}
        </Button>
      </Tooltip>

      {dropdownEntries.length > 0 && (
        <>
          <Button
            isIconOnly
            aria-controls={actionsId}
            aria-expanded={menuOpen}
            aria-haspopup="menu"
            aria-label={t("resource.playOnThisDevice.moreOptions")}
            className="!p-0 [@media(hover:hover)]:hidden"
            size="sm"
            variant="light"
            onPress={() => (menuOpen ? closeMenu() : openMenu())}
          >
            <MoreOutlined className="text-lg" />
          </Button>
          <ControlledMenu
            ref={menuRef}
            portal
            align="start"
            anchorRef={anchorRef}
            aria-label={t("resource.playOnThisDevice.moreOptions")}
            boundingBoxPadding="8"
            captureFocus={keyboardMenu}
            containerProps={{
              onPointerEnter: cancelClose,
              onPointerLeave: (event) => {
                if (event.pointerType === "mouse") scheduleClose();
              },
            }}
            direction="right"
            id={actionsId}
            menuClassName="resource-context-menu !w-max !min-w-44 !max-w-[min(320px,calc(100vw-16px))] !rounded-lg !p-1"
            menuItemFocus={keyboardMenu ? firstMenuItem : undefined}
            offsetX={4}
            overflow="auto"
            state={menuOpen ? "open" : "closed"}
            viewScroll="close"
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
            }}
            onClose={(event) => {
              closeMenu();
              if (event.key === "Escape") {
                // Focus restoration must not reopen the just-dismissed menu.
                const target =
                  waiting || mainEntry?.isDisabled
                    ? anchorRef.current
                    : (anchorRef.current?.querySelector<HTMLButtonElement>(
                        "button:not(:disabled)",
                      ) ?? anchorRef.current);

                target?.focus({ preventScroll: true });
                closeMenu();
              }
            }}
          >
            {dropdownEntries.map((entry) => (
              <MenuItem key={entry.key} onClick={entry.onClick}>
                <div className="flex items-center gap-2">
                  <span aria-hidden>{entry.icon}</span>
                  <span className="min-w-0 break-words">{entry.label}</span>
                </div>
              </MenuItem>
            ))}
          </ControlledMenu>
        </>
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

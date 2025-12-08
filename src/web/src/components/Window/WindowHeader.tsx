import type { WindowState } from "./types.ts";

import React, { useCallback } from "react";
import { IoMdClose, IoMdRemove } from "react-icons/io";
import { MdMaximize, MdFullscreenExit } from "react-icons/md";
import { FiMaximize2 } from "react-icons/fi";
import { RiKeyboardLine } from "react-icons/ri";
import { Button, Kbd, Modal, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useTranslation } from "react-i18next";

interface WindowHeaderProps {
  windowState: WindowState;
  onMinimize: () => void;
  onMaximize: () => void;
  onClose: () => void;
  isMinimized?: boolean;
  title?: string;
  renderActions?: () => React.ReactNode;
}

export const WindowHeader: React.FC<WindowHeaderProps> = ({
  windowState,
  onMinimize,
  onMaximize,
  onClose,
  isMinimized = false,
  title = "Media Player",
  renderActions,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const headerClasses = isMinimized
    ? "window-header flex items-center justify-between px-3 py-2 min-h-[40px] bg-[rgba(30,30,30,0.9)] border-b border-white/10 cursor-move select-none flex-shrink-0 hover:bg-[rgba(40,40,40,0.9)] transition-colors"
    : "window-header flex items-center justify-between px-4 py-2.5 min-h-[44px] bg-[rgba(30,30,30,0.9)] border-b border-white/10 cursor-move select-none flex-shrink-0 hover:bg-[rgba(40,40,40,0.9)] transition-colors";

  const showShortcuts = useCallback(() => {
    createPortal(Modal, {
      defaultVisible: true,
      size: "sm",
      title: t<string>("Shortcuts"),
      footer: { actions: ["cancel"] },
      classNames: {
        wrapper: "z-[9999]",
      },
      children: (
        <div className="flex flex-col gap-3">
          <div className="flex items-center justify-between">
            <div className="text-sm">{t<string>("Previous")}</div>
            <div className="flex items-center gap-2">
              <Kbd keys={["left"]} />
              <span className="text-xs text-white/50">{t<string>("or scroll up")}</span>
            </div>
          </div>
          <div className="flex items-center justify-between">
            <div className="text-sm">{t<string>("Next")}</div>
            <div className="flex items-center gap-2">
              <Kbd keys={["right"]} />
              <span className="text-xs text-white/50">{t<string>("or scroll down")}</span>
            </div>
          </div>
          <div className="flex items-center justify-between">
            <div className="text-sm">{t<string>("Close")}</div>
            <Kbd keys={["escape"]} />
          </div>
        </div>
      ),
    });
  }, [createPortal, t]);

  return (
    <div className={headerClasses} onDoubleClick={isMinimized ? onMinimize : onMaximize}>
      <div className="flex-1 overflow-hidden text-ellipsis whitespace-nowrap text-white/90 text-sm font-medium">
        {title}
      </div>
      <div className="flex items-center gap-1 ml-3">
        {!isMinimized && (
          <Tooltip content={t<string>("Shortcuts")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              className="text-white/70 hover:text-white/90 min-w-8 w-8 h-8"
              title="Shortcuts"
              onMouseDown={(e) => {
                e.stopPropagation();
                e.preventDefault();
              }}
              onPress={() => {
                showShortcuts();
              }}
            >
              <RiKeyboardLine size={18} />
            </Button>
          </Tooltip>
        )}
        {!isMinimized && renderActions && renderActions()}
        {!isMinimized && (
          <Button
            isIconOnly
            size="sm"
            variant="light"
            className="text-white/70 hover:text-white/90 min-w-8 w-8 h-8"
            title="Minimize"
            onMouseDown={(e) => {
              e.stopPropagation();
              e.preventDefault();
            }}
            onPress={(e) => {
              onMinimize();
            }}
          >
            <IoMdRemove size={18} />
          </Button>
        )}
        <Button
          isIconOnly
          size="sm"
          variant="light"
          className="text-white/70 hover:text-white/90 min-w-8 w-8 h-8"
          title={windowState.isMaximized ? "Restore" : isMinimized ? "Restore" : "Maximize"}
          onMouseDown={(e) => {
            e.stopPropagation();
            e.preventDefault();
          }}
          onPress={(e) => {
            if (isMinimized) {
              onMinimize();
            } else {
              onMaximize();
            }
          }}
        >
          {windowState.isMaximized ? <MdFullscreenExit size={16} /> : <FiMaximize2 size={16} />}
        </Button>
        <Button
          isIconOnly
          size="sm"
          variant="light"
          className="text-white/70 hover:text-red-500 hover:bg-red-500/20 min-w-8 w-8 h-8"
          title="Close"
          onMouseDown={(e) => {
            e.stopPropagation();
            e.preventDefault();
          }}
          onPress={(e) => {
            onClose();
          }}
        >
          <IoMdClose size={isMinimized ? 16 : 18} />
        </Button>
      </div>
    </div>
  );
};

import type { WindowState } from "./types";
import React, { useCallback } from "react";
import { IoMdClose, IoMdRemove } from "react-icons/io";
import { MdFullscreenExit } from "react-icons/md";
import { FiMaximize2 } from "react-icons/fi";
import { RiKeyboardLine } from "react-icons/ri";
import { useTranslation } from "react-i18next";
import { Button, Kbd, Modal, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface WindowHeaderProps {
  windowState: WindowState;
  onMinimize: () => void;
  onMaximize: () => void;
  onClose: () => void;
  isMinimized?: boolean;
  title?: string;
  renderActions?: () => React.ReactNode;
}
export const WindowHeader = ({
  windowState,
  onMinimize,
  onMaximize,
  onClose,
  isMinimized = false,
  title,
  renderActions,
}: WindowHeaderProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const showShortcuts = useCallback(
    () =>
      createPortal(Modal, {
        defaultVisible: true,
        size: "md",
        title: t("mediaPlayer.window.shortcuts"),
        footer: { actions: ["cancel"], cancelProps: { children: t("mediaPlayer.window.dismiss") } },
        classNames: { wrapper: "z-[9999]" },
        children: (
          <div className="flex flex-col gap-4 py-2">
            <p className="text-sm text-default-500">{t("mediaPlayer.window.shortcutsHint")}</p>
            {[
              ["mediaPlayer.previous", "left"],
              ["mediaPlayer.next", "right"],
              ["mediaPlayer.window.playPause", "space"],
              ["mediaPlayer.window.close", "escape"],
            ].map(([label, key]) => (
              <div key={key} className="flex items-center justify-between gap-8">
                <span className="text-sm">{t(label)}</span>
                <Kbd keys={[key as any]} />
              </div>
            ))}
            <p className="text-xs text-default-400 border-t border-default-200 pt-3">
              {t("mediaPlayer.window.scrollHint")}
            </p>
          </div>
        ),
      }),
    [createPortal, t],
  );
  const control = (label: string, icon: React.ReactNode, action: () => void, danger = false) => (
    <Tooltip content={t(label)}>
      <Button
        isIconOnly
        className={`min-w-8 w-8 h-8 ${danger ? "text-white/60 hover:text-red-300 hover:bg-red-500/20" : "text-white/60 hover:text-white hover:bg-white/10"}`}
        size="sm"
        aria-label={t(label)}
        title={t(label)}
        variant="light"
        onMouseDown={(event) => {
          event.stopPropagation();
        }}
        onDoubleClick={(event) => event.stopPropagation()}
        onPress={() => action()}
      >
        {icon}
      </Button>
    </Tooltip>
  );
  return (
    <div
      className="window-header flex items-center justify-between gap-3 px-3 py-2 min-h-[44px] bg-[#182131] border-b border-white/10 cursor-move select-none flex-shrink-0"
      onDoubleClick={isMinimized ? onMinimize : onMaximize}
    >
      <div className="flex-1 truncate text-white/85 text-sm font-medium" title={title}>
        {title || t("mediaPlayer.window.title")}
      </div>
      <div className="flex items-center gap-1">
        {!isMinimized &&
          control("mediaPlayer.window.shortcuts", <RiKeyboardLine size={18} />, showShortcuts)}
        {!isMinimized && renderActions?.()}
        {!isMinimized &&
          control("mediaPlayer.window.minimize", <IoMdRemove size={18} />, onMinimize)}
        {control(
          windowState.isMaximized || isMinimized
            ? "mediaPlayer.window.restore"
            : "mediaPlayer.window.maximize",
          windowState.isMaximized ? <MdFullscreenExit size={17} /> : <FiMaximize2 size={15} />,
          isMinimized ? onMinimize : onMaximize,
        )}
        {control("mediaPlayer.window.close", <IoMdClose size={18} />, onClose, true)}
      </div>
    </div>
  );
};

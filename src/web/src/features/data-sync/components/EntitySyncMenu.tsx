import type { DataSyncEntityStatusView } from "../api";
import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";
import type { EntityMenuAction } from "../viewModels";

import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineEllipsis } from "react-icons/ai";

import { dataSyncApi } from "../api";
import { useMenuKeyboard } from "../hooks/useMenuKeyboard";
import { entityMenu } from "../viewModels";

import { DataSyncEntitySyncState } from "@/sdk/constants";

/*
 * What can be chosen for one definition: keep it on this device only, sync its definition
 * without its options (a shared choice: every device this one syncs with takes it), stop
 * syncing it, or sync it again. Each runs through the host's actions like every other data
 * sync control.
 */

export interface EntitySyncMenuProps {
  kind: string;
  entity: DataSyncEntityStatusView;
  /** The definition's name, for the menu's accessible name and the confirmations. */
  name: string;
  /** Only choice, multiple choice, tags and multilevel properties have options to leave out. */
  offersDefinitionOnly: boolean;
  actions: DataSyncPanelActions;
}

export default function EntitySyncMenu({
  kind,
  entity,
  name,
  offersDefinitionOnly,
  actions,
}: EntitySyncMenuProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLSpanElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLSpanElement>(null);
  const menuId = useId();
  // Escape closes the menu, and only the menu: the page or details around it stay as they are.
  const menuKeys = useMenuKeyboard(open, menu, trigger, () => setOpen(false), root);
  const items = entityMenu(entity, offersDefinitionOnly);
  // A choice was made from the menu, whose item is gone: the keyboard comes back to the menu's
  // button once the choice is over — the button is disabled while it runs, and a confirmation
  // gives focus back to it only while it can take it.
  const returning = useRef(false);

  useEffect(() => {
    const letGo = (event: Event) => {
      const target = event.target instanceof Element ? event.target : null;

      // The reader went elsewhere; a confirmation the choice opened is still the choice's.
      if (target !== trigger.current && !target?.closest('[role="alertdialog"]'))
        returning.current = false;
    };

    document.addEventListener("focusin", letGo, true);
    document.addEventListener("pointerdown", letGo, true);

    return () => {
      document.removeEventListener("focusin", letGo, true);
      document.removeEventListener("pointerdown", letGo, true);
    };
  }, []);

  useEffect(() => {
    if (!returning.current || actions.busy) return;
    const active = document.activeElement;

    if (active && active !== document.body && active.isConnected) return;
    returning.current = false;
    trigger.current?.focus();
  });

  useEffect(() => {
    if (!open) return;
    const close = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };

    document.addEventListener("pointerdown", close, true);

    return () => document.removeEventListener("pointerdown", close, true);
  }, [open]);

  const set = (input: Parameters<typeof dataSyncApi.setEntitySync>[2]) => () =>
    dataSyncApi.setEntitySync(kind, entity.localKey, input);

  const choose = (item: EntityMenuAction) => {
    setOpen(false);
    trigger.current?.focus();
    returning.current = true;
    switch (item) {
      case "keepLocal":
        void actions.run(set({ state: DataSyncEntitySyncState.LocalOnly }), ["dataSync"]);
        break;
      case "rejoin":
        void actions.run(set({ state: DataSyncEntitySyncState.Synced }), ["dataSync"]);
        break;
      case "detach":
        actions.confirm({
          title: t("dataSync.entity.detach.title", { name }),
          description: t("dataSync.entity.detach.description"),
          action: set({ state: DataSyncEntitySyncState.Detached }),
          refresh: ["dataSync"],
        });
        break;
      case "definitionOnlyOn":
        actions.confirm({
          title: t("dataSync.entity.definitionOnly.onTitle", { name }),
          description: t("dataSync.entity.definitionOnly.everyDevice"),
          action: set({ childrenLocal: true }),
          refresh: ["dataSync"],
        });
        break;
      case "definitionOnlyOff":
        actions.confirm({
          title: t("dataSync.entity.definitionOnly.offTitle", { name }),
          description: t("dataSync.entity.definitionOnly.offDescription"),
          action: set({ childrenLocal: false }),
          refresh: ["dataSync"],
        });
        break;
    }
  };

  return (
    <span ref={root} className="relative inline-flex">
      <button
        ref={trigger}
        aria-controls={open ? menuId : undefined}
        aria-expanded={open}
        aria-haspopup="menu"
        aria-label={t("dataSync.entity.menu", { name })}
        className="rounded p-1 text-default-500 hover:bg-default-100 disabled:opacity-50"
        data-testid="data-sync-entity-menu"
        disabled={actions.busy}
        id={`${menuId}-button`}
        type="button"
        onClick={() => setOpen((current) => !current)}
        onKeyDown={(event) => {
          if (!open && (event.key === "ArrowDown" || event.key === "ArrowUp")) {
            event.preventDefault();
            setOpen(true);
          }
        }}
      >
        <AiOutlineEllipsis aria-hidden />
      </button>
      {open && (
        <span
          ref={menu}
          aria-labelledby={`${menuId}-button`}
          className="absolute right-0 top-full z-20 mt-1 flex min-w-56 flex-col rounded-lg border border-default-200 bg-content1 p-1 shadow-lg"
          id={menuId}
          role="menu"
          tabIndex={-1}
          onKeyDown={menuKeys}
        >
          {items.map((item) => (
            <button
              key={item}
              className="rounded-md px-2 py-1.5 text-left text-xs outline-none hover:bg-default-100 focus-visible:bg-default-100 focus-visible:ring-2 focus-visible:ring-focus"
              data-action={item}
              role="menuitem"
              tabIndex={-1}
              type="button"
              onClick={() => choose(item)}
            >
              <span className="block">{t(`dataSync.entity.action.${item}`)}</span>
              {(item === "definitionOnlyOn" || item === "definitionOnlyOff") && (
                <span className="block text-[11px] text-default-500">
                  {t("dataSync.entity.definitionOnly.everyDevice")}
                </span>
              )}
            </button>
          ))}
        </span>
      )}
    </span>
  );
}

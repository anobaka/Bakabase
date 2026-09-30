import type { DataSyncEntityStatusView } from "../api";
import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";
import type { EntityMenuAction } from "../viewModels";

import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineEllipsis } from "react-icons/ai";

import { dataSyncApi } from "../api";
import { entityMenu } from "../viewModels";

import { Dropdown, DropdownItem, DropdownMenu, DropdownTrigger } from "@/components/bakaui";
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
  const trigger = useRef<HTMLButtonElement>(null);
  const items = entityMenu(entity, offersDefinitionOnly);
  // A choice was made from the menu, whose item is gone: the keyboard comes back to the menu's
  // button once the choice is over — the button is disabled while it runs, and a confirmation
  // gives focus back to it only while it can take it.
  const returning = useRef(false);

  useEffect(() => {
    const letGo = (event: Event) => {
      const target = event.target instanceof Element ? event.target : null;

      // The reader went elsewhere; the menu closing, or a confirmation the choice opened, is
      // still the choice's.
      if (target !== trigger.current && !target?.closest('[role="alertdialog"], [role="menu"]'))
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

  const set = (input: Parameters<typeof dataSyncApi.setEntitySync>[2]) => () =>
    dataSyncApi.setEntitySync(kind, entity.localKey, input);

  const choose = (item: EntityMenuAction) => {
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
    <Dropdown isDisabled={actions.busy} placement="bottom-end">
      <DropdownTrigger>
        <button
          ref={trigger}
          aria-label={t("dataSync.entity.menu", { name })}
          className="rounded p-1 text-default-500 hover:bg-default-100 disabled:opacity-50"
          data-testid="data-sync-entity-menu"
          disabled={actions.busy}
          type="button"
        >
          <AiOutlineEllipsis aria-hidden />
        </button>
      </DropdownTrigger>
      <DropdownMenu
        aria-label={t("dataSync.entity.menu", { name })}
        onAction={(key) => choose(key as EntityMenuAction)}
      >
        {items.map((item) => (
          <DropdownItem
            key={item}
            data-action={item}
            description={
              item === "definitionOnlyOn" || item === "definitionOnlyOff"
                ? t("dataSync.entity.definitionOnly.everyDevice")
                : undefined
            }
          >
            {t(`dataSync.entity.action.${item}`)}
          </DropdownItem>
        ))}
      </DropdownMenu>
    </Dropdown>
  );
}

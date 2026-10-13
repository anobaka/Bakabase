import type { IMenuItem } from "../AntdMenu/menuConfig";
import type {
  NavigationCapabilities,
  NavigationLayout,
  NavigationNode,
} from "../AntdMenu/navigationLayout";
import type { DragEndEvent } from "@dnd-kit/core";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { v4 as uuid } from "uuid";
import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import {
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import {
  TbArrowDown,
  TbArrowUp,
  TbEye,
  TbEyeOff,
  TbFolder,
  TbGripVertical,
  TbLayoutSidebar,
  TbPlus,
  TbRefresh,
  TbTrash,
} from "react-icons/tb";

import {
  addNavigationDirectory,
  mergeNavigationLayout,
  moveNavigationNode,
  removeNavigationDirectory,
  renameNavigationDirectory,
  setNavigationNodeHidden,
} from "../AntdMenu/navigationLayout";

import { Button, Chip, Input, Modal, Select, Tooltip } from "@/components/bakaui";

const ROOT_LOCATION = "navigation-editor-root";
const MAXIMUM_DIRECTORY_NAME = 80;

export type NavigationEditorProps = {
  isOpen: boolean;
  onClose: () => void;
  catalog: readonly IMenuItem[];
  savedLayout?: unknown;
  capabilities: NavigationCapabilities;
  onSave: (layout: NavigationLayout) => Promise<void>;
};

type CatalogEntry = { item: IMenuItem; label: string; unavailable: boolean };
type Location = { node: NavigationNode; parentId: string | null; index: number };

const findLocation = (layout: NavigationLayout, id: string): Location | undefined => {
  for (let index = 0; index < layout.items.length; index++) {
    const node = layout.items[index]!;

    if (node.id === id) return { node, parentId: null, index };
    const childIndex = node.children?.findIndex((child) => child.id === id) ?? -1;

    if (childIndex >= 0)
      return { node: node.children![childIndex]!, parentId: node.id, index: childIndex };
  }
};

type RowProps = {
  node: NavigationNode;
  label: string;
  entry?: CatalogEntry;
  parentId: string | null;
  parentHidden?: boolean;
  index: number;
  total: number;
  saving: boolean;
  newDirectory: boolean;
  invalidName: boolean;
  directories: { id: string; name: string; hidden?: boolean }[];
  onName: (name: string) => void;
  onHidden: () => void;
  onRemove: () => void;
  onMove: (direction: -1 | 1) => void;
  onLocation: (parentId: string | null) => void;
  children?: React.ReactNode;
};

const NavigationRow = ({
  node,
  label,
  entry,
  parentId,
  parentHidden,
  index,
  total,
  saving,
  newDirectory,
  invalidName,
  directories,
  onName,
  onHidden,
  onRemove,
  onMove,
  onLocation,
  children,
}: RowProps) => {
  const { t } = useTranslation();
  const nameInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!newDirectory) return;
    nameInput.current?.focus({ preventScroll: true });
    nameInput.current?.select();
  }, [newDirectory]);
  const directory = node.kind === "directory";
  const {
    attributes,
    listeners,
    setNodeRef,
    setActivatorNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: node.id, disabled: saving });
  const Icon = directory ? TbFolder : (entry?.item.icon ?? TbLayoutSidebar);
  const hidden = node.hidden || parentHidden;
  const action = (key: string, icon: React.ReactNode, run: () => void, disabled = false) => (
    <Tooltip content={t(key, { name: label })}>
      <Button
        isIconOnly
        aria-label={t(key, { name: label })}
        className="h-7 w-7 min-w-7 text-default-500"
        isDisabled={saving || disabled}
        size="sm"
        variant="light"
        onPress={run}
      >
        {icon}
      </Button>
    </Tooltip>
  );

  return (
    <div
      ref={setNodeRef}
      className={directory ? "rounded-lg border border-default-200 bg-default-50/40" : "rounded-md"}
      data-navigation-editor-node={node.id}
      style={{
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.4 : 1,
      }}
    >
      <div
        className={`flex flex-wrap items-center gap-1 px-2 py-1 sm:flex-nowrap ${hidden ? "text-default-400" : ""}`}
      >
        <button
          ref={setActivatorNodeRef}
          {...attributes}
          {...listeners}
          aria-label={t("navigationEditor.drag.label", { name: label })}
          className="flex h-7 w-6 shrink-0 touch-none items-center justify-center rounded text-default-400 hover:bg-default-200 focus-visible:outline-primary"
          disabled={saving}
          type="button"
        >
          <TbGripVertical size={17} />
        </button>
        <Icon
          className={`shrink-0 text-lg ${directory ? "text-primary-400" : "text-default-400"}`}
        />
        <div className="min-w-0 flex-1">
          {directory ? (
            <Input
              ref={nameInput}
              aria-label={t("navigationEditor.directoryName.label", { name: label })}
              classNames={{
                inputWrapper: "h-8 min-h-8 bg-transparent shadow-none",
                input: "text-sm font-medium",
              }}
              errorMessage={invalidName ? t("navigationEditor.directoryName.error") : undefined}
              isDisabled={saving}
              isInvalid={invalidName}
              maxLength={MAXIMUM_DIRECTORY_NAME}
              size="sm"
              value={label}
              variant="flat"
              onValueChange={onName}
            />
          ) : (
            <div className="flex min-w-0 items-center gap-1.5 py-1 text-sm">
              <span className="truncate" title={label}>
                {label}
              </span>
              {entry?.unavailable && (
                <Tooltip content={t("navigationEditor.unavailable.description")}>
                  <Chip className="h-5 shrink-0 text-[10px]" size="sm" variant="flat">
                    {t("navigationEditor.unavailable.label")}
                  </Chip>
                </Tooltip>
              )}
              {parentHidden && (
                <span className="shrink-0 text-[10px] text-default-400">
                  {t("navigationEditor.parentHidden.label")}
                </span>
              )}
            </div>
          )}
        </div>
        {!directory && (
          <Select
            disallowEmptySelection
            aria-label={t("navigationEditor.location.label", { name: label })}
            className="w-40 shrink-0"
            classNames={{ trigger: "h-8 min-h-8", value: "text-xs" }}
            dataSource={[
              { value: ROOT_LOCATION, label: t("navigationEditor.location.root") },
              ...directories.map((item) => ({
                value: item.id,
                label: item.hidden
                  ? t("navigationEditor.location.hiddenDirectory", { name: item.name })
                  : item.name,
              })),
            ]}
            isDisabled={saving}
            selectedKeys={[parentId ?? ROOT_LOCATION]}
            size="sm"
            onSelectionChange={(keys) => {
              const selected = Array.from(keys)[0]?.toString();

              if (selected) onLocation(selected === ROOT_LOCATION ? null : selected);
            }}
          />
        )}
        <div className="flex shrink-0 items-center gap-0.5">
          {action(
            node.hidden ? "navigationEditor.show.label" : "navigationEditor.hide.label",
            node.hidden ? <TbEyeOff size={17} /> : <TbEye size={17} />,
            onHidden,
          )}
          {action(
            "navigationEditor.moveUp.label",
            <TbArrowUp size={15} />,
            () => onMove(-1),
            index === 0,
          )}
          {action(
            "navigationEditor.moveDown.label",
            <TbArrowDown size={15} />,
            () => onMove(1),
            index === total - 1,
          )}
          {directory &&
            action("navigationEditor.removeDirectory.label", <TbTrash size={16} />, onRemove)}
        </div>
      </div>
      {directory && (
        <div className="ml-8 border-l border-default-200 pl-1">
          {children || (
            <p className="px-3 py-2 text-xs text-default-400">
              {t("navigationEditor.emptyDirectory.description")}
            </p>
          )}
        </div>
      )}
    </div>
  );
};

const NavigationEditor = ({
  isOpen,
  onClose,
  catalog,
  savedLayout,
  capabilities,
  onSave,
}: NavigationEditorProps) => {
  const { t } = useTranslation();
  const [layout, setLayout] = useState<NavigationLayout>(() =>
    mergeNavigationLayout(catalog, savedLayout, (key) => t(key)),
  );
  const [names, setNames] = useState<Record<string, string>>({});
  const [newDirectoryId, setNewDirectoryId] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const [announcement, setAnnouncement] = useState("");
  const wasOpen = useRef(false);
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  useEffect(() => {
    if (isOpen && !wasOpen.current) {
      setLayout(mergeNavigationLayout(catalog, savedLayout, (key) => t(key)));
      setNames({});
      setNewDirectoryId(undefined);
      setError(undefined);
      setAnnouncement("");
    }
    wasOpen.current = isOpen;
  }, [isOpen, catalog, savedLayout, t]);

  const entries = useMemo(() => {
    const result = new Map<string, CatalogEntry>();
    const visit = (items: readonly IMenuItem[], pureClientOnly = false, localNodeOnly = false) => {
      for (const item of items) {
        const pure = pureClientOnly || !!item.pureClientOnly;
        const local = localNodeOnly || !!item.localNodeOnly;

        result.set(`${item.children ? "directory" : "menu"}:${item.name}`, {
          item,
          label: t<string>(item.name) ?? item.name,
          unavailable: (pure && !capabilities.isPureClient) || (local && !capabilities.isLocalNode),
        });
        if (item.children) visit(item.children, pure, local);
      }
    };

    visit(catalog);

    return result;
  }, [catalog, capabilities.isLocalNode, capabilities.isPureClient, t]);
  const labelFor = (node: NavigationNode): string =>
    node.kind === "directory"
      ? (names[node.id] ??
        node.name ??
        entries.get(node.id)?.label ??
        t<string>("navigationEditor.newDirectoryName.label") ??
        "")
      : (entries.get(node.id)?.label ?? t<string>("navigationEditor.unknownMenu.label") ?? "");
  const directories = layout.items
    .filter((item) => item.kind === "directory")
    .map((item) => ({
      id: item.id,
      name: labelFor(item),
      hidden: item.hidden,
    }));
  const invalidNames = directories.some(
    (item) => !item.name.trim() || item.name.trim().length > MAXIMUM_DIRECTORY_NAME,
  );
  const update = (next: NavigationLayout) => {
    setError(undefined);
    setLayout(next);
  };
  const moved = (id: string, parentId: string | null, index?: number) => {
    const location = findLocation(layout, id);

    if (!location || saving) return;
    update(moveNavigationNode(layout, id, parentId, index));
    setAnnouncement(
      t("navigationEditor.moved.description", {
        name: labelFor(location.node),
        location:
          directories.find((item) => item.id === parentId)?.name ??
          t("navigationEditor.location.root"),
      }),
    );
  };
  const dragEnded = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id || saving) return;
    const from = findLocation(layout, String(active.id));
    const to = findLocation(layout, String(over.id));

    if (!from || !to) return;
    if (from.node.kind === "directory") {
      const index = to.parentId
        ? layout.items.findIndex((node) => node.id === to.parentId)
        : to.index;

      moved(from.node.id, null, index);
    } else if (to.node.kind === "directory") moved(from.node.id, to.node.id);
    else moved(from.node.id, to.parentId, to.index);
  };
  const save = async () => {
    if (saving || invalidNames) return;
    let next = layout;

    // Untouched built-in names stay token-based so changing the UI language
    // still translates them. Only explicit name edits become label overrides.
    for (const directory of directories) {
      if (names[directory.id] !== undefined)
        next = renameNavigationDirectory(next, directory.id, directory.name.trim());
    }
    setSaving(true);
    setError(undefined);
    try {
      await onSave(next);
      onClose();
    } catch {
      setError(t("navigationEditor.saveFailed.description"));
    } finally {
      setSaving(false);
    }
  };
  const addDirectory = () => {
    const used = new Set(directories.map((directory) => directory.name));
    let name = t("navigationEditor.newDirectoryName.label");
    let number = 2;

    while (used.has(name))
      name = t("navigationEditor.newDirectoryName.numbered", { number: number++ });
    const id = uuid();

    update(addNavigationDirectory(layout, id, name));
    setNewDirectoryId(id);
  };
  const renderRow = (
    node: NavigationNode,
    parentId: string | null,
    index: number,
    total: number,
    parentHidden = false,
  ) => (
    <NavigationRow
      key={node.id}
      directories={directories}
      entry={entries.get(node.id)}
      index={index}
      invalidName={
        node.kind === "directory" &&
        (!labelFor(node).trim() || labelFor(node).trim().length > MAXIMUM_DIRECTORY_NAME)
      }
      label={labelFor(node)}
      newDirectory={node.id === newDirectoryId}
      node={node}
      parentHidden={parentHidden}
      parentId={parentId}
      saving={saving}
      total={total}
      onHidden={() => update(setNavigationNodeHidden(layout, node.id, !node.hidden))}
      onLocation={(destination) => moved(node.id, destination)}
      onMove={(direction) => moved(node.id, parentId, index + direction)}
      onName={(name) => {
        setError(undefined);
        setNames((current) => ({ ...current, [node.id]: name }));
      }}
      onRemove={() => update(removeNavigationDirectory(layout, node.id))}
    >
      {node.children?.length ? (
        <SortableContext
          items={node.children.map((item) => item.id)}
          strategy={verticalListSortingStrategy}
        >
          <div className="space-y-0.5 pb-1">
            {node.children.map((child, childIndex) =>
              renderRow(child, node.id, childIndex, node.children!.length, !!node.hidden),
            )}
          </div>
        </SortableContext>
      ) : undefined}
    </NavigationRow>
  );

  return (
    <Modal
      footer={
        <div className="flex w-full items-center justify-between gap-3">
          <Button
            isDisabled={saving}
            size="sm"
            startContent={<TbRefresh size={16} />}
            variant="light"
            onPress={() => {
              update(mergeNavigationLayout(catalog, undefined, (key) => t(key)));
              setNames({});
              setNewDirectoryId(undefined);
            }}
          >
            {t("navigationEditor.restore.label")}
          </Button>
          <div className="flex gap-2">
            <Button isDisabled={saving} size="sm" variant="light" onPress={onClose}>
              {t("navigationEditor.cancel.label")}
            </Button>
            <Button
              color="primary"
              isDisabled={invalidNames}
              isLoading={saving}
              size="sm"
              onPress={save}
            >
              {t("navigationEditor.save.label")}
            </Button>
          </div>
        </div>
      }
      hideCloseButton={saving}
      isDismissable={!saving}
      isKeyboardDismissDisabled={saving}
      size="3xl"
      title={t("navigationEditor.title")}
      visible={isOpen}
      onClose={() => {
        if (!saving) onClose();
      }}
    >
      <div className="flex flex-col gap-3">
        <div className="flex items-start justify-between gap-3">
          <p className="text-xs leading-relaxed text-default-500">
            {t("navigationEditor.description")}
          </p>
          <Button
            className="shrink-0"
            isDisabled={saving}
            size="sm"
            startContent={<TbPlus size={16} />}
            variant="flat"
            onPress={addDirectory}
          >
            {t("navigationEditor.addDirectory.label")}
          </Button>
        </div>
        {error && (
          <p className="rounded-lg bg-danger-50 px-3 py-2 text-sm text-danger" role="alert">
            {error}
          </p>
        )}
        <DndContext
          accessibility={{
            screenReaderInstructions: { draggable: t("navigationEditor.drag.instructions") },
          }}
          collisionDetection={closestCenter}
          sensors={sensors}
          onDragEnd={dragEnded}
        >
          <SortableContext
            items={layout.items.map((node) => node.id)}
            strategy={verticalListSortingStrategy}
          >
            <div className="max-h-[57vh] space-y-1.5 overflow-y-auto rounded-xl border border-default-200 p-2">
              {layout.items.map((node, index) => renderRow(node, null, index, layout.items.length))}
            </div>
          </SortableContext>
        </DndContext>
        <p className="text-[11px] text-default-400">
          {t("navigationEditor.removeDirectory.description")}
        </p>
        <span aria-live="polite" className="sr-only">
          {announcement}
        </span>
      </div>
    </Modal>
  );
};

export default NavigationEditor;

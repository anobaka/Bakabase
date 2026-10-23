"use client";

import type { CSSProperties } from "react";
import type { DragEndEvent } from "@dnd-kit/core";
import type { MediaLibrary } from "./models";
import type { OwnedCatalogItem } from "@/features/management/ownedCatalog";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
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
  rectSortingStrategy,
  sortableKeyboardCoordinates,
} from "@dnd-kit/sortable";
import {
  AiOutlineArrowRight,
  AiOutlineEdit,
  AiOutlineFolderOpen,
  AiOutlineMore,
  AiOutlinePlus,
  AiOutlineReload,
  AiOutlineSearch,
} from "react-icons/ai";
import { HiOutlineCollection } from "react-icons/hi";
import { MdOutlineDelete } from "react-icons/md";

import MediaLibraryEditor from "./components/MediaLibraryEditor";
import SortableLibraryCard from "./components/SortableLibraryCard";
import styles from "./index.module.scss";

import {
  Button,
  Checkbox,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Input,
  Modal,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { HelpCenterButton } from "@/components/HelpCenter";
import {
  useOwnedCatalog,
  catalogItemKey,
  catalogItemApi,
  canEditCatalogItem,
  CatalogOwnerMark,
  CatalogSourceStatus,
  catalogOwnerSearch,
} from "@/features/management/ownedCatalog";
import { dashboardResourceSearch } from "@/pages/dashboard/dashboardSearch";
import BApi from "@/sdk/BApi";
import { usePendingSearchStore } from "@/stores/pendingSearch";
import { mediaLibraryChoiceValue } from "@/features/management/mediaLibraryFilter";
import { orderMediaLibraryChoices } from "@/features/management/mediaLibraryOrder";
import { useMediaLibraryOrder } from "@/features/management/useMediaLibraryOrder";
import MediaLibraryOrderToolbar from "@/features/management/MediaLibraryOrderToolbar";

type Library = OwnedCatalogItem<MediaLibrary>;
type LibraryFilter = "all" | "withResources" | "empty";

const MediaLibraryPage = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { createPortal } = useBakabaseContext();
  const setPendingSearch = usePendingSearchStore((s) => s.setPendingSearch);
  const [mediaLibraries, setMediaLibraries] = useState<MediaLibrary[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [keyword, setKeyword] = useState("");
  const [filter, setFilter] = useState<LibraryFilter>("all");
  const [editor, setEditor] = useState<{ library?: Library }>();
  const [highlightedKey, setHighlightedKey] = useState<string>();
  const [selectionMode, setSelectionMode] = useState(false);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const catalog = useOwnedCatalog(mediaLibraries, (api) =>
    api.mediaLibraryV2.getAllMediaLibraryV2(),
  );
  const sourcesRef = useRef(catalog.sources);

  sourcesRef.current = catalog.sources;
  const loadRevision = useRef(0);
  const loadMediaLibraries = useCallback(async () => {
    const revision = ++loadRevision.current;

    setLoading(true);
    try {
      const response = await BApi.mediaLibraryV2.getAllMediaLibraryV2();

      if (response.code || !response.data) throw new Error(response.message);
      if (revision !== loadRevision.current) return;
      setMediaLibraries(response.data);
      setLoadError(false);

      return response.data;
    } catch {
      if (revision === loadRevision.current) setLoadError(true);
    } finally {
      if (revision === loadRevision.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadMediaLibraries();

    return () => {
      loadRevision.current++;
    };
  }, [loadMediaLibraries]);

  useEffect(() => {
    if (!highlightedKey) return;
    document.getElementById(`media-library-${highlightedKey}`)?.scrollIntoView?.({
      behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches
        ? "instant"
        : "smooth",
      block: "nearest",
    });
    const timer = setTimeout(() => setHighlightedKey(undefined), 3000);

    return () => clearTimeout(timer);
  }, [highlightedKey]);

  // A dialog can outlive the device connection it was opened from.
  const currentItem = (library: Library): Library => ({
    ...library,
    owner:
      library.owner && !library.owner.isLocal
        ? (sourcesRef.current.find(
            (source) =>
              source.serverId === library.owner!.serverId &&
              source.libraryEpoch === library.owner!.libraryEpoch,
          ) ?? { ...library.owner, available: false })
        : library.owner,
  });

  const saveLibrary = async (value: { name: string; color: string }) => {
    const library = editor?.library ? currentItem(editor.library) : undefined;
    const existingIds = new Set(mediaLibraries.map((item) => item.id));

    if (library && !canEditCatalogItem(library)) {
      throw new Error(t("management.catalog.ownerUnavailable"));
    }
    const response = library
      ? await catalogItemApi(library).mediaLibraryV2.patchMediaLibraryV2(library.id, value)
      : await BApi.mediaLibraryV2.addMediaLibraryV2({ ...value, paths: [] });

    if (response.code) throw new Error(response.message);
    setKeyword("");
    setFilter("all");
    const updated = await loadMediaLibraries();
    const savedLibrary = library ?? updated?.find((item) => !existingIds.has(item.id));

    if (savedLibrary) {
      setHighlightedKey(
        catalogItemKey({
          ...savedLibrary,
          owner: library?.owner ?? sourcesRef.current.find((source) => source.isLocal),
        }),
      );
    }
    catalog.refresh();
  };

  const deleteLibraries = (libraries: Library[]) => {
    let removeRelatedPathMarks = false;
    let remainingLibraries = libraries;
    let deleting = false;

    createPortal(Modal, {
      defaultVisible: true,
      title: t(
        libraries.length === 1
          ? "mediaLibrary.confirm.deleteTitle"
          : "mediaLibrary.confirm.deleteSelectedTitle",
      ),
      children: (
        <div className="space-y-3">
          <p className="break-words">
            {libraries.length === 1
              ? t("mediaLibrary.confirm.deleteNamed", { name: libraries[0].name })
              : t("mediaLibrary.confirm.deleteSelected", { count: libraries.length })}
          </p>
          {libraries.length > 1 && (
            <ul className="max-h-40 space-y-1 overflow-y-auto text-sm">
              {libraries.map((library) => (
                <li key={catalogItemKey(library)} className="flex flex-wrap items-center gap-2">
                  <span className="break-words">{library.name}</span>
                  <CatalogOwnerMark owner={library.owner} />
                </li>
              ))}
            </ul>
          )}
          <p className="text-sm text-default-500">{t("mediaLibrary.confirm.deleteDescription")}</p>
          <div className="rounded-xl border border-default-200 p-3">
            <Checkbox
              aria-describedby="media-library-delete-path-marks-description"
              defaultSelected={false}
              onValueChange={(value) => {
                removeRelatedPathMarks = value;
              }}
            >
              {t("mediaLibrary.confirm.removeRelatedPathMarks.label")}
            </Checkbox>
            <p
              className="mt-2 pl-7 text-xs leading-6 text-default-500"
              id="media-library-delete-path-marks-description"
            >
              {t("mediaLibrary.confirm.removeRelatedPathMarks.description")}
            </p>
          </div>
          <p className="text-sm text-danger">{t("mediaLibrary.warning.irreversible")}</p>
        </div>
      ),
      onOk: async () => {
        if (deleting) return;
        const currentLibraries = remainingLibraries.map(currentItem);

        if (currentLibraries.some((library) => !canEditCatalogItem(library))) {
          throw new Error(t("management.catalog.ownerUnavailable"));
        }
        const groups = new Map<string, Library[]>();

        for (const library of currentLibraries) {
          const owner = JSON.stringify([
            library.owner?.serverId ?? "local",
            library.owner?.libraryEpoch ?? "",
          ]);
          const group = groups.get(owner) ?? [];

          group.push(library);
          groups.set(owner, group);
        }
        deleting = true;
        const deleted = new Set<string>();
        const cleanup = removeRelatedPathMarks;

        try {
          for (const group of groups.values()) {
            // Managed-device requests accept at most 100 IDs per operation.
            for (let offset = 0; offset < group.length; offset += 100) {
              const chunk = group.slice(offset, offset + 100);
              const current = currentItem(chunk[0]);

              if (!canEditCatalogItem(current))
                throw new Error(t("management.catalog.ownerUnavailable"));
              const response = await catalogItemApi(current).mediaLibraryV2.deleteMediaLibrariesV2({
                ids: chunk.map((library) => library.id),
                removeRelatedPathMarks: cleanup,
              });

              if (response.code) throw new Error(response.message);
              chunk.forEach((library) => deleted.add(catalogItemKey(library)));
            }
          }
        } finally {
          if (deleted.size) {
            // A retry only submits owners that have not already confirmed deletion.
            remainingLibraries = remainingLibraries.filter(
              (library) => !deleted.has(catalogItemKey(library)),
            );
            setSelectedKeys((keys) => new Set([...keys].filter((key) => !deleted.has(key))));
            await loadMediaLibraries();
            catalog.refresh();
          }
          deleting = false;
        }
      },
      footer: {
        actions: ["ok", "cancel"],
        okProps: { children: t("mediaLibrary.action.delete"), color: "danger" },
        cancelProps: { children: t("mediaLibrary.action.cancel"), autoFocus: true },
      },
    });
  };

  const browse = (library: Library) => {
    setPendingSearch({
      ...dashboardResourceSearch({ libraryId: library.id }),
      ...catalogOwnerSearch(library.owner),
    });
    navigate("/resource");
  };

  const allLibraries = catalog.items;
  const selectedLibraries = allLibraries.filter((library) =>
    selectedKeys.has(catalogItemKey(library)),
  );
  const availableKeys = JSON.stringify(allLibraries.map(catalogItemKey));

  useEffect(() => {
    const available = new Set<string>(JSON.parse(availableKeys));

    setSelectedKeys((keys) =>
      [...keys].every((key) => available.has(key))
        ? keys
        : new Set([...keys].filter((key) => available.has(key))),
    );
  }, [availableKeys]);

  const libraryKey = (library: Library) =>
    library.owner ? mediaLibraryChoiceValue(library.owner, library.id) : catalogItemKey(library);
  const order = useMediaLibraryOrder(allLibraries.map(libraryKey));
  const orderPending = useRef(false);
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );
  const orderingReady = allLibraries.every((library) => library.owner != null);
  const populatedCount = allLibraries.filter((library) => library.resourceCount > 0).length;
  const counts = {
    all: allLibraries.length,
    withResources: populatedCount,
    empty: allLibraries.length - populatedCount,
  };
  const normalizedKeyword = keyword.trim().toLocaleLowerCase();
  const visibleLibraries = orderMediaLibraryChoices(allLibraries, order.keys, libraryKey).filter(
    (library) =>
      library.name.toLocaleLowerCase().includes(normalizedKeyword) &&
      (filter === "all" ||
        (filter === "withResources" ? library.resourceCount > 0 : !library.resourceCount)),
  );
  const selectableLibraries = visibleLibraries.filter(canEditCatalogItem);
  const selectedVisibleCount = selectableLibraries.filter((library) =>
    selectedKeys.has(catalogItemKey(library)),
  ).length;
  const changeSelection = (libraries: Library[], selected: boolean) => {
    setSelectedKeys((keys) => {
      const next = new Set(keys);

      for (const library of libraries) {
        if (selected) next.add(catalogItemKey(library));
        else next.delete(catalogItemKey(library));
      }

      return next;
    });
  };
  const moveLibrary = async ({ active, over }: DragEndEvent) => {
    if (orderPending.current || !orderingReady || !over || active.id === over.id) return;
    orderPending.current = true;
    try {
      await order.move(String(active.id), String(over.id), visibleLibraries.map(libraryKey));
    } finally {
      orderPending.current = false;
    }
  };
  const clearSearch = () => {
    setKeyword("");
    setFilter("all");
  };
  const initialLoading = (loading || catalog.loading) && allLibraries.length === 0;

  return (
    <div className={styles.page}>
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <div className="flex items-center gap-3">
            <h1 className="text-2xl font-semibold tracking-tight">{t("mediaLibrary.title")}</h1>
            {!initialLoading && !loadError && (
              <span className="rounded-full bg-default-100 px-2.5 py-0.5 text-xs font-medium tabular-nums text-default-500">
                {allLibraries.length}
              </span>
            )}
          </div>
          <p className="mt-2 max-w-2xl text-sm leading-relaxed text-default-500">
            {t("mediaLibrary.description")}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <HelpCenterButton concept="mediaLibrary" topic="pathMark" />
          {(selectionMode || allLibraries.length > 0) && (
            <Button
              aria-controls={selectionMode ? "media-library-selection-toolbar" : undefined}
              aria-expanded={selectionMode}
              color={selectionMode ? "primary" : "default"}
              variant="flat"
              onPress={() => {
                setSelectionMode((active) => !active);
                setSelectedKeys(new Set());
              }}
            >
              {t(selectionMode ? "mediaLibrary.selection.exit" : "mediaLibrary.action.bulkManage")}
            </Button>
          )}
          <Button
            color="primary"
            startContent={<AiOutlinePlus aria-hidden className="text-lg" />}
            onPress={() => setEditor({})}
          >
            {t("mediaLibrary.action.add")}
          </Button>
        </div>
      </header>

      <CatalogSourceStatus {...catalog} />
      {loadError && (
        <div
          className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-danger/20 bg-danger/5 p-4"
          role="alert"
        >
          <p className="text-sm text-danger">{t("mediaLibrary.error.load")}</p>
          <Button isLoading={loading} size="sm" variant="flat" onPress={loadMediaLibraries}>
            {t("mediaLibrary.action.retry")}
          </Button>
        </div>
      )}

      {(allLibraries.length > 0 || initialLoading) && (
        <section aria-label={t("mediaLibrary.toolbar.label")} className={styles.toolbar}>
          <div className="flex flex-wrap items-center gap-3">
            <Input
              isClearable
              aria-label={t("mediaLibrary.search.label")}
              className="min-w-0 flex-1 basis-52"
              classNames={{ inputWrapper: "border border-default-200/70 bg-content1 shadow-none" }}
              placeholder={t("mediaLibrary.search.placeholder")}
              startContent={
                <AiOutlineSearch aria-hidden className="shrink-0 text-lg text-default-400" />
              }
              value={keyword}
              onClear={() => setKeyword("")}
              onValueChange={setKeyword}
            />
            <Button
              isIconOnly
              aria-label={t("mediaLibrary.action.refresh")}
              isLoading={loading || catalog.loading}
              variant="light"
              onPress={() => {
                void loadMediaLibraries();
                catalog.refresh();
              }}
            >
              <AiOutlineReload aria-hidden className="text-lg" />
            </Button>
          </div>
          <MediaLibraryOrderToolbar
            disabled={!orderingReady}
            entries={allLibraries.map((library) => ({
              key: libraryKey(library),
              label: library.name,
            }))}
          />
          {order.error && (
            <p className="text-xs text-danger" role="alert">
              {t("mediaLibrary.optionOrder.saveFailed")}
            </p>
          )}
          <div
            aria-label={t("mediaLibrary.filter.label")}
            className="flex flex-wrap gap-1"
            role="group"
          >
            {(["all", "withResources", "empty"] as const).map((value) => (
              <Button
                key={value}
                aria-pressed={filter === value}
                className="gap-2"
                color={filter === value ? "primary" : "default"}
                size="sm"
                variant={filter === value ? "flat" : "light"}
                onPress={() => setFilter(value)}
              >
                {t(`mediaLibrary.filter.${value}`)}
                <span aria-hidden className="text-xs tabular-nums opacity-70">
                  {counts[value]}
                </span>
              </Button>
            ))}
          </div>
          {selectionMode && (
            <div
              aria-label={t("mediaLibrary.selection.label")}
              className="flex flex-wrap items-center gap-3"
              id="media-library-selection-toolbar"
              role="group"
            >
              <Checkbox
                isDisabled={selectableLibraries.length === 0}
                isIndeterminate={
                  selectedVisibleCount > 0 && selectedVisibleCount < selectableLibraries.length
                }
                isSelected={
                  selectableLibraries.length > 0 &&
                  selectedVisibleCount === selectableLibraries.length
                }
                onValueChange={(selected) => changeSelection(selectableLibraries, selected)}
              >
                {t("mediaLibrary.selection.selectVisible")}
              </Checkbox>
              {selectedLibraries.length > 0 && (
                <>
                  <span aria-live="polite" className="text-sm text-default-500">
                    {t("mediaLibrary.selection.count", { count: selectedLibraries.length })}
                  </span>
                  <Button size="sm" variant="light" onPress={() => setSelectedKeys(new Set())}>
                    {t("mediaLibrary.selection.clear")}
                  </Button>
                  <Button
                    color="danger"
                    isDisabled={selectedLibraries.some((library) => !canEditCatalogItem(library))}
                    size="sm"
                    startContent={<MdOutlineDelete aria-hidden />}
                    variant="flat"
                    onPress={() => deleteLibraries(selectedLibraries)}
                  >
                    {t("mediaLibrary.action.deleteSelected")}
                  </Button>
                </>
              )}
            </div>
          )}
        </section>
      )}

      <section aria-busy={loading || catalog.loading} aria-label={t("mediaLibrary.list.label")}>
        {initialLoading ? (
          <div aria-label={t("mediaLibrary.label.loading")} className={styles.grid} role="status">
            {[0, 1, 2, 3, 4, 5].map((index) => (
              <div key={index} className={styles.skeleton} />
            ))}
          </div>
        ) : allLibraries.length === 0 && !loadError ? (
          <div className={styles.empty}>
            <div aria-hidden className={styles.emptyIcon}>
              <HiOutlineCollection />
            </div>
            <h2 className="mt-6 text-xl font-semibold">{t("mediaLibrary.emptyState.title")}</h2>
            <p className="mt-2 max-w-md text-sm leading-7 text-default-500">
              {t("mediaLibrary.emptyState.description")}
            </p>
            <Button
              className="mt-6"
              color="primary"
              startContent={<AiOutlinePlus aria-hidden />}
              onPress={() => setEditor({})}
            >
              {t("mediaLibrary.action.createFirst")}
            </Button>
            <p className="mt-5 text-xs text-default-400">{t("mediaLibrary.emptyState.example")}</p>
          </div>
        ) : allLibraries.length > 0 && visibleLibraries.length === 0 ? (
          <div className={styles.empty} role="status">
            <AiOutlineSearch aria-hidden className="text-4xl text-default-300" />
            <h2 className="mt-4 text-lg font-semibold">{t("mediaLibrary.search.empty.title")}</h2>
            <p className="mt-2 text-sm text-default-500">
              {t("mediaLibrary.search.empty.description")}
            </p>
            <Button className="mt-5" variant="flat" onPress={clearSearch}>
              {t("mediaLibrary.action.clearSearch")}
            </Button>
          </div>
        ) : (
          <DndContext collisionDetection={closestCenter} sensors={sensors} onDragEnd={moveLibrary}>
            <SortableContext
              items={visibleLibraries.map(libraryKey)}
              strategy={rectSortingStrategy}
            >
              <div className={styles.grid}>
                {visibleLibraries.map((library) => (
                  <SortableLibraryCard
                    key={catalogItemKey(library)}
                    className={`${styles.card} ${highlightedKey === catalogItemKey(library) || selectedKeys.has(catalogItemKey(library)) ? styles.highlighted : ""}`}
                    disabled={order.saving || !orderingReady}
                    domId={`media-library-${catalogItemKey(library)}`}
                    dragLabel={t("mediaLibrary.optionOrder.drag", { name: library.name })}
                    id={libraryKey(library)}
                    style={{ "--library-color": library.color || "#6366f1" } as CSSProperties}
                  >
                    {(handle) => (
                      <>
                        <div className={styles.cardHeader}>
                          <div className="flex items-center gap-2">
                            {handle}
                            {selectionMode && (
                              <Checkbox
                                aria-label={t("mediaLibrary.selection.selectNamed", {
                                  name: library.name,
                                })}
                                isDisabled={!canEditCatalogItem(library)}
                                isSelected={selectedKeys.has(catalogItemKey(library))}
                                onValueChange={(selected) => changeSelection([library], selected)}
                              />
                            )}
                            <div aria-hidden className={styles.libraryIcon}>
                              <HiOutlineCollection />
                            </div>
                          </div>
                          <Dropdown>
                            <DropdownTrigger>
                              <Button
                                isIconOnly
                                aria-label={t("mediaLibrary.action.manageNamed", {
                                  name: library.name,
                                })}
                                className="text-default-500"
                                isDisabled={!canEditCatalogItem(library)}
                                size="sm"
                                variant="light"
                              >
                                <AiOutlineMore aria-hidden className="text-xl" />
                              </Button>
                            </DropdownTrigger>
                            <DropdownMenu
                              aria-label={t("mediaLibrary.action.manageNamed", {
                                name: library.name,
                              })}
                            >
                              <DropdownItem
                                key="edit"
                                startContent={<AiOutlineEdit aria-hidden />}
                                onPress={() => setEditor({ library })}
                              >
                                {t("mediaLibrary.action.edit")}
                              </DropdownItem>
                              <DropdownItem
                                key="delete"
                                className="text-danger"
                                color="danger"
                                startContent={<MdOutlineDelete aria-hidden />}
                                onPress={() => deleteLibraries([library])}
                              >
                                {t("mediaLibrary.action.delete")}
                              </DropdownItem>
                            </DropdownMenu>
                          </Dropdown>
                        </div>
                        <div className={styles.cardContent}>
                          <h2 className={styles.libraryName} title={library.name}>
                            {library.name}
                          </h2>
                          <div className="flex min-h-5 items-center gap-2 text-xs text-default-400">
                            <CatalogOwnerMark owner={library.owner} />
                            <span>
                              {library.resourceCount > 0
                                ? t("mediaLibrary.card.collection")
                                : t("mediaLibrary.card.empty")}
                            </span>
                          </div>
                          <div className="mt-5 flex items-baseline gap-2">
                            <span className="text-3xl font-semibold tracking-tight tabular-nums">
                              {(library.resourceCount ?? 0).toLocaleString()}
                            </span>
                            <span className="whitespace-nowrap text-sm text-default-500">
                              {t("mediaLibrary.label.resources")}
                            </span>
                          </div>
                        </div>
                        <div className={styles.cardFooter}>
                          <Button
                            aria-label={t("mediaLibrary.action.browseNamed", {
                              name: library.name,
                            })}
                            className="w-full justify-between bg-default-100/70 text-default-700"
                            endContent={
                              <AiOutlineArrowRight aria-hidden className={styles.browseArrow} />
                            }
                            variant="flat"
                            onPress={() => browse(library)}
                          >
                            {t("mediaLibrary.action.browse")}
                          </Button>
                        </div>
                      </>
                    )}
                  </SortableLibraryCard>
                ))}
              </div>
            </SortableContext>
          </DndContext>
        )}
      </section>

      <aside className={styles.guide}>
        <div aria-hidden className="rounded-xl bg-primary/10 p-3 text-xl text-primary">
          <AiOutlineFolderOpen />
        </div>
        <div className="min-w-0 flex-1">
          <h2 className="text-sm font-medium">{t("mediaLibrary.guide.title")}</h2>
          <p className="mt-1 text-xs leading-6 text-default-500">
            {t("mediaLibrary.guide.description")}
          </p>
        </div>
        <Button
          className="shrink-0"
          endContent={<AiOutlineArrowRight aria-hidden />}
          size="sm"
          variant="light"
          onPress={() => navigate("/path-mark-config")}
        >
          {t("mediaLibrary.action.goToPathMarkConfig")}
        </Button>
        <HelpCenterButton topic="pathMark" />
      </aside>

      {editor && (
        <MediaLibraryEditor
          library={editor.library}
          onClose={() => setEditor(undefined)}
          onSave={saveLibrary}
        />
      )}
    </div>
  );
};

export default MediaLibraryPage;

"use client";

import type { CollectionModel } from "@/stores/collections";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate } from "react-router-dom";
import { AiOutlinePlusCircle, AiOutlineSearch } from "react-icons/ai";
import { HiOutlineCollection } from "react-icons/hi";
import { MdOutlineDelete } from "react-icons/md";
import { useShallow } from "zustand/react/shallow";

import CompositionBar from "./components/CompositionBar";
import ProgressChip from "./components/ProgressChip";
import { buildCollectionSearch } from "./helpers";

import {
  useOwnedCatalog,
  catalogItemKey,
  catalogItemApi,
  canEditCatalogItem,
  CatalogOwnerMark,
  CatalogSourceStatus,
  catalogOwnerSearch,
  type OwnedCatalogItem,
} from "@/features/management/ownedCatalog";
import BApi from "@/sdk/BApi";
import { HelpCenterButton } from "@/components/HelpCenter";
import {
  Button,
  Card,
  CardBody,
  Chip,
  ColorPicker,
  Input,
  Modal,
  Spinner,
  Tooltip,
  toast,
} from "@/components/bakaui";
import { buildColorValueString } from "@/components/bakaui/components/ColorPicker";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { selectCollectionList, useCollectionsStore } from "@/stores/collections";
import { usePendingSearchStore } from "@/stores/pendingSearch";

const CollectionPage = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const { createPortal } = useBakabaseContext();

  const collections = useCollectionsStore(useShallow(selectCollectionList));
  const setCollections = useCollectionsStore((s) => s.setCollections);
  const setPendingSearch = usePendingSearchStore((s) => s.setPendingSearch);

  const catalog = useOwnedCatalog(collections, (api) =>
    api.collection.getAllCollections({ withProgress: true }),
  );
  const allCollections = [...catalog.items].sort(
    (a, b) =>
      (a.order ?? 0) - (b.order ?? 0) ||
      (a.id ?? 0) - (b.id ?? 0) ||
      catalogItemKey(a).localeCompare(catalogItemKey(b)),
  );
  const detailUrl = (collection: OwnedCatalogItem<CollectionModel>) =>
    `/collections/detail?id=${collection.id}${collection.owner && !collection.owner.isLocal ? `&serverId=${encodeURIComponent(collection.owner.serverId)}&libraryEpoch=${encodeURIComponent(collection.owner.libraryEpoch)}` : ""}`;
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");

  const load = useCallback(async () => {
    try {
      const rsp = await BApi.collection.getAllCollections({ withProgress: true });

      setCollections((rsp.data ?? []) as CollectionModel[]);
    } finally {
      setLoading(false);
    }
  }, [setCollections]);

  useEffect(() => {
    void load();
  }, [load]);

  const add = async () => {
    const name = `${t<string>("collection.defaultName")} ${collections.length + 1}`;
    const rsp = await BApi.collection.addCollection({ name, autoAcquire: false, order: 0 });

    await load();

    if (rsp.data?.id) navigate(`/collections/detail?id=${rsp.data.id}`);
  };

  const remove = (collection: OwnedCatalogItem<CollectionModel>) => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("collection.confirm.deleteTitle", { name: collection.name }),
      children: (
        <div className="flex flex-col gap-1">
          <span>{t<string>("collection.confirm.delete")}</span>
          <span className="text-default-500 text-sm">
            {t<string>("collection.confirm.deleteKeepsResources")}
          </span>
        </div>
      ),
      onOk: async () => {
        await catalogItemApi(collection).collection.deleteCollection(collection.id);
        toast.success(t<string>("collection.deleted"));
        await load();
        catalog.refresh();
      },
      footer: {
        actions: ["ok", "cancel"],
        okProps: { children: t<string>("collection.action.delete"), color: "danger" },
      },
    });
  };

  /** The collection, on the resource page. Membership is a property, so this is just a filter. */
  const openInResourcePage = (collection: OwnedCatalogItem<CollectionModel>) => {
    setPendingSearch({
      ...buildCollectionSearch(collection),
      ...catalogOwnerSearch(collection.owner),
    } as any);

    if (location.pathname !== "/resource") navigate("/resource");
  };

  const shown = keyword
    ? allCollections.filter((c) => c.name.toLowerCase().includes(keyword.toLowerCase()))
    : allCollections;

  const renderCard = (collection: OwnedCatalogItem<CollectionModel>) => (
    <div
      key={catalogItemKey(collection)}
      aria-label={collection.name}
      className="h-full cursor-pointer rounded-large outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
      role="link"
      tabIndex={0}
      onClick={(event) => {
        const target = event.target;

        // Controls and portalled popovers keep their own actions.
        if (
          !(target instanceof Element) ||
          !event.currentTarget.contains(target) ||
          target.closest(
            'button, a, input, textarea, select, [role="button"], [role="combobox"], [contenteditable="true"]',
          )
        ) {
          return;
        }

        navigate(detailUrl(collection));
      }}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget || event.repeat) return;
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          navigate(detailUrl(collection));
        }
      }}
    >
      <Card className="h-full transition-shadow hover:shadow-md" shadow="sm">
        <CardBody className="p-4 flex flex-col gap-3">
          <div className="flex items-center justify-between gap-2">
            <CatalogOwnerMark owner={collection.owner} />
            <h2
              className="min-w-0 flex-1 break-words text-base font-medium"
              style={{ color: collection.color ?? undefined }}
            >
              {collection.name}
            </h2>
            {canEditCatalogItem(collection) && (
              <ColorPicker
                color={collection.color ?? undefined}
                onChange={async (color) => {
                  const api = catalogItemApi(collection);
                  // Replica rows intentionally omit acquisition settings. Fetch the owner's full
                  // editor model before a PUT so a color edit cannot erase unreplicated settings.
                  const current =
                    collection.owner && !collection.owner.isLocal
                      ? (await api.collection.getCollection(collection.id)).data
                      : collection;

                  if (!current) throw new Error(t("collection.notFound"));
                  await api.collection.putCollection(collection.id, {
                    name: current.name,
                    description: current.description,
                    coverPath: current.coverPath,
                    ruleSearchJson: current.ruleSearchJson,
                    autoAcquire: current.autoAcquire,
                    acquisitionSettingsJson: current.acquisitionSettingsJson,
                    order: current.order,
                    color: buildColorValueString(color),
                  });
                  await load();
                  catalog.refresh();
                }}
              />
            )}
          </div>

          <CompositionBar collection={collection} />

          <div className="flex items-center gap-2">
            <ProgressChip collection={collection} />
            {collection.hasRule && (
              <Tooltip content={t<string>("collection.rule.tip")}>
                <Chip color="secondary" size="sm" variant="flat">
                  {t<string>("collection.rule.title")}
                </Chip>
              </Tooltip>
            )}
          </div>

          <div className="flex items-center gap-1">
            <Tooltip content={t<string>("collection.action.openInResourcePage")}>
              <Button
                isIconOnly
                aria-label={t<string>("collection.action.openInResourcePage")}
                size="sm"
                variant="light"
                onPress={() => openInResourcePage(collection)}
              >
                <AiOutlineSearch className="text-base" />
              </Button>
            </Tooltip>
            <Tooltip content={t<string>("collection.action.delete")}>
              <Button
                isIconOnly
                aria-label={t<string>("collection.action.delete")}
                className="ml-auto"
                color="danger"
                isDisabled={!canEditCatalogItem(collection)}
                size="sm"
                variant="light"
                onPress={() => remove(collection)}
              >
                <MdOutlineDelete className="text-base" />
              </Button>
            </Tooltip>
          </div>
        </CardBody>
      </Card>
    </div>
  );

  if (loading) {
    return (
      <div className="flex justify-center py-16">
        <Spinner />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4 p-2">
      <div className="flex items-center gap-2">
        <Button
          color="primary"
          size="sm"
          startContent={<AiOutlinePlusCircle className="text-base" />}
          onPress={add}
        >
          {t<string>("collection.action.create")}
        </Button>
        <Input
          className="max-w-xs"
          placeholder={t<string>("collection.action.search")}
          size="sm"
          value={keyword}
          onValueChange={setKeyword}
        />
        <HelpCenterButton topic="collection" />
      </div>

      <CatalogSourceStatus {...catalog} />
      {allCollections.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-16 gap-4 text-center">
          <div className="w-20 h-20 rounded-full bg-secondary/10 flex items-center justify-center">
            <HiOutlineCollection className="text-4xl text-secondary" />
          </div>
          <div className="space-y-2 max-w-lg">
            <h2 className="text-xl font-semibold">{t<string>("collection.empty.title")}</h2>
            <div className="text-default-500 text-sm leading-relaxed">
              {t<string>("collection.empty.description")}
            </div>
          </div>
          <Button color="primary" onPress={add}>
            {t<string>("collection.action.createFirst")}
          </Button>
        </div>
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-3">
          {shown.map(renderCard)}
        </div>
      )}
    </div>
  );
};

CollectionPage.displayName = "CollectionPage";

export default CollectionPage;

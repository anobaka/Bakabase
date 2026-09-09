"use client";

import type { CollectionModel } from "@/stores/collections";
import type { InputProps } from "@heroui/react";
import type { ButtonProps } from "@/components/bakaui";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate } from "react-router-dom";
import { AiOutlinePlusCircle, AiOutlineSearch } from "react-icons/ai";
import { HiOutlineCollection } from "react-icons/hi";
import { MdOutlineDelete } from "react-icons/md";

import CompositionBar from "./components/CompositionBar";
import { buildCollectionSearch, percent } from "./helpers";

import BApi from "@/sdk/BApi";
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
import { EditableValue } from "@/components/EditableValue";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { selectCollectionList, useCollectionsStore } from "@/stores/collections";
import { usePendingSearchStore } from "@/stores/pendingSearch";

const CollectionPage = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const { createPortal } = useBakabaseContext();

  const collections = useCollectionsStore(selectCollectionList);
  const setCollections = useCollectionsStore((s) => s.setCollections);
  const setPendingSearch = usePendingSearchStore((s) => s.setPendingSearch);

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

  const remove = (collection: CollectionModel) => {
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
        await BApi.collection.deleteCollection(collection.id);
        toast.success(t<string>("collection.deleted"));
        await load();
      },
      footer: {
        actions: ["ok", "cancel"],
        okProps: { children: t<string>("collection.action.delete"), color: "danger" },
      },
    });
  };

  /** The collection, on the resource page. Membership is a property, so this is just a filter. */
  const openInResourcePage = (collection: CollectionModel) => {
    setPendingSearch(buildCollectionSearch(collection) as any);

    if (location.pathname !== "/resource") navigate("/resource");
  };

  const rename = async (collection: CollectionModel, name: string) => {
    if (!name) {
      toast.danger(t<string>("collection.error.nameEmpty"));

      return;
    }

    await BApi.collection.putCollection(collection.id, { ...collection, name });
    await load();
  };

  const shown = keyword
    ? collections.filter((c) => c.name.toLowerCase().includes(keyword.toLowerCase()))
    : collections;

  const renderCard = (collection: CollectionModel) => (
    <Card key={collection.id} shadow="sm">
      <CardBody className="p-4 flex flex-col gap-3">
        <div className="flex items-center justify-between gap-2">
          <EditableValue<string, InputProps, ButtonProps & { value: string }>
            Editor={Input}
            Viewer={({ value, ...props }) => (
              <Button
                className="whitespace-break-spaces h-auto text-left font-medium text-base px-2 min-w-0"
                size="sm"
                style={{ color: collection.color ?? undefined }}
                variant="light"
                {...props}
              >
                <span className="truncate">{value}</span>
              </Button>
            )}
            editorProps={{ size: "sm", isRequired: true }}
            trigger="viewer"
            value={collection.name}
            onSubmit={(v) => rename(collection, (v ?? "").trim())}
          />
          <ColorPicker
            color={collection.color ?? undefined}
            onChange={async (color) => {
              await BApi.collection.putCollection(collection.id, {
                ...collection,
                color: buildColorValueString(color),
              });
              await load();
            }}
          />
        </div>

        <CompositionBar collection={collection} />

        <div className="flex items-center gap-2">
          <Chip
            color={percent(collection) === 100 ? "success" : "default"}
            size="sm"
            variant="flat"
          >
            {t<string>("collection.percentComplete", { percent: percent(collection) })}
          </Chip>
          {collection.hasRule && (
            <Tooltip content={t<string>("collection.rule.tip")}>
              <Chip color="secondary" size="sm" variant="flat">
                {t<string>("collection.rule.title")}
              </Chip>
            </Tooltip>
          )}
        </div>

        <div className="flex items-center gap-1">
          <Button
            color="primary"
            size="sm"
            variant="light"
            onPress={() => navigate(`/collections/detail?id=${collection.id}`)}
          >
            {t<string>("collection.action.open")}
          </Button>
          <Tooltip content={t<string>("collection.action.openInResourcePage")}>
            <Button
              isIconOnly
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
              className="ml-auto"
              color="danger"
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
      </div>

      {collections.length === 0 ? (
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

"use client";

import type { IProperty } from "@/components/Property/models";

import { useTranslation } from "react-i18next";
import { useCallback, useEffect, useMemo, useState } from "react";
import {
  DeleteOutlined,
  EditOutlined,
  MoreOutlined,
  PlusOutlined,
  SearchOutlined,
  SortAscendingOutlined,
  SwapOutlined,
} from "@ant-design/icons";

import PropertySummary from "./components/PropertySummary";
import TypeConversionRuleOverviewDialog from "./components/TypeConversionRuleOverviewDialog";

import { ResourceOwnerProvider } from "@/features/management/resourceContext";
import {
  useOwnedCatalog,
  catalogItemKey,
  catalogItemApi,
  canEditCatalogItem,
  CatalogOwnerMark,
  CatalogSourceStatus,
  type OwnedCatalogItem,
} from "@/features/management/ownedCatalog";
import PropertyModal from "@/components/PropertyModal";
import { propertyHasOptions } from "@/features/data-sync/components/EntitySyncList";
import {
  DataSyncEmptyStateLine,
  DataSyncHeaderLink,
  DefinitionSyncRow,
  useDefinitionSync,
} from "@/features/data-sync/components/DefinitionsPageSync";
import BApi from "@/sdk/BApi";
import { CustomPropertyAdditionalItem, PropertyType } from "@/sdk/constants";
import {
  Button,
  Input,
  Listbox,
  ListboxItem,
  Modal,
  Popover,
  Select,
  Spinner,
  Tooltip,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import CustomPropertySortModal from "@/components/CustomPropertySortModal";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";

const CustomPropertyPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [properties, setProperties] = useState<IProperty[]>([]);
  const catalog = useOwnedCatalog(properties, (api) =>
    api.customProperty.getAllCustomProperties({
      additionalItems: CustomPropertyAdditionalItem.ValueCount,
    }),
  );
  const allProperties = useMemo(
    () =>
      [...catalog.items].sort(
        (a, b) =>
          (a.order ?? 0) - (b.order ?? 0) ||
          a.name.localeCompare(b.name) ||
          catalogItemKey(a).localeCompare(catalogItemKey(b)),
      ),
    [catalog.items],
  );
  const [editing, setEditing] = useState<OwnedCatalogItem<IProperty>>();
  const editingSource = editing?.owner
    ? (catalog.sources.find(
        (source) =>
          source.serverId === editing.owner!.serverId &&
          source.libraryEpoch === editing.owner!.libraryEpoch,
      ) ?? { ...editing.owner, available: false })
    : undefined;
  const [keyword, setKeyword] = useState("");
  const [typeFilter, setTypeFilter] = useState("all");
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [moreOpen, setMoreOpen] = useState(false);

  const loadProperties = useCallback(async () => {
    setLoading(true);
    setLoadFailed(false);
    try {
      const rsp = await BApi.customProperty.getAllCustomProperties({
        additionalItems: CustomPropertyAdditionalItem.ValueCount,
      });

      if (rsp.code) {
        setLoadFailed(true);

        return;
      }
      const next = (rsp.data ?? []) as IProperty[];

      setProperties(next.sort((a, b) => (a.order ?? 0) - (b.order ?? 0)));
    } catch {
      setLoadFailed(true);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadProperties();
  }, [loadProperties]);
  const reload = useCallback(async () => {
    await loadProperties();
    catalog.refresh();
  }, [loadProperties, catalog.refresh]);
  const dataSync = useDefinitionSync("customProperty", reload);

  const typeOptions = useMemo(() => {
    const counts = new Map<PropertyType, number>();

    for (const property of allProperties)
      counts.set(property.type, (counts.get(property.type) ?? 0) + 1);

    return [
      { value: "all", label: t("customProperty.filter.allTypes") },
      ...Array.from(counts.entries())
        .sort(([a], [b]) => a - b)
        .map(([type, count]) => ({
          value: String(type),
          label: `${t(`PropertyType.${PropertyType[type]}`)} (${count})`,
        })),
    ];
  }, [allProperties, t]);
  const query = keyword.trim().toLocaleLowerCase();
  const filteredProperties = allProperties.filter(
    (property) =>
      property.name.toLocaleLowerCase().includes(query) &&
      (typeFilter === "all" || String(property.type) === typeFilter),
  );
  const hasFilters = query.length > 0 || typeFilter !== "all";

  const openEditor = (property?: OwnedCatalogItem<IProperty>) => {
    if (property && !canEditCatalogItem(property)) return;
    if (property?.owner && !property.owner.isLocal) {
      setEditing(property);

      return;
    }
    // Type conversion persists before the editor closes, including when the outer form is cancelled.
    createPortal(PropertyModal, { value: property, onDestroyed: loadProperties });
  };
  const deleteProperty = (property: OwnedCatalogItem<IProperty>) => {
    if (!canEditCatalogItem(property)) return;
    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("customProperty.confirm.deleteTitle", { name: property.name }),
      children: t<string>("customProperty.confirm.deleteDescription"),
      footer: {
        actions: ["ok", "cancel"],
        okProps: { color: "danger", children: t<string>("common.action.delete") },
        cancelProps: { children: t<string>("common.action.cancel") },
      },
      onOk: async () => {
        const response = await catalogItemApi(property).customProperty.removeCustomProperty(
          property.id,
        );

        if (response.code) throw new Error(response.message ?? t("customProperty.error.delete"));
        await reload();
      },
    });
  };

  return (
    <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6 pb-6">
      {editing && (
        <ResourceOwnerProvider key={catalogItemKey(editing)} source={editingSource}>
          <PropertyModal
            value={editing}
            onDestroyed={() => {
              setEditing(undefined);
              void reload();
            }}
          />
        </ResourceOwnerProvider>
      )}
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0 flex-1">
          <h1 className="text-xl font-semibold text-default-900">
            {t<string>("customProperty.title")}
          </h1>
          <p className="mt-1.5 max-w-2xl text-sm leading-relaxed text-default-500">
            {t<string>("customProperty.description")}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <DataSyncHeaderLink />
          <Button
            color="primary"
            size="sm"
            startContent={<PlusOutlined />}
            onPress={() => openEditor()}
          >
            {t<string>("customProperty.action.create")}
          </Button>
          <Popover
            placement="bottom-end"
            trigger={
              <Button
                isIconOnly
                aria-label={t<string>("customProperty.action.more")}
                size="sm"
                variant="light"
              >
                <MoreOutlined className="text-lg" />
              </Button>
            }
            visible={moreOpen}
            onVisibleChange={setMoreOpen}
          >
            <Listbox
              aria-label={t<string>("customProperty.action.more")}
              onAction={(key) => {
                setMoreOpen(false);
                if (key === "sort")
                  createPortal(CustomPropertySortModal, {
                    properties: allProperties,
                    onDestroyed: reload,
                  });
                else if (key === "conversion") createPortal(TypeConversionRuleOverviewDialog, {});
              }}
            >
              <ListboxItem key="sort" startContent={<SortAscendingOutlined />}>
                {t<string>("customProperty.action.adjustDisplayOrders")}
              </ListboxItem>
              <ListboxItem key="conversion" startContent={<SwapOutlined />}>
                {t<string>("customProperty.action.checkTypeConversionRules")}
              </ListboxItem>
            </Listbox>
          </Popover>
        </div>
      </header>
      {dataSync.host}
      <CatalogSourceStatus {...catalog} />
      <div className="flex flex-wrap items-center gap-3">
        <Input
          isClearable
          aria-label={t<string>("customProperty.search.label")}
          className="w-full sm:max-w-xs"
          classNames={{ inputWrapper: "h-9 min-h-9" }}
          placeholder={t<string>("customProperty.search.placeholder")}
          size="sm"
          startContent={<SearchOutlined className="text-base text-default-400" />}
          value={keyword}
          onValueChange={setKeyword}
        />
        <Select
          disallowEmptySelection
          aria-label={t<string>("customProperty.filter.type")}
          className="w-44"
          classNames={{ trigger: "h-9 min-h-9" }}
          dataSource={typeOptions}
          selectedKeys={[typeFilter]}
          size="sm"
          onSelectionChange={(keys) => setTypeFilter(String(Array.from(keys)[0] ?? "all"))}
        />
        <span className="text-xs text-default-500 sm:ml-auto" role="status">
          {t<string>("customProperty.resultCount", {
            count: filteredProperties.length,
            total: allProperties.length,
          })}
        </span>
      </div>
      {loading ? (
        <div className="flex min-h-52 items-center justify-center">
          <Spinner label={t<string>("common.state.loading")} size="sm" />
        </div>
      ) : loadFailed && allProperties.length === 0 ? (
        <div className="flex min-h-52 flex-col items-center justify-center gap-3 text-default-500">
          <p>{t<string>("customProperty.error.load")}</p>
          <Button size="sm" variant="flat" onPress={loadProperties}>
            {t<string>("common.action.retry")}
          </Button>
        </div>
      ) : filteredProperties.length === 0 ? (
        <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-2xl bg-default-50/50 p-6 text-center">
          <SearchOutlined className="text-2xl text-default-400" />
          <div>
            <h2 className="text-base font-medium">
              {t<string>(
                hasFilters ? "customProperty.empty.noMatch" : "customProperty.empty.title",
              )}
            </h2>
            <p className="mt-1 text-sm text-default-500">
              {t<string>(
                hasFilters ? "customProperty.empty.tryAgain" : "customProperty.empty.description",
              )}
            </p>
          </div>
          <Button
            color="primary"
            size="sm"
            variant="flat"
            onPress={() => {
              if (hasFilters) {
                setKeyword("");
                setTypeFilter("all");
              } else openEditor();
            }}
          >
            {t<string>(
              hasFilters ? "customProperty.action.clearFilters" : "customProperty.action.create",
            )}
          </Button>
          {!hasFilters && <DataSyncEmptyStateLine />}
        </div>
      ) : (
        <div className="overflow-hidden rounded-2xl bg-content1">
          <div className="hidden grid-cols-[minmax(200px,1.2fr)_minmax(160px,1.5fr)_4rem_4.5rem] items-center gap-x-5 border-b border-default-200/60 px-4 py-3 text-xs font-medium text-default-500 lg:grid">
            <span>{t<string>("customProperty.column.property")}</span>
            <span>{t<string>("customProperty.column.configuration")}</span>
            <Tooltip content={t<string>("customProperty.column.valuesHelp")}>
              <span className="text-right">{t<string>("customProperty.column.values")}</span>
            </Tooltip>
            <span className="text-right">{t<string>("customProperty.column.actions")}</span>
          </div>
          <ul className="divide-y divide-default-100">
            {filteredProperties.map((property) => (
              <li
                key={catalogItemKey(property)}
                className="group grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-5 gap-y-2 px-4 py-4 transition-colors hover:bg-default-50/60 focus-within:bg-default-50/60 lg:grid-cols-[minmax(200px,1.2fr)_minmax(160px,1.5fr)_4rem_4.5rem]"
              >
                <div className="min-w-0">
                  <button
                    className="max-w-full rounded text-left text-sm font-medium text-default-800 [overflow-wrap:anywhere] hover:text-primary focus-visible:outline-2 focus-visible:outline-primary"
                    disabled={!canEditCatalogItem(property)}
                    type="button"
                    onClick={() => openEditor(property)}
                  >
                    {property.name}
                  </button>
                  <CatalogOwnerMark owner={property.owner} />
                  <div className="mt-1.5 flex flex-wrap items-center gap-3 text-default-500">
                    <PropertyTypeIcon type={property.type} />
                    <span className="text-xs tabular-nums lg:hidden">
                      {property.valueCount == null
                        ? "—"
                        : t<string>("customProperty.valueCount", { count: property.valueCount })}
                    </span>
                    {(!property.owner || property.owner.isLocal) && (
                      <DefinitionSyncRow
                        kind="customProperty"
                        localKey={String(property.id)}
                        name={property.name}
                        offersDefinitionOnly={propertyHasOptions(property.type)}
                        sync={dataSync}
                      />
                    )}
                  </div>
                </div>
                <div className="col-span-2 row-start-2 min-w-0 lg:col-span-1 lg:row-auto">
                  <PropertySummary property={property} />
                </div>
                <span className="hidden text-right text-sm tabular-nums text-default-500 lg:block">
                  {property.valueCount?.toLocaleString() ?? "—"}
                </span>
                <div className="col-start-2 row-start-1 flex items-center justify-end gap-1 lg:col-auto lg:row-auto">
                  <Tooltip content={t<string>("common.action.edit")}>
                    <Button
                      isIconOnly
                      aria-label={t<string>("customProperty.action.editNamed", {
                        name: property.name,
                      })}
                      isDisabled={!canEditCatalogItem(property)}
                      size="sm"
                      variant="light"
                      onPress={() => openEditor(property)}
                    >
                      <EditOutlined className="text-base" />
                    </Button>
                  </Tooltip>
                  <Tooltip content={t<string>("common.action.delete")}>
                    <Button
                      isIconOnly
                      aria-label={t<string>("customProperty.action.deleteNamed", {
                        name: property.name,
                      })}
                      className="text-default-400 hover:text-danger"
                      isDisabled={!canEditCatalogItem(property)}
                      size="sm"
                      variant="light"
                      onPress={() => deleteProperty(property)}
                    >
                      <DeleteOutlined className="text-base" />
                    </Button>
                  </Tooltip>
                </div>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
};

CustomPropertyPage.displayName = "CustomPropertyPage";
export default CustomPropertyPage;

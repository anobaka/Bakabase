"use client";

import React, { useEffect, useReducer, useRef, useState } from "react";
import "./index.scss";
import dayjs from "dayjs";
import { useTranslation } from "react-i18next";
import { DisconnectOutlined, SettingOutlined } from "@ant-design/icons";
import { useNavigate } from "react-router-dom";
import { TbPackageExport } from "react-icons/tb";

import FeatureStatusTip from "@/components/FeatureStatusTip";
import SortableCategoryList from "@/pages/category/components/SortableCategoryList";
import MediaLibrarySynchronization from "@/pages/category/components/MediaLibrarySynchronization";
import { useResourceOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";
import { CategoryAdditionalItem, MediaLibraryAdditionalItem } from "@/sdk/constants";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import {
  Alert,
  Button,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Input,
  Modal,
  Spinner,
} from "@/components/bakaui";
import HandleUnknownResources from "@/components/HandleUnknownResources";
const CategoryPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [categories, setCategories] = useState<any[]>([]);
  const [libraries, setLibraries] = useState<any[]>([]);
  const categoriesLoadedRef = useRef(false);
  // const [enhancers, setEnhancers] = useState([]);
  const resourceOptions = useResourceOptionsStore((state) => state.data);
  const [allComponents, setAllComponents] = useState([]);

  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);

  const [loading, setLoading] = useState(true);
  const [migrating, setMigrating] = useState(false);
  const [, forceUpdate] = useReducer((x) => x + 1, 0);

  // const gotoNewCategoryPage = (noCategory: boolean) => {
  //   history!.push(`/category/setupWizard?noCategory=${noCategory ? 1 : 0}`);
  // };

  const loadAllCategories = (cb: () => void = () => {}): Promise<any> => {
    return BApi.category
      .getAllCategories({
        additionalItems:
          CategoryAdditionalItem.Validation |
          CategoryAdditionalItem.CustomProperties |
          CategoryAdditionalItem.EnhancerOptions,
      })
      .then((rsp) => {
        categoriesLoadedRef.current = true;
        rsp.data?.sort((a, b) => a.order! - b.order!);
        setCategories(rsp.data || []);
        cb && cb();
      });
  };

  const loadAllMediaLibraries = (cb: () => void = () => {}): Promise<any> => {
    return BApi.mediaLibrary
      .getAllMediaLibraries({
        additionalItems:
          MediaLibraryAdditionalItem.Category |
          MediaLibraryAdditionalItem.FileSystemInfo |
          MediaLibraryAdditionalItem.PathConfigurationBoundProperties,
      })
      .then((x) => {
        x.data?.sort((a, b) => a.order - b.order);
        setLibraries(x.data || []);
      });
  };

  async function init() {
    setLoading(true);
    try {
      await loadAllCategories();
      await loadAllMediaLibraries();
      await BApi.component.getComponentDescriptors().then((a) => {
        setAllComponents(a.data || []);
      });
      const er = await BApi.enhancer.getAllEnhancerDescriptors();
      const enhancerData = er.data || [];

      // @ts-ignore
      setEnhancers(enhancerData);
    } finally {
      setLoading(false);
    }
  }

  const reloadCategory = async (id: number) => {
    const c =
      (
        await BApi.category.getCategory(id, {
          additionalItems:
            CategoryAdditionalItem.Validation |
            CategoryAdditionalItem.CustomProperties |
            CategoryAdditionalItem.EnhancerOptions,
        })
      ).data ?? {};
    const idx = categories.findIndex((x) => x.id == id);

    categories[idx] = c;
    setCategories([...categories]);
  };

  const reloadMediaLibrary = async (id: number) => {
    const c =
      (
        await BApi.mediaLibrary.getMediaLibrary(id, {
          additionalItems:
            MediaLibraryAdditionalItem.Category |
            MediaLibraryAdditionalItem.FileSystemInfo |
            MediaLibraryAdditionalItem.PathConfigurationBoundProperties,
        })
      ).data ?? {};
    const idx = libraries.findIndex((x) => x.id == id);

    libraries[idx] = c;
    setLibraries({ ...libraries });
  };

  useEffect(() => {
    console.log("init");
    init();
  }, []);

  // useEffect(() => {
  //   if (categoriesLoadedRef.current) {
  //     const noCategory = !(categories?.length > 0);
  //     if (noCategory) {
  //       gotoNewCategoryPage(true);
  //     }
  //   }
  // }, [categories]);

  // console.log(categories);

  return (
    <div className={"category-page"}>
      <Modal
        defaultVisible
        footer={{ actions: ["cancel"] }}
        size={"lg"}
        title={t<string>("Current media library feature will be removed soon")}
      >
        <Alert
          color={"danger"}
          title={t<string>(
            "Please note that the categorization feature will be removed soon. Please switch to the new media library function as soon as possible.",
          )}
        />
        <Alert
          color={"warning"}
          icon={<TbPackageExport className={"text-lg"} />}
          title={t<string>("You can migrate data button at top of the page to migrate data to the new media library.")}
        />
      </Modal>
      <div className="header mb-1">
        <div className="left">
          <Button
            color={"primary"}
            size={"small"}
            onClick={() => {
              let name = "";

              createPortal(Modal, {
                defaultVisible: true,
                size: "md",
                title: t<string>("Create a category"),
                children: (
                  <div>
                    <Input
                      className={"w-full"}
                      placeholder={t<string>("Please enter the name of the category")}
                      size={"md"}
                      onValueChange={(v) => {
                        name = v;
                      }}
                    />
                    <FeatureStatusTip
                      className={"mt-2"}
                      name={t<string>("Setup wizard")}
                      status={"deprecated"}
                    />
                  </div>
                ),
                onOk: async () => {
                  if (name == undefined || name.length == 0) {
                    throw new Error("Name is required");
                  }
                  await BApi.category.addCategory({
                    name,
                  });
                  loadAllCategories();
                },
              });
            }}
          >
            {t<string>("Add")}
          </Button>
          {categories.length > 1 && (
            <Dropdown>
              <DropdownTrigger>
                <Button className={"elevator"} size={"small"}>
                  {t<string>("QuickJump")}
                </Button>
              </DropdownTrigger>
              <DropdownMenu
                aria-label="Categories"
                selectionMode="single"
                onSelectionChange={(keys) => {
                  const arr = Array.from(keys ?? []);
                  const id = arr?.[0] as number;

                  // console.log(keys, id);
                  document.getElementById(`category-${id}`)?.scrollIntoView();
                }}
              >
                {categories?.map((c) => <DropdownItem key={c.id}>{c.name}</DropdownItem>)}
              </DropdownMenu>
            </Dropdown>
          )}
        </div>
        <div className="right">
          <HandleUnknownResources />
          <Button
            color={"warning"}
            isDisabled={migrating}
            size={"small"}
            variant={"flat"}
            onPress={async () => {
              try {
                setMigrating(true);
                await BApi.migration.migrateCategoriesMediaLibrariesAndResourcesToNewMediaLibrary();
              } finally {
                setMigrating(false);
              }
            }}
          >
            <TbPackageExport className={"text-lg"} />
            {migrating ? t<string>("Migrating...") : t<string>("Migrate data (one-click)")}
          </Button>
          <Button
            color={"primary"}
            size={"small"}
            variant={"light"}
            onPress={() => {
              const navigate = useNavigate();

              navigate("/synchronizationoptions");
            }}
          >
            <SettingOutlined className={"text-base"} />
            {t<string>("Synchronization options")}
          </Button>
          <Button
            color={"default"}
            size={"small"}
            onClick={() => {
              createPortal(Modal, {
                defaultVisible: true,
                title: t<string>("Sort by name"),
                children: t<string>("We'll sort categories by name"),
                onOk: async () => {
                  const orderedCategoryIds = categories
                    .slice()
                    .sort((a, b) => a.name.localeCompare(b.name))
                    .map((a) => a.id);
                  const rsp = await BApi.category.sortCategories({
                    ids: orderedCategoryIds,
                  });

                  if (!rsp.code) {
                    loadAllCategories();
                  }
                },
              });
            }}
          >
            {t<string>("Sort by name")}
          </Button>
          <div className={"last-sync-time"}>
            {t<string>("Last sync time")}:{" "}
            {resourceOptions?.lastSyncDt
              ? dayjs(resourceOptions.lastSyncDt).format("YYYY-MM-DD HH:mm:ss")
              : t<string>("Never")}
          </div>
          <MediaLibrarySynchronization onComplete={() => loadAllMediaLibraries()} />
        </div>
      </div>
      {loading && <Spinner />}
      {categories.length > 0 ? (
        <SortableCategoryList
          allComponents={allComponents}
          categories={categories}
          enhancers={enhancers}
          forceUpdate={forceUpdate}
          libraries={libraries}
          loadAllCategories={loadAllCategories}
          loadAllMediaLibraries={loadAllMediaLibraries}
          reloadCategory={reloadCategory}
          reloadMediaLibrary={reloadMediaLibrary}
        />
      ) : (
        <div className={"flex items-center gap-1 justify-center mt-10"}>
          <DisconnectOutlined className={"text-base"} />
          {t<string>(
            "Categories have not been created yet. To load your resources, please ensure that at least one category is created.",
          )}
        </div>
      )}
    </div>
  );
};

CategoryPage.displayName = "CategoryPage";

export default CategoryPage;

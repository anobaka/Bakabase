"use client";

import React, { useState } from "react";
import { SketchPicker } from "react-color";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useTranslation } from "react-i18next";
import {
  CopyOutlined,
  EditOutlined,
  PictureOutlined,
  SyncOutlined,
  UnorderedListOutlined,
} from "@ant-design/icons";

import AddMediaLibraryInBulkDialog from "./AddMediaLibraryInBulkDialog";
import DisplayNameTemplateEditorDialog from "./DisplayNameTemplateEditorDialog";

import { toast } from "@/components/bakaui";
import { MdWarning } from "react-icons/md";
import { MdDragIndicator } from 'react-icons/md';
import {
  ComponentType,
  componentTypes,
  CoverSelectOrder,
  coverSelectOrders,
} from "@/sdk/constants";
import SortableMediaLibraryList from "@/pages/category/components/SortableMediaLibraryList";
import DragHandle from "@/components/DragHandle";
import BasicCategoryComponentSelector from "@/components/BasicCategoryComponentSelector";
import BApi from "@/sdk/BApi";
import { MdCheck, MdClose } from "react-icons/md";
import CustomPropertyBinderModal from "@/pages/category/components/CustomPropertyBinderModal";
import {
  Button,
  Chip,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Input,
  Modal,
  Tooltip,
} from "@/components/bakaui";
import EnhancerSelectorV2 from "@/components/EnhancerSelectorV2";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import SynchronizationConfirmModal from "@/pages/category/components/SynchronizationConfirmModal";
import DeleteEnhancementsModal from "@/pages/category/components/DeleteEnhancementsModal";
import EnhancerIcon from "@/components/Enhancer/components/EnhancerIcon";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";

const EditMode = {
  CoverSelectOrder: 1,
  NameAndColor: 2,
};

const ComponentTips = {
  [ComponentType.PlayableFileSelector]:
    "Determine which files will be treated as playable files",
  [ComponentType.Player]: "Specify a player to play playable files",
  [ComponentType.Enhancer]:
    "Expand properties of resources, such as publisher(property), publication date(property), tags(property), cover(file), etc",
};

type CategoryColor = {
  hex?: string;
};

type Props = {
  category: any;
  libraries: any[];
  loadAllCategories: () => void;
  loadAllMediaLibraries: () => void;
  reloadCategory: (id: number) => any;
  reloadMediaLibrary: (id: number) => any;
  allComponents: any[];
  enhancers: any[];
  forceUpdate: () => void;
};

export default ({
  category,
  loadAllCategories,
  loadAllMediaLibraries,
  reloadCategory,
  reloadMediaLibrary,
  libraries,
  forceUpdate,
  allComponents,
  enhancers,
}: Props) => {
  const { attributes, listeners, setNodeRef, transform, transition } =
    useSortable({ id: category.id });
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  // console.log(createPortal, 1234567);

  const style = {
    transform: CSS.Translate.toString({
      ...transform,
      scaleY: 1,
    }),
    transition,
  };

  const [categoryColor, setCategoryColor] = useState<CategoryColor>(undefined);
  const [colorPickerVisible, setColorPickerVisible] = useState(false);
  const [editMode, setEditMode] = useState<number>();
  const [name, setName] = useState<string>();

  const clearEditMode = () => {
    setEditMode(undefined);
  };

  const components = category.componentsData || [];
  // console.log('[SortableCategory]rendering', category, enhancers, componentKeys);

  const renderBasicComponentSelector = (
    componentType,
    componentKey = undefined,
  ) => {
    if (
      componentType == ComponentType.PlayableFileSelector ||
      componentType == ComponentType.Player
    ) {
      let newKey;
      createPortal(Modal, {
        defaultVisible: true,
        title: t<string>(ComponentType[componentType]),
        children: (
          <BasicCategoryComponentSelector
            componentType={componentType}
            value={[componentKey]}
            onChange={(componentKeys) => {
              newKey = (componentKeys || [])[0];
            }}
          />
        ),
        onOk: () =>
          new Promise((resolve, reject) => {
            if (newKey) {
              if (componentType == ComponentType.PlayableFileSelector) {
                createPortal(Modal, {
                  classNames: {
                    wrapper: "z-[10000]",
                    backdrop: "z-[10000]",
                    // body: 'z-10000',
                  },
                  defaultVisible: true,
                  title: t<string>(
                    "Are you sure to change playable file selector?",
                  ),
                  children: (
                    <div>
                      {t<string>(
                        "Changing the playable file selector will clear all cached playable file information. The cache for these files will be rebuilt after the operation.",
                      )}
                    </div>
                  ),
                  onOk: async () => {
                    const rsp = await BApi.category.configureCategoryComponents(
                      category.id,
                      {
                        componentKeys: [newKey],
                        type: componentType,
                      },
                    );

                    if (!rsp.code) {
                      reloadCategory(category.id);
                      resolve(rsp);
                    } else {
                      reject();
                    }
                  },
                  onClose: () => {
                    reject();
                  },
                });
              } else {
                return BApi.category
                  .configureCategoryComponents(category.id, {
                    componentKeys: [newKey],
                    type: componentType,
                  })
                  .then((a) => {
                    if (!a.code) {
                      reloadCategory(category.id);
                      resolve(a);
                    } else {
                      reject();
                    }
                  });
              }
            } else {
              reject();
            }
          }),
      });
    }
  };

  const resourceCount = libraries.reduce((s, t) => s + t.resourceCount, 0);

  const renderMediaLibraryAddModal = () => {
    let n;

    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("Add a media library"),
      children: (
        <Input
          defaultValue={n}
          placeholder={t<string>("Name of media library")}
          style={{ width: "100%" }}
          onValueChange={(v) => {
            n = v;
          }}
        />
      ),
      onOk: async () => {
        if (n?.length > 0) {
          const r = await BApi.mediaLibrary.addMediaLibrary({
            categoryId: category.id,
            name: n,
          });

          if (!r.code) {
            loadAllMediaLibraries();
          } else {
            throw new Error(r.message!);
          }
        } else {
          toast.error(t<string>("Invalid data"));
          throw new Error("Invalid data");
        }
      },
    });
  };

  const renderMediaLibraryAddInBulkModal = () => {
    createPortal(AddMediaLibraryInBulkDialog, {
      categoryId: category.id,
      onSubmitted: loadAllMediaLibraries,
    });
  };

  return (
    <div
      key={category.id}
      ref={setNodeRef}
      className={"category-page-draggable-category"}
      id={`category-${category.id}`}
      style={style}
    >
      <div className="title-line">
        <div className="left">
          <DragHandle {...listeners} {...attributes} />
          <div className={"name"}>
            {editMode == EditMode.NameAndColor ? (
              <div className={"editing"}>
                <Input value={name} onValueChange={(v) => setName(v)} />
                &nbsp;
                <div
                  style={{
                    position: "relative",
                    width: 26,
                    height: 26,
                  }}
                >
                  <div
                    style={{
                      padding: "5px",
                      background: "#fff",
                      borderRadius: "1px",
                      boxShadow: "0 0 0 1px rgba(0,0,0,.1)",
                      display: "inline-block",
                      cursor: "pointer",
                    }}
                    onClick={() => setColorPickerVisible(true)}
                  >
                    <div
                      style={{
                        width: "16px",
                        height: "16px",
                        borderRadius: "2px",
                        background: categoryColor?.hex,
                      }}
                    />
                  </div>
                  {colorPickerVisible ? (
                    <div
                      style={{
                        position: "absolute",
                        top: "-80px",
                        left: "60px",
                        zIndex: "20",
                      }}
                    >
                      <div
                        style={{
                          position: "fixed",
                          top: "0px",
                          right: "0px",
                          bottom: "0px",
                          left: "0px",
                        }}
                        onClick={() => setColorPickerVisible(false)}
                      />
                      <SketchPicker
                        color={categoryColor}
                        onChange={(v) => {
                          setCategoryColor(v);
                        }}
                      />
                    </div>
                  ) : null}
                </div>
                <Button
                  isIconOnly
                  color={"default"}
                  size={"lg"}
                  variant={"light"}
                  className={"submit"}
                  onPress={() => {
                    BApi.category
                      .patchCategory(category.id, {
                        name,
                        color: categoryColor.hex,
                      })
                      .then((t) => {
                        if (!t.code) {
                          category.name = name;
                          category.color = categoryColor.hex;
                          clearEditMode();
                        }
                      });
                  }}
                >
                  <MdCheck className={"text-lg"} />
                </Button>
                <Button
                  isIconOnly
                  color={"default"}
                  size={"lg"}
                  variant={"light"}
                  className={"cancel"}
                  onPress={clearEditMode}
                >
                  <MdClose className={"text-lg"} />
                </Button>
              </div>
            ) : (
              <span className="editable">
                <span className="hover-area">
                  <span style={{ color: category.color }} title={category.id}>
                    {category.name}
                  </span>
                  &nbsp;
                  <Button
                    isIconOnly
                    size={"sm"}
                    onClick={() => {
                      setName(category.name);
                      setCategoryColor({
                        hex: category.color,
                      });
                      // console.log(category.color);
                      setEditMode(EditMode.NameAndColor);
                    }}
                  >
                    <EditOutlined className={"text-base"} />
                  </Button>
                </span>
              </span>
            )}
          </div>
          <Dropdown placement={"bottom-start"}>
            <DropdownTrigger>
              <Button isIconOnly size={"sm"}>
                <UnorderedListOutlined className={"text-base"} />
              </Button>
            </DropdownTrigger>
            <DropdownMenu
              aria-label={"static actions"}
              onAction={(key) => {
                switch (key) {
                  case "enhance-manually": {
                    break;
                  }
                  case "delete-enhancements": {
                    createPortal(DeleteEnhancementsModal, {
                      title: t<string>(
                        "Deleting all enhancement records of resources under this category",
                      ),
                      onOk: async (deleteEmptyOnly) => {
                        await BApi.category.deleteEnhancementsByCategory(
                          category.id,
                          { deleteEmptyOnly: deleteEmptyOnly },
                        );
                      },
                    });
                    break;
                  }
                  case "delete-category": {
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: `${t<string>("Deleting")} ${category.name}`,
                      children: (
                        <>
                          <div>
                            {t<string>(
                              "All related data will be deleted too, are you sure?",
                            )}
                          </div>
                          <div>
                            {t<string>(
                              "This operation cannot be undone. Would you like to proceed?",
                            )}
                          </div>
                        </>
                      ),
                      onOk: async () => {
                        const rsp = await BApi.category.deleteCategory(
                          category.id,
                        );

                        if (!rsp.code) {
                          loadAllCategories();
                        }
                      },
                    });
                    break;
                  }
                }
              }}
            >
              <DropdownItem
                key={"delete-enhancements"}
                className="text-danger"
                color="danger"
              >
                {t<string>("Delete all enhancement records")}
              </DropdownItem>
              <DropdownItem
                key={"delete-category"}
                className="text-danger"
                color="danger"
              >
                {t<string>("Delete category")}
              </DropdownItem>
            </DropdownMenu>
          </Dropdown>

          {resourceCount > 0 && (
            <>
              <Tooltip content={t<string>("Count of resources")}>
                <Chip
                  // size={'sm'}
                  color={"success"}
                  variant={"light"}
                >
                  <PictureOutlined className={"text-base"} />
                  &nbsp;
                  {resourceCount}
                </Chip>
              </Tooltip>
            </>
          )}
        </div>
        <div className="right flex items-center gap-2">
          <Tooltip
            color={"secondary"}
            content={t<string>("Sync all media libraries in current category")}
            placement={"left"}
          >
            <Button
              // isIconOnly
              color={"secondary"}
              size={"sm"}
              // variant={'light'}
              variant={"bordered"}
              onClick={() => {
                createPortal(SynchronizationConfirmModal, {
                  onOk: async () =>
                    await BApi.category.startSyncingCategoryResources(
                      category.id,
                    ),
                });
              }}
            >
              <SyncOutlined className={"text-base"} />
              {t<string>("Sync now")}
            </Button>
          </Tooltip>
          <Button
            color={"default"}
            size={"sm"}
            variant={"bordered"}
            onClick={() => {
              let name;

              createPortal(Modal, {
                defaultVisible: true,
                title: t<string>("Duplicating a category"),
                children: (
                  <Input
                    placeholder={t<string>(
                      "Please input a new name for the duplicated category",
                    )}
                    style={{ width: 400 }}
                    onValueChange={(v) => (name = v)}
                  />
                ),
                onOk: async () => {
                  const rsp = await BApi.category.duplicateCategory(
                    category.id,
                    { name },
                  );

                  if (!rsp.code) {
                    loadAllCategories();
                    loadAllMediaLibraries();
                  }
                },
              });
            }}
          >
            <CopyOutlined className={"text-base"} />
            {t<string>("Duplicate")}
          </Button>
        </div>
      </div>
      <div className="configuration-line block">
        {componentTypes
          .filter((t) => t.value != ComponentType.Enhancer)
          .map((type) => {
            const comp = components.find((a) => a.componentType == type.value);

            // console.log(components, comp);
            return (
              <div key={type.value} className={"flex items-center gap-1"}>
                <Tooltip content={t<string>(ComponentTips[type.value])}>
                  <Chip radius={"sm"} size={"sm"}>
                    {t<string>(type.label)}
                  </Chip>
                </Tooltip>
                <Button
                  color={comp ? "default" : "primary"}
                  size={"sm"}
                  variant={"light"}
                  onClick={() => {
                    renderBasicComponentSelector(
                      type.value,
                      comp?.componentKey,
                    );
                  }}
                >
                  {t<string>(comp?.descriptor?.name ?? "Click to set")}
                </Button>
              </div>
            );
          })}
        <div className={"flex items-center gap-1"}>
          <Chip radius={"sm"} size={"sm"}>
            {t<string>("Priority on cover selection")}
          </Chip>
          <Dropdown>
            <DropdownTrigger>
              <Button
                color={category.coverSelectionOrder > 0 ? "default" : "primary"}
                size={"sm"}
                variant={"light"}
              >
                {category.coverSelectionOrder > 0
                  ? t<string>(CoverSelectOrder[category.coverSelectionOrder])
                  : t<string>("Click to set")}
              </Button>
            </DropdownTrigger>
            <DropdownMenu
              disallowEmptySelection
              aria-label="Cover select order"
              selectedKeys={[category.coverSelectionOrder]}
              selectionMode="single"
              variant="flat"
              onSelectionChange={(keys) => {
                const arr = Array.from(keys ?? []);
                const order = arr?.[0] as CoverSelectOrder;

                console.log(keys);
                BApi.category
                  .patchCategory(category.id, { coverSelectionOrder: order })
                  .then((t) => {
                    if (!t.code) {
                      category.coverSelectionOrder = order;
                      forceUpdate();
                    }
                  });
              }}
            >
              {coverSelectOrders.map((c) => {
                return (
                  <DropdownItem key={c.value}>
                    {t<string>(CoverSelectOrder[c.value])}
                  </DropdownItem>
                );
              })}
            </DropdownMenu>
          </Dropdown>
        </div>
        <div className={"col-span-3"}>
          <div className={"flex items-center gap-2"}>
            <Tooltip content={t<string>(ComponentTips[ComponentType.Enhancer])}>
              <Chip radius={"sm"} size={"sm"}>
                {t<string>("Enhancers")}
              </Chip>
            </Tooltip>
            <div className="flex flex-wrap gap-1">
              {category.enhancerOptions?.filter((eo) => eo.active).length >
              0 ? (
                <div className="flex flex-wrap gap-1">
                  {category.enhancerOptions
                    ?.filter((eo) => eo.active)
                    .map((e, i) => {
                      const enhancer = enhancers?.find(
                        (eh) => eh.id == e.enhancerId,
                      );

                      return (
                        <Button
                          key={e.id}
                          size={"sm"}
                          variant={"flat"}
                          onClick={() => {
                            createPortal(EnhancerSelectorV2, {
                              categoryId: category.id,
                              onClose: () => reloadCategory(category.id),
                            });
                          }}
                        >
                          <EnhancerIcon id={e.enhancerId} />
                          {enhancer?.name}
                        </Button>
                      );
                    })}
                </div>
              ) : (
                <Button
                  color={"primary"}
                  size={"sm"}
                  variant={"light"}
                  onClick={() => {
                    createPortal(EnhancerSelectorV2, {
                      categoryId: category.id,
                      onClose: () => reloadCategory(category.id),
                    });
                  }}
                >
                  {t<string>("Click to set")}
                </Button>
              )}
            </div>
          </div>
        </div>
        <div className={"col-span-3"}>
          <div className={"flex flex-wrap items-center gap-2"}>
            <Tooltip
              content={t<string>(
                "Unassociated custom properties will not be displayed",
              )}
            >
              <Chip radius={"sm"} size={"sm"}>
                {t<string>("Custom properties")}
              </Chip>
            </Tooltip>
            {category.customProperties?.length > 0 ? (
              <div className="flex flex-wrap gap-1">
                {category.customProperties?.map((e, i) => {
                  // console.log(e);
                  return (
                    <Button
                      key={e.id}
                      size={"sm"}
                      variant={"flat"}
                      onClick={() => {
                        createPortal(CustomPropertyBinderModal, {
                          category: category,
                          onSaved: () => reloadCategory(category.id),
                        });
                      }}
                    >
                      <PropertyTypeIcon textVariant={"none"} type={e.type} />
                      {e.name}
                    </Button>
                  );
                })}
              </div>
            ) : (
              <Button
                className={""}
                color={"primary"}
                size={"sm"}
                variant={"light"}
                onClick={() => {
                  createPortal(CustomPropertyBinderModal, {
                    category: category,
                    onSaved: () => reloadCategory(category.id),
                  });
                }}
              >
                {t<string>("Click to set")}
              </Button>
            )}
          </div>
        </div>
        <div className={"col-span-3"}>
          <div className={"flex flex-wrap items-center gap-2"}>
            <Tooltip
              content={t<string>(
                "You can set a display name template for resources. By default, file name will be used as display name",
              )}
            >
              <Chip radius={"sm"} size={"sm"}>
                {t<string>("Display name template")}
              </Chip>
            </Tooltip>
            <Button
              color={
                category.resourceDisplayNameTemplate == undefined
                  ? "primary"
                  : "default"
              }
              size={"sm"}
              variant={"light"}
              onClick={() => {
                createPortal(DisplayNameTemplateEditorDialog, {
                  categoryId: category.id,
                  onSaved: () => reloadCategory(category.id),
                });
              }}
            >
              {category.resourceDisplayNameTemplate ??
                t<string>("Click to set")}
            </Button>
          </div>
        </div>
      </div>
      <div className="libraries-line block">
        <div className="libraries-header">
          <div className="title-line ls">
            <div className="title">{t<string>("Media libraries")}</div>
            <Tooltip
              content={t<string>(
                "Resources will not loaded automatically after modifying media libraries, " +
                  'you can click "sync button" at top-right of current page to load your resources immediately.',
              )}
            >
              <MdWarning />
            </Tooltip>
            <Dropdown placement={"bottom-start"}>
              <DropdownTrigger>
                <Button isIconOnly size={"sm"}>
                  <UnorderedListOutlined className={"text-base"} />
                </Button>
              </DropdownTrigger>
              <DropdownMenu
                aria-label={"static actions"}
                onAction={(key) => {
                  switch (key) {
                    case "add": {
                      renderMediaLibraryAddModal();
                      break;
                    }
                    case "add-in-bulk": {
                      renderMediaLibraryAddInBulkModal();
                      break;
                    }
                  }
                }}
              >
                <DropdownItem
                  // className="text-danger"
                  // color="danger"
                  key={"add"}
                >
                  {t<string>("Add a media library")}
                </DropdownItem>
                <DropdownItem
                  // className="text-danger"
                  // color="danger"
                  key={"add-in-bulk"}
                >
                  {t<string>("Add in bulk")}
                </DropdownItem>
              </DropdownMenu>
            </Dropdown>
          </div>
          <div className="path-configuration header">
            <div className="path">{t<string>("Root path")}</div>
            <div className="filter">{t<string>("Resource discovery")}</div>
            {/* <div className="tags">{t<string>('Fixed tags')}</div> */}
            <div className="tags">{t<string>("Generated properties")}</div>
          </div>
        </div>
        <div className="libraries">
          {libraries.length > 0 ? (
            <SortableMediaLibraryList
              forceUpdate={forceUpdate}
              libraries={libraries}
              loadAllMediaLibraries={loadAllMediaLibraries}
              reloadMediaLibrary={reloadMediaLibrary}
            />
          ) : (
            <div className={"flex flex-col gap-2"}>
              <div className={"text-center"}>
                {t<string>(
                  "You should set up a media library first to visit your resources",
                )}
              </div>
              <div className={"flex items-center gap-4 justify-center"}>
                <Button
                  color={"primary"}
                  size={"sm"}
                  onClick={() => {
                    renderMediaLibraryAddModal();
                  }}
                >
                  {t<string>("Set up now")}
                </Button>
                <Button
                  color={"secondary"}
                  size={"sm"}
                  onClick={() => {
                    renderMediaLibraryAddInBulkModal();
                  }}
                >
                  {t<string>("Set up in bulk")}
                </Button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

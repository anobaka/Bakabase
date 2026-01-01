"use client";

import React, { useCallback, useEffect } from "react";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useTranslation } from "react-i18next";
import {
  DeleteOutlined,
  FolderOpenOutlined,
  PictureOutlined,
  PlusCircleOutlined,
  QuestionCircleOutlined,
  SyncOutlined,
  UnorderedListOutlined,
} from "@ant-design/icons";
import { useUpdate } from "react-use";
import { AutoTextSize } from "auto-text-size";
import { MdDelete, MdSearch, MdPlaylistAdd } from "react-icons/md";

import { DropdownTrigger, toast } from "@/components/bakaui";
import DragHandle from "@/components/DragHandle";
import { ResourceMatcherValueType, ResourceProperty } from "@/sdk/constants";
import { buildLogger } from "@/components/utils";
import BApi from "@/sdk/BApi";
import PathConfigurationDialog from "@/pages/category/components/PathConfigurationDialog";
import AddRootPathsInBulkDialog from "@/pages/category/components/AddRootPathsInBulkDialog";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import {
  Button,
  Chip,
  Input,
  Modal,
  Tooltip,
  Dropdown,
  DropdownMenu,
  DropdownItem,
} from "@/components/bakaui";
import SynchronizationConfirmModal from "@/pages/category/components/SynchronizationConfirmModal";
import DeleteEnhancementsModal from "@/pages/category/components/DeleteEnhancementsModal";
const SortableMediaLibrary = ({ library, loadAllMediaLibraries, reloadMediaLibrary }) => {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({
    id: library.id,
  });

  const log = buildLogger("SortableMediaLibrary");

  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const style = {
    transform: CSS.Translate.toString({
      ...transform!,
      scaleY: 1,
    }),
    transition,
  };

  const forceUpdate = useUpdate();

  useEffect(() => {}, []);

  // useTraceUpdate({
  //   library,
  //   loadAllMediaLibraries,
  //   regexInputVisible,
  //   testResult,
  //   checkingPathRelations,
  //   relativeLibraries,
  //   pathConfiguration
  // }, 'MediaLibrary')

  const renderFilter = useCallback((pc: any) => {
    const resourceValue = pc.rpmValues?.find(
      (r) => !r.isCustomProperty && r.propertyId == ResourceProperty.Resource,
    );
    let valueComponent: any;

    if (resourceValue) {
      switch (resourceValue.valueType) {
        case ResourceMatcherValueType.Layer:
          valueComponent = (
            <div>
              {t<string>(
                resourceValue.layer > 0
                  ? "The {{layer}} layer after root path"
                  : "The {{layer}} layer to the resource",
                { layer: Math.abs(resourceValue.layer) },
              )}
            </div>
          );
          break;
        case ResourceMatcherValueType.Regex:
          valueComponent = <div>{resourceValue.regex}</div>;
          break;
        case ResourceMatcherValueType.FixedText:
          valueComponent = <div>{resourceValue.fixedText}</div>;
          break;
      }
    }
    if (valueComponent) {
      return (
        <div className={"filter"}>
          <Chip radius={"sm"} size={"sm"}>
            {t<string>(ResourceMatcherValueType[resourceValue.valueType])}
          </Chip>
          {valueComponent}
        </div>
      );
    }

    return <div className={"unset filter"}>{t<string>("Not set")}</div>;
  }, []);

  const renderCustomProperties = useCallback((p) => {
    let properties =
      p.rpmValues?.filter((x) => !x.isResourceProperty).map((x) => x.propertyName) ?? [];

    properties = properties.filter((p, i) => properties.indexOf(p) === i);

    return properties.length > 0
      ? properties.map((n) => {
          return (
            <Chip radius={"sm"} size={"sm"}>
              {n ?? t<string>("Unknown property")}
            </Chip>
          );
        })
      : t<string>("Not set");
  }, []);

  const renderAddRootPathInBulkModal = () => {
    createPortal(AddRootPathsInBulkDialog, {
      libraryId: library.id,
      onSubmitted: () => loadAllMediaLibraries(),
    });
  };

  const renderAddRootPathModal = () => {
    createPortal(Modal, {
      defaultVisible: true,
      size: "lg",
      title: t<string>("Add root path"),
      children: (
        <Input
          isRequired
          label={t<string>("Path")}
          onValueChange={(v) => {
            if (v) {
              BApi.mediaLibrary
                .addMediaLibraryPathConfiguration(library.id, {
                  path: v,
                })
                .then((b) => {
                  if (!b.code) {
                    loadAllMediaLibraries();
                  }
                });
            }
          }}
        />
      ),
    });
  };

  return (
    <div
      ref={setNodeRef}
      className={"category-page-draggable-media-library libraries-grid"}
      style={style}
    >
      <div className="library">
        <DragHandle {...listeners} {...attributes} />
        <div className="flex items-center gap-1">
          <div
            className={"edit relative flex items-center gap-1"}
            onClick={() => {
              let n = library.name;

              createPortal(Modal, {
                defaultVisible: true,
                size: "lg",
                title: t<string>("Change name"),
                children: (
                  <Input
                    defaultValue={n}
                    style={{ width: "100%" }}
                    onValueChange={(v) => {
                      n = v;
                    }}
                  />
                ),
                onOk: () => {
                  return new Promise((resolve, reject) => {
                    if (n?.length > 0) {
                      BApi.mediaLibrary
                        .patchMediaLibrary(library.id, {
                          name: n,
                        })
                        .then((t) => {
                          if (!t.code) {
                            resolve(t);
                            library.name = n;
                            forceUpdate();
                          } else {
                            reject();
                          }
                        });
                    } else {
                      toast.danger(t<string>("Invalid data"));
                    }
                  });
                },
              });
            }}
          >
            <AutoTextSize className={"cursor-pointer"} maxFontSizePx={14}>
              {library.name}
            </AutoTextSize>
            {library.resourceCount > 0 ? (
              <Tooltip content={t<string>("Count of resources")}>
                <Chip className={"p-0"} color={"success"} size={"sm"} variant={"light"}>
                  <PictureOutlined className={"text-sm"} />
                  &nbsp;
                  {library.resourceCount}
                </Chip>
              </Tooltip>
            ) : (
              library.pathConfigurations?.length > 0 && (
                <Tooltip
                  content={t<string>(
                    "Resource not found? Please try to perform the synchronization operation.",
                  )}
                >
                  <QuestionCircleOutlined className={"text-base"} />
                </Tooltip>
              )
            )}
          </div>
          <div className={"pl-2"}>
            <Tooltip
              color={"secondary"}
              content={t<string>("Sync current media library")}
              placement={"top"}
            >
              <Button
                isIconOnly
                className={"w-auto min-w-fit px-1"}
                color={"secondary"}
                size={"sm"}
                variant={"light"}
                onClick={() => {
                  createPortal(SynchronizationConfirmModal, {
                    onOk: async () =>
                      await BApi.mediaLibrary.startSyncingMediaLibraryResources(library.id),
                  });
                }}
              >
                <SyncOutlined className={"text-base"} />
              </Button>
            </Tooltip>
            <Dropdown>
              <DropdownTrigger>
                <Button
                  isIconOnly
                  className={"w-auto min-w-fit px-1"}
                  size={"sm"}
                  variant={"light"}
                >
                  <PlusCircleOutlined
                    className={"text-base"}
                    onClick={() => {
                      renderAddRootPathModal();
                    }}
                  />
                </Button>
              </DropdownTrigger>
              <DropdownMenu>
                <DropdownItem
                  key={0}
                  onClick={() => {
                    renderAddRootPathInBulkModal();
                  }}
                >
                  <MdPlaylistAdd className={"text-base"} />
                  {t<string>("Add root paths in bulk")}
                </DropdownItem>
              </DropdownMenu>
            </Dropdown>
            <Dropdown className={"category-page-media-library-more-operations-popup"}>
              <DropdownTrigger>
                <Button
                  isIconOnly
                  className={"w-auto min-w-fit px-1"}
                  size={"sm"}
                  variant={"light"}
                >
                  <UnorderedListOutlined className={"text-base"} />
                </Button>
              </DropdownTrigger>
              <DropdownMenu>
                <DropdownItem
                  key={0}
                  className={"warning"}
                  onClick={() => {
                    createPortal(DeleteEnhancementsModal, {
                      title: t<string>(
                        "Deleting all enhancement records of resources under this media library",
                      ),
                      onOk: async (deleteEmptyOnly) => {
                        await BApi.mediaLibrary.deleteByEnhancementsMediaLibrary(library.id, {
                          deleteEmptyOnly: deleteEmptyOnly,
                        });
                      },
                    });
                  }}
                >
                  <MdSearch className={"text-base"} />
                  {t<string>("Delete all enhancement records")}
                </DropdownItem>
                <DropdownItem
                  key={1}
                  className={"warning"}
                  onClick={() => {
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: `${t<string>("Deleting")} ${library.name}`,
                      children: t<string>("Are you sure you want to delete this media library?"),
                      onOk: () =>
                        new Promise((resolve, reject) => {
                          BApi.mediaLibrary.deleteMediaLibrary(library.id).then((a) => {
                            if (!a.code) {
                              loadAllMediaLibraries();
                              resolve(a);
                            }
                          });
                        }),
                    });
                  }}
                >
                  <MdDelete className={"text-base"} />
                  {t<string>("Remove")}
                </DropdownItem>
              </DropdownMenu>
            </Dropdown>
          </div>
        </div>
      </div>
      <div className="path-configurations">
        {library.pathConfigurations?.length > 0 ? (
          library.pathConfigurations?.map((p, i) => {
            return (
              <div
                key={i}
                className={"path-configuration item"}
                onClick={() => {
                  createPortal(PathConfigurationDialog, {
                    onClosed: (pc) => {
                      Object.assign(library.pathConfigurations[i], pc);
                      loadAllMediaLibraries();
                    },
                    libraryId: library.id,
                    pcIdx: i,
                  });
                }}
              >
                <div className="flex items-center">
                  <span>{p.path}</span>
                  <Chip color={"success"} radius={"sm"} size={"sm"} variant={"light"}>
                    {library.fileSystemInformation?.[p.path]?.freeSpaceInGb}GB
                  </Chip>
                  <div className={"flex items-center"}>
                    {/*<Button*/}
                    {/*  isIconOnly*/}
                    {/*  className={"w-auto min-w-fit px-1"}*/}
                    {/*  color={"primary"}*/}
                    {/*  size={"sm"}*/}
                    {/*  variant={"light"}*/}
                    {/*  onPress={(e) => {*/}
                    {/*    let templateName = "";*/}

                    {/*    createPortal(Modal, {*/}
                    {/*      defaultVisible: true,*/}
                    {/*      size: "lg",*/}
                    {/*      title: t<string>(*/}
                    {/*        "Exporting path configuration as new media library template",*/}
                    {/*      ),*/}
                    {/*      children: (*/}
                    {/*        <div className={"flex flex-col gap-1"}>*/}
                    {/*          <Alert*/}
                    {/*            color={"default"}*/}
                    {/*            description={*/}
                    {/*              <div>*/}
                    {/*                <div>*/}
                    {/*                  {t<string>(*/}
                    {/*                    "The resource selection rules, attribute value settings, enhancer configurations, playable file locator, " +*/}
                    {/*                      "and resource naming conventions of the current path will all be exported to the media library template.",*/}
                    {/*                  )}*/}
                    {/*                </div>*/}
                    {/*                <div>*/}
                    {/*                  {t<string>(*/}
                    {/*                    "However, the player will not be included. Please configure the player manually in the new media library.",*/}
                    {/*                  )}*/}
                    {/*                </div>*/}
                    {/*                <div>*/}
                    {/*                  {t<string>(*/}
                    {/*                    "A media library template is a combination of the rules below and can be reused across multiple paths.",*/}
                    {/*                  )}*/}
                    {/*                </div>*/}
                    {/*                <div>*/}
                    {/*                  {t<string>(*/}
                    {/*                    "It is not necessary to create a separate template for each path.",*/}
                    {/*                  )}*/}
                    {/*                </div>*/}
                    {/*              </div>*/}
                    {/*            }*/}
                    {/*            title={t<string>(*/}
                    {/*              "The old categorization and media library features will soon be removed. Please export the data as a new media library template.",*/}
                    {/*            )}*/}
                    {/*          />*/}
                    {/*          <Input*/}
                    {/*            isRequired*/}
                    {/*            label={t<string>("Template name")}*/}
                    {/*            onValueChange={(v) => (templateName = v)}*/}
                    {/*          />*/}
                    {/*        </div>*/}
                    {/*      ),*/}
                    {/*      onOk: async () => {*/}
                    {/*        if (*/}
                    {/*          templateName == undefined ||*/}
                    {/*          templateName.length == 0*/}
                    {/*        ) {*/}
                    {/*          const msg = t<string>(*/}
                    {/*            "Template name is required",*/}
                    {/*          );*/}

                    {/*          toast.error(msg);*/}
                    {/*          throw new Error(msg);*/}
                    {/*        }*/}
                    {/*        const r =*/}
                    {/*          await BApi.mediaLibraryTemplate.addMediaLibraryTemplateByMediaLibraryV1(*/}
                    {/*            {*/}
                    {/*              v1Id: library.id,*/}
                    {/*              pcIdx: i,*/}
                    {/*              name: templateName,*/}
                    {/*            },*/}
                    {/*          );*/}

                    {/*        if (!r.code) {*/}
                    {/*          toast.success(*/}
                    {/*            t<string>(*/}
                    {/*              "Media library template created successfully",*/}
                    {/*            ),*/}
                    {/*          );*/}
                    {/*        }*/}
                    {/*      },*/}
                    {/*    });*/}
                    {/*  }}*/}
                    {/*>*/}
                    {/*  <TbPackageExport className={"text-base"} />*/}
                    {/*</Button>*/}
                    <Button
                      isIconOnly
                      className={"w-auto min-w-fit px-1"}
                      size={"sm"}
                      variant={"light"}
                    >
                      <FolderOpenOutlined
                        className={"text-base"}
                        onClick={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                          BApi.tool.openFileOrDirectory({ path: p.path });
                        }}
                      />
                    </Button>
                    <Button
                      isIconOnly
                      className={"w-auto min-w-fit px-1"}
                      color={"danger"}
                      size={"sm"}
                      variant={"light"}
                    >
                      <DeleteOutlined
                        className={"text-base"}
                        onClick={(e) => {
                          createPortal(Modal, {
                            defaultVisible: true,
                            title: `${t<string>("Deleting")} ${p.path}`,
                            onOk: async () => {
                              const rsp =
                                await BApi.mediaLibrary.removeMediaLibraryPathConfiguration(
                                  library.id,
                                  {
                                    index: i,
                                  },
                                );

                              if (rsp.code) {
                                throw new Error(rsp.message!);
                              } else {
                                loadAllMediaLibraries();
                              }
                            },
                          });
                        }}
                      />
                    </Button>
                  </div>
                </div>
                {renderFilter(p)}
                <div className="flex flex-wrap gap-1">{renderCustomProperties(p)}</div>
              </div>
            );
          })
        ) : (
          <div className={"flex flex-col gap-2"}>
            <div className={"text-center"}>
              {t<string>(
                "To get your resources loaded, you must add at least one root path containing your local resources to this media library",
              )}
            </div>
            <div className={"flex items-center gap-4 justify-center"}>
              <Button
                color={"primary"}
                size={"sm"}
                onClick={() => {
                  renderAddRootPathModal();
                }}
              >
                {t<string>("Add root path")}
              </Button>
              <Button
                color={"secondary"}
                size={"sm"}
                onClick={() => {
                  renderAddRootPathInBulkModal();
                }}
              >
                {t<string>("Add root paths in bulk")}
              </Button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

SortableMediaLibrary.displayName = "SortableMediaLibrary";

export default SortableMediaLibrary;

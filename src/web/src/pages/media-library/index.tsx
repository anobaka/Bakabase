"use client";

import type { Key } from "@react-types/shared";
import type { MediaLibraryTemplatePage } from "../media-library-template/models";
import type { MediaLibrary, MediaLibraryPlayer } from "./models";
import type { InputProps } from "@heroui/react";
import type { ButtonProps, SelectProps } from "@/components/bakaui";

import { useTranslation } from "react-i18next";
import { useEffect, useRef, useState } from "react";
import { useUpdate, useUpdateEffect } from "react-use";
import { FaSort } from "react-icons/fa";
import {
  AiOutlineDelete,
  AiOutlineFolderOpen,
  AiOutlineImport,
  AiOutlinePlusCircle,
  AiOutlineProduct,
  AiOutlineSearch,
} from "react-icons/ai";
import { MdOutlineDelete } from "react-icons/md";
import { IoIosSync } from "react-icons/io";
import { AiOutlineFileUnknown } from "react-icons/ai";
import { TbTemplate } from "react-icons/tb";
import { PiEmpty } from "react-icons/pi";
import { BsController } from "react-icons/bs";
import { useNavigate } from "react-router-dom";
import { CiCircleMore } from "react-icons/ci";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskStatus } from "@/sdk/constants";
import envConfig from "@/config/env";

import SyncStatus from "./components/SyncStatus";
import PlayerSelectorModal from "./components/PlayerSelectorModal";
import OutdatedModal from "./components/OutdatedModal";

import {
  Button,
  Chip,
  Input,
  Modal,
  Select,
  Tooltip,
  Switch,
  ColorPicker,
  Table,
  TableHeader,
  TableBody,
  TableColumn,
  TableRow,
  TableCell,
  Dropdown,
  DropdownTrigger,
  DropdownMenu,
  DropdownItem,
  toast,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import { useResourceOptionsStore } from "@/stores/options";
import { splitPathIntoSegments } from "@/components/utils";
import PresetTemplateBuilder from "@/pages/media-library-template/components/PresetTemplateBuilder";
import TemplateModal from "@/pages/media-library-template/components/TemplateModal";
import { InternalProperty, PropertyPool, SearchOperation } from "@/sdk/constants";
import { buildColorValueString } from "@/components/bakaui/components/ColorPicker";
import DeleteEnhancementsByEnhancerSelectorModal from "@/pages/media-library/components/DeleteEnhancementsByEnhancerSelectorModal";
import { EditableValue } from "@/components/EditableValue";
import PathAutocomplete, { PathAutocompleteProps } from "@/components/PathAutocomplete";
import { BakabaseServiceModelsViewUnknownResourcesCountViewModel } from "@/sdk/Api";
import HandleUnknownResourcesModal from "@/components/HandleUnknownResourcesModal";
import BetaChip from "@/components/Chips/BetaChip";
import MediaLibrarySelectorV2 from "@/components/MediaLibrarySelectorV2";

enum SortBy {
  Path = 1,
  Template = 2,
}

type UnknownResourceCount = BakabaseServiceModelsViewUnknownResourcesCountViewModel;

const MediaLibraryPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const navigate = useNavigate();

  const [mediaLibraries, setMediaLibraries] = useState<MediaLibrary[]>([]);
  const [templates, setTemplates] = useState<MediaLibraryTemplatePage[]>([]);
  const templatesRef = useRef<Record<number, MediaLibraryTemplatePage>>({});
  const [sortBy, setSortBy] = useState<SortBy>(SortBy.Path);
  const forceUpdate = useUpdate();
  const outdatedModalRef = useRef<{ check: () => Promise<void> } | null>(null);
  const [unknownResourceCounts, setUnknownResourceCounts] = useState<UnknownResourceCount>();
  const resourceOptionsStore = useResourceOptionsStore();
  const resourceOptions = resourceOptionsStore.data;

  const loadMediaLibraries = async (): Promise<MediaLibrary[]> => {
    const r = await BApi.mediaLibraryV2.getAllMediaLibraryV2();
    const list = r.data ?? [];

    setMediaLibraries(list);
    const unknown = await BApi.resource.getUnknownResourcesCount();
    setUnknownResourceCounts(unknown.data);

    return list;
  };

  const loadTemplates = async () => {
    const r = await BApi.mediaLibraryTemplate.getAllMediaLibraryTemplates();
    const tpls = r.data ?? [];

    // @ts-ignore
    setTemplates(tpls);
    templatesRef.current = tpls.reduce<Record<number, MediaLibraryTemplatePage>>((s, t) => {
      s[t.id] = t as unknown as MediaLibraryTemplatePage;

      return s;
    }, {});
  };

  useEffect(() => {
    Promise.all([loadTemplates(), loadMediaLibraries()]).then(() => {
      outdatedModalRef.current?.check();
    });
  }, []);

  useUpdateEffect(() => {
    switch (sortBy) {
      case SortBy.Path:
        mediaLibraries.sort((a, b) => (a.paths[0] ?? "").localeCompare(b.paths[0] ?? ""));
        break;
      case SortBy.Template:
        mediaLibraries.sort((a, b) =>
          (templatesRef.current[a.templateId ?? 0]?.name ?? "").localeCompare(
            templatesRef.current[b.templateId ?? 0]?.name ?? "",
          ),
        );
        break;
    }
    forceUpdate();
  }, [sortBy, mediaLibraries]);

  const renderPath = (ml: MediaLibrary) => {
    const paths = ml.paths ?? [];

    if (paths.length == 0) {
      return (
        <Button
          isIconOnly
          color="primary"
          size="sm"
          variant="light"
          onPress={() => {
            ml.paths.push("");
            forceUpdate();
          }}
        >
          <AiOutlinePlusCircle className={"text-lg"} />
        </Button>
      );
    }

    return (
      <div className={"flex flex-col gap-1"}>
        {paths.map((p, idx) => {
          return (
            <div key={p} className="flex items-center gap-1">
              <EditableValue<string, PathAutocompleteProps, ButtonProps & { value: string }>
                className="grow"
                Viewer={({ value, ...props }) => value ? <Button
                  key={idx}
                  className={"justify-start"}
                  color={"default"}
                  size={"sm"}
                  variant={"flat"}
                  onPress={() => BApi.tool.openFileOrDirectory({ path: p })}
                >
                  <AiOutlineFolderOpen className={"text-lg"} />
                  {value}
                </Button> : null}
                Editor={({ onValueChange, ...props }) => <PathAutocomplete
                  maxResults={100}
                  label={
                    paths.length > 1
                      ? `${t<string>("MediaLibrary.Path")}${idx + 1}`
                      : t<string>("MediaLibrary.Path")
                  }
                  pathType="folder"
                  placeholder={t<string>("MediaLibrary.PathPlaceholder")}
                  size="sm"
                  onSelectionChange={onValueChange}
                  {...props}
                />
                }
                onSubmit={async v => {
                  paths[idx] = v ?? "";
                  ml.paths = paths;
                  await BApi.mediaLibraryV2.patchMediaLibraryV2(ml.id, {
                    paths: paths,
                  });
                  outdatedModalRef.current?.check();
                  forceUpdate();
                }}
                defaultValue={p}
              />
              <Button
                isIconOnly
                color="primary"
                size="sm"
                variant="light"
                onPress={() => {
                  ml.paths.push("");
                  forceUpdate();
                }}
              >
                <AiOutlinePlusCircle className={"text-lg"} />
              </Button>
              <Button
                isIconOnly
                color="danger"
                size="sm"
                variant="light"
                onPress={() => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t("Remove folder from media library?"),
                    children: t("This operation will not affect the files in the directory."),
                    onOk: async () => {
                      const newPaths = ml.paths?.filter((x) => x != p);
                      const rsp = await BApi.mediaLibraryV2.patchMediaLibraryV2(ml.id, {
                        paths: newPaths,
                      });

                      if (!rsp.code) {
                        ml.paths = newPaths;
                        forceUpdate();
                      }
                    },
                  });
                }}
              >
                <AiOutlineDelete className={"text-lg"} />
              </Button>
            </div>
          );
        })}
      </div>
    );
  };

  const mediaLibraryUnknownResourcesCounts = unknownResourceCounts?.unknownPathCountByMediaLibraryId ?? {};

  const renderTable = () => {
    if (mediaLibraries.length === 0) {
      return (
        <div className={"flex items-center gap-2 grow justify-center"}>
          <PiEmpty className={"text-2xl"} />
          {t<string>("MediaLibrary.NoMediaLibrariesFound")}
        </div>
      );
    }

    return (
      <Table removeWrapper className="mt-2 overflow-x-auto">
        <TableHeader>
          <TableColumn>{t<string>("MediaLibrary.Name")}</TableColumn>
          <TableColumn>{t<string>("MediaLibrary.Path")}</TableColumn>
          <TableColumn>{t<string>("MediaLibrary.Template")}</TableColumn>
          <TableColumn>{t<string>("MediaLibrary.Players")}</TableColumn>
          <TableColumn width="200">{t<string>("Operations")}</TableColumn>
        </TableHeader>
        <TableBody>
          {mediaLibraries.map((row, i) => {
            const ml = row as MediaLibrary;
            const template = templates.find((t) => t.id == ml.templateId);
            const getCssVariable = (variableName: string) => {
              if (typeof document === "undefined") return "";

              return getComputedStyle(document.documentElement).getPropertyValue(variableName);
            };
            const textColor = getCssVariable("--theme-text") || "#222";

            return (
              <TableRow key={ml.id}>
                {/* Name */}
                <TableCell>
                  <div className="flex items-center gap-1">
                    <EditableValue<string, InputProps, ButtonProps & { value: string }>
                      Editor={Input}
                      Viewer={({ value, ...props }) => (
                        <Button
                          size="sm"
                          style={{ color: ml.color ?? textColor }}
                          variant="light"
                          className="whitespace-break-spaces h-auto text-left"
                          {...props}
                        >
                          {value}
                        </Button>
                      )}
                      editorProps={{
                        size: "sm",
                        isRequired: true,
                      }}
                      trigger="viewer"
                      value={ml.name}
                      onSubmit={async (v) => {
                        if (v == undefined || v.length == 0) {
                          toast.danger(t<string>("Name cannot be empty"));

                          return;
                        }
                        const rsp = await BApi.mediaLibraryV2.patchMediaLibraryV2(ml.id, {
                          name: v,
                        });

                        if (!rsp.code) {
                          ml.name = v;
                          forceUpdate();
                        }
                      }}
                    />
                    <ColorPicker
                      color={ml.color ?? textColor}
                      onChange={async (color) => {
                        const strColor = buildColorValueString(color);

                        await BApi.mediaLibraryV2.putMediaLibraryV2(ml.id, {
                          ...ml,
                          color: strColor,
                        });
                        ml.color = strColor;
                        forceUpdate();
                      }}
                    />
                  </div>
                </TableCell>
                {/* Paths */}
                <TableCell>{renderPath(ml)}</TableCell>
                {/* Template */}
                <TableCell>
                  <EditableValue<
                    number,
                    Omit<SelectProps, "value"> & {
                      onValueChange?: (value: number) => void;
                      value: number;
                    },
                    ButtonProps & { value: number }
                  >
                    Editor={({ onValueChange, value, ...props }) => (
                      <Select
                        isRequired
                        dataSource={templates
                          .map(
                            (t) =>
                              ({
                                textValue: `#${t.id} ${t.name}`,
                                label: `#${t.id} ${t.name}`,
                                value: t.id,
                              }) as {
                                label?: any;
                                value: Key;
                                textValue?: string;
                                isDisabled?: boolean;
                              },
                          )
                          .concat([
                            {
                              label: (
                                <div className={"flex items-center gap-1"}>
                                  <AiOutlineImport className={"text-lg"} />
                                  {t<string>("MediaLibrary.CreateNewTemplate")}
                                </div>
                              ),
                              value: -1,
                              textValue: t<string>("MediaLibrary.CreateNewTemplate"),
                            },
                          ])}
                        isInvalid={value == undefined || value <= 0}
                        label={t<string>("MediaLibrary.Template")}
                        placeholder={t<string>("MediaLibrary.TemplatePlaceholder")}
                        selectedKeys={value ? [value.toString()] : undefined}
                        size={"sm"}
                        onSelectionChange={(keys) => {
                          const arr = Array.from(keys);

                          if (arr.length > 0) {
                            const idStr = arr[0] as string;
                            const value = parseInt(idStr, 10);

                            if (value == -1) {
                              createPortal(PresetTemplateBuilder, {
                                onSubmitted: async (id) => {
                                  onValueChange?.(id);
                                  await loadTemplates();
                                },
                              });
                            } else {
                              onValueChange?.(value);
                              forceUpdate();
                            }
                          }
                        }}
                      />
                    )}
                    Viewer={({ value }) =>
                      value ? (
                        <Button
                          className={"text-left"}
                          isDisabled={!template}
                          radius={"sm"}
                          size={"sm"}
                          startContent={<TbTemplate className={"text-base"} />}
                          variant={"light"}
                          onPress={() => {
                            if (ml.templateId) {
                              createPortal(TemplateModal, {
                                id: ml.templateId,
                                onDestroyed: loadTemplates,
                              });
                            }
                          }}
                        >
                          {template?.name ?? t<string>("MediaLibrary.Unknown")}
                        </Button>
                      ) : (
                        <Chip size="sm" variant="light" color="danger">
                          {t<string>("Media library template must be set")}
                        </Chip>
                      )
                    }
                    className="grow"
                    editorProps={
                      {
                        // size: "sm",
                      }
                    }
                    value={ml.templateId}
                    onSubmit={async (v) => {
                      if (v == undefined || v <= 0) {
                        toast.danger(t<string>("Template cannot be empty"));

                        return;
                      }

                      if (ml.templateId != v) {
                        ml.templateId = v;
                        const rsp = await BApi.mediaLibraryV2.patchMediaLibraryV2(ml.id, {
                          templateId: v,
                        });

                        if (!rsp.code) {
                          outdatedModalRef.current?.check();
                          forceUpdate();
                        }
                      }
                    }}
                  />
                </TableCell>
                {/* Players */}
                <TableCell>
                  <div className="flex items-center gap-1">
                    <Tooltip content={t("Setup custom players")}>
                      <Button
                        isIconOnly
                        radius={"sm"}
                        size={"sm"}
                        variant={"light"}
                        onPress={() => {
                          createPortal(PlayerSelectorModal, {
                            players: ml.players,
                            onSubmit: async (players: MediaLibraryPlayer[]) => {
                              await BApi.mediaLibraryV2.putMediaLibraryV2(ml.id, {
                                ...ml,
                                players,
                              });
                              await loadMediaLibraries();
                            },
                          });
                        }}
                      >
                        <BsController className={"text-lg"} />
                      </Button>
                    </Tooltip>
                    {ml.players && ml.players.length > 0 && (
                      <div className="flex flex-col gap-1">
                        {ml.players.map((player, index) => {
                          const executablePathSegments = splitPathIntoSegments(
                            player.executablePath,
                          );

                          return (
                            <div key={index} className={"flex items-center gap-2 text-xs"}>
                              <div className={"flex flex-col gap-1"}>
                                <div className={"font-medium truncate"}>
                                  {executablePathSegments[executablePathSegments.length - 1]}
                                </div>
                                <div className={"text-gray-500"}>
                                  {t<string>("Command")}: {player.command}
                                </div>
                              </div>
                              <div className={"flex flex-wrap gap-1"}>
                                {player.extensions?.map((ext) => (
                                  <Chip key={ext} size={"sm"} variant={"flat"}>
                                    {ext}
                                  </Chip>
                                ))}
                                {player.extensions && player.extensions.length > 3 && (
                                  <Chip color={"secondary"} size={"sm"} variant={"flat"}>
                                    +{player.extensions.length - 3}
                                  </Chip>
                                )}
                              </div>
                            </div>
                          );
                        })}
                      </div>
                    )}
                  </div>
                </TableCell>
                {/* Actions */}
                <TableCell>
                  <div className="flex items-center gap-1">
                    <Tooltip content={ml.resourceCount > 0 ? t<string>("{{count}} resource(s) loaded in this media library.", {
                      count: ml.resourceCount,
                    }) : t<string>("No resource loaded in this media library, you can click the synchronize button to load resources.")} placement="top">
                      <Chip
                        color={"success"}
                        size={"sm"}
                        startContent={<AiOutlineProduct className={"text-lg"} />}
                        variant={"light"}
                      >
                        {ml.resourceCount}
                      </Chip>
                    </Tooltip>
                    {/* Unknown resources of type b (PathDoesNotExist) */}
                    {mediaLibraryUnknownResourcesCounts[ml.id] > 0 && (
                      <Tooltip content={t<string>("Handle {{count}} unknown resources in this media library", { count: mediaLibraryUnknownResourcesCounts[ml.id] })} placement="top">
                        <Button
                          isIconOnly
                          radius={"sm"}
                          size={"sm"}
                          color="warning"
                          variant={"light"}
                          onPress={async () => {
                            createPortal(HandleUnknownResourcesModal, {
                              mediaLibraryId: ml.id,
                              onHandled: () => loadMediaLibraries(),
                            });
                          }}
                        >
                          <AiOutlineFileUnknown className={"text-lg"} />
                        </Button>
                      </Tooltip>
                    )}
                    {ml.resourceCount > 0 && (
                      <Tooltip content={t<string>("MediaLibrary.SearchResources")} placement="top">
                        <Button
                          isIconOnly
                          className={""}
                          radius={"sm"}
                          size={"sm"}
                          variant={"light"}
                          onPress={() => {
                            createPortal(Modal, {
                              title: t<string>("MediaLibrary.Confirm"),
                              children: t<string>("MediaLibrary.LeavePageConfirm"),
                              defaultVisible: true,
                              onOk: async () => {
                                const valuePropertyResponse =
                                  await BApi.resource.getFilterValueProperty({
                                    propertyPool: PropertyPool.Internal,
                                    propertyId: InternalProperty.MediaLibraryV2,
                                    operation: SearchOperation.Equals,
                                  });
                                const searchForm = {
                                  group: {
                                    combinator: 1,
                                    disabled: false,
                                    filters: [
                                      {
                                        propertyPool: PropertyPool.Internal,
                                        propertyId: InternalProperty.MediaLibraryV2,
                                        operation: SearchOperation.Equals,
                                        dbValue: ml.id.toString(),
                                        bizValue: ml.name,
                                        valueProperty: valuePropertyResponse.data,
                                        disabled: false,
                                      },
                                    ],
                                  },
                                  page: 1,
                                  pageSize: 100,
                                };
                                const query = encodeURIComponent(JSON.stringify(searchForm));

                                navigate(`/resource2?query=${query}`);
                              },
                              footer: {
                                actions: ["ok", "cancel"],
                                okProps: {
                                  children: t<string>("MediaLibrary.Continue"),
                                },
                                cancelProps: {
                                  children: t<string>("MediaLibrary.Cancel"),
                                },
                              },
                            });
                          }}
                        >
                          <AiOutlineSearch className={"text-lg"} />
                        </Button>
                      </Tooltip>
                    )}
                    <SyncStatus
                      id={ml.id}
                      onSyncCompleted={() => {
                        loadMediaLibraries();
                      }}
                    />
                    <Dropdown>
                      <DropdownTrigger>
                        <Button isIconOnly radius={"sm"} size={"sm"} variant={"light"}>
                          <CiCircleMore className={"text-lg"} />
                        </Button>
                      </DropdownTrigger>
                      <DropdownMenu
                        aria-label="More operations"
                        selectionMode="single"
                        onSelectionChange={(keys) => {
                          const key = Array.from(keys ?? [])[0] as string;

                          if (key === "deleteEnhancements") {
                            createPortal(DeleteEnhancementsByEnhancerSelectorModal, {
                              mediaLibraryId: ml.id,
                              mediaLibraryName: ml.name,
                              template: template,
                              onCompleted: async () => {
                                toast.success(t<string>("Saved"));
                                outdatedModalRef.current?.check();
                              },
                            });
                          } else if (key === "moveResourcesToOtherMediaLibrary") {
                            createPortal(MediaLibrarySelectorV2, {
                              confirmation: true,
                              onSelect: async (toId: number, isLegacy: boolean) => {
                                if (isLegacy) {
                                  toast.danger(t<string>("Only media library v2 is supported"));
                                  return;
                                }
                                if (toId === ml.id) {
                                  toast.warning(t<string>("Cannot move resources to the same media library"));
                                  return;
                                }

                                try {
                                  const valuePropertyResponse = await BApi.resource.getFilterValueProperty({
                                    propertyPool: PropertyPool.Internal,
                                    propertyId: InternalProperty.MediaLibraryV2,
                                    operation: SearchOperation.Equals,
                                  });

                                  const searchForm = {
                                    group: {
                                      combinator: 1,
                                      disabled: false,
                                      filters: [
                                        {
                                          propertyPool: PropertyPool.Internal,
                                          propertyId: InternalProperty.MediaLibraryV2,
                                          operation: SearchOperation.Equals,
                                          dbValue: ml.id.toString(),
                                          bizValue: ml.name,
                                          valueProperty: valuePropertyResponse.data,
                                          disabled: false,
                                        },
                                      ],
                                    },
                                    page: 1,
                                    pageSize: 1000000,
                                  } as const;

                                  const idsResp = await BApi.resource.searchAllResourceIds(searchForm as any);
                                  const ids = idsResp.data || [];

                                  if (ids.length > 0) {
                                    await BApi.resource.moveResources({
                                      ids,
                                      mediaLibraryId: toId,
                                      isLegacyMediaLibrary: false,
                                    } as any);
                                  }

                                  // Ask whether to delete the source media library after move
                                  createPortal(Modal, {
                                    defaultVisible: true,
                                    title: t<string>("MediaLibrary.Confirm"),
                                    children: t<string>("Move resources completed. Delete the source media library?"),
                                    onOk: async () => {
                                      await BApi.mediaLibraryV2.deleteMediaLibraryV2(ml.id);
                                      await loadMediaLibraries();
                                    },
                                    onClose: async () => {
                                      await loadMediaLibraries();
                                    },
                                    footer: {
                                      actions: ["ok", "cancel"],
                                      okProps: { children: t<string>("Delete"), color: "danger" },
                                      cancelProps: { children: t<string>("Keep it"), color: "default" },
                                    },
                                  });
                                } catch (e) {
                                  toast.danger(t<string>("Operation failed"));
                                }
                              },
                            });
                          } else if (key === "deleteMediaLibrary") {
                            createPortal(Modal, {
                              defaultVisible: true,
                              title: t<string>("MediaLibrary.Confirm"),
                              children: (
                                <div>
                                  {t<string>("MediaLibrary.DeleteConfirm")}
                                  <br />
                                  <span className="text-danger">
                                    {t<string>("Be careful, this operation can not be undone")}
                                  </span>
                                </div>
                              ),
                              onOk: async () => {
                                await BApi.mediaLibraryV2.deleteMediaLibraryV2(ml.id);
                                loadMediaLibraries();
                              },
                              footer: {
                                actions: ["ok", "cancel"],
                                okProps: {
                                  children: t<string>("Delete"),
                                  color: "danger",
                                  autoFocus: true,
                                },
                                cancelProps: {
                                  children: t<string>("MediaLibrary.Cancel"),
                                },
                              },
                            });
                          }
                        }}
                      >
                        <DropdownItem
                          key="deleteEnhancements"
                          className="text-warning"
                          startContent={<MdOutlineDelete className={"text-lg"} />}
                        >
                          {t<string>("MediaLibrary.DeleteEnhancements")}
                        </DropdownItem>
                        <DropdownItem
                          key="moveResourcesToOtherMediaLibrary"
                          className="text-primary"
                          startContent={<AiOutlineImport className={"text-lg"} />}
                        >
                          {t<string>("Move resources to another media library")}
                        </DropdownItem>
                        <DropdownItem
                          key="deleteMediaLibrary"
                          className="text-danger"
                          startContent={<MdOutlineDelete className={"text-lg"} />}
                        >
                          {t<string>("Delete this media library")}
                        </DropdownItem>
                      </DropdownMenu>
                    </Dropdown>
                  </div>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    );
  };

  const unknownMediaLibraryResourceCount = unknownResourceCounts?.unknownMediaLibraryCount ?? 0;

  return (
    <div className={"h-full flex flex-col"}>
      <OutdatedModal ref={(r) => (outdatedModalRef.current = r)} />
      <div className={"flex items-center justify-between"}>
        <div className={"flex items-center gap-2"}>
          <Button
            color={"primary"}
            size={"sm"}
            onPress={async () => {
              const no =
                mediaLibraries
                  .map((ml) => ml.name.split(" "))
                  .filter((x) => x.length > 1)
                  .map((x) => parseInt(x[1], 10))
                  .reduce((a, b) => Math.max(a, b), 0) + 1;
              const name = `${t("Media library")} ${no}`;

              await BApi.mediaLibraryV2.addMediaLibraryV2({
                name,
                paths: [],
              });
              await loadMediaLibraries();
            }}
          >
            <AiOutlinePlusCircle className={"text-lg"} />
            {t<string>("MediaLibrary.Add")}
          </Button>
          <Button
            color={"secondary"}
            size={"sm"}
            onPress={() => {
              BApi.mediaLibraryV2.syncAllMediaLibrariesV2();
            }}
          >
            <IoIosSync className={"text-lg"} />
            {t<string>("MediaLibrary.SynchronizeAll")}
          </Button>
          {unknownMediaLibraryResourceCount > 0 && (
            <Button
              isIconOnly
              radius={"sm"}
              size={"sm"}
              variant={"light"}
              onPress={async () => {
                createPortal(HandleUnknownResourcesModal, {
                  onHandled: () => loadMediaLibraries(),
                });
              }}
            >
              <AiOutlineFileUnknown className={"text-lg"} />
              {t<string>("Handle {{count}} unknown resources", { count: unknownMediaLibraryResourceCount })}
            </Button>
          )}
          <Tooltip
            content={
              <div className={"max-w-md space-y-2 text-sm p-2"}>
                <div>
                  {t<string>(
                    "By default, when a folder path changes, the resource data will be created as a new resource."
                  )}
                </div>
                <div>
                  {t<string>(
                    "If you enable this feature, a hidden .bakabase.json file will be generated inside folder resources to store their resource IDs."
                  )}
                </div>
                <div>
                  {t<string>(
                    "During synchronization, if a folder path changes but contains a .bakabase.json with any matching resource IDs, the resource data will be preserved instead of creating a new resource."
                  )}
                </div>
                <div>
                  {t<string>(
                    "Since a path can be added to multiple media libraries, resources under each path will be tracked per media library."
                  )}
                </div>
                <div>
                  {t<string>(
                    "Only supports resources in new version (V2) media libraries. Legacy resources (category-based media libraries) are not supported."
                  )}
                </div>
                <div className={"text-warning"}>
                  {t<string>(
                    "Enabling this feature will increase synchronization time as marker files need to be generated and verified."
                  )}
                </div>
                <div className={"font-semibold text-warning"}>
                  {t<string>(
                    "This feature will work after a complete GenerateResourceMarker task finishes."
                  )}
                </div>
              </div>
            }
            placement={"bottom-start"}
            closeDelay={200}
          >
            <div className={"flex items-center gap-2"}>
              <span className={"text-sm font-medium"}>{t<string>("Keep resources on path change")}
                <BetaChip />
              </span>
              <Switch
                size={"sm"}
                isSelected={resourceOptions?.keepResourcesOnPathChange ?? false}
                onValueChange={async (v) => {
                  if (!v) {
                    // Check if task is running
                    const tasks = useBTasksStore.getState().tasks;
                    const keepResourcesTask = tasks.find(
                      (t) =>
                        t.id === "GenerateResourceMarker" &&
                        (t.status === BTaskStatus.Running || t.status === BTaskStatus.Paused)
                    );

                    if (keepResourcesTask) {
                      // Show modal to confirm stopping the task
                      const shouldContinue = await new Promise<boolean>((resolve) => {
                        createPortal(Modal, {
                          defaultVisible: true,
                          title: t<string>("Confirm disabling resource markers"),
                          size: "md",
                          footer: {
                            actions: ["cancel", "ok"],
                            okProps: {
                              text: t<string>("Confirm and stop task"),
                              color: "danger",
                            },
                            cancelProps: {
                              text: t<string>("Cancel"),
                            },
                          },
                          onOk: () => resolve(true),
                          onClose: () => resolve(false),
                          children: (
                            <div className={"flex flex-col gap-3"}>
                              <div>
                                {t<string>(
                                  "The GenerateResourceMarker task is currently running."
                                )}
                              </div>
                              <div>
                                {t<string>(
                                  "Disabling this feature will stop the task. Are you sure you want to continue?"
                                )}
                              </div>
                            </div>
                          ),
                        });
                      });

                      if (!shouldContinue) {
                        return;
                      }

                      // Stop the task
                      try {
                        await BApi.backgroundTask.stopBackgroundTask("GenerateResourceMarker", { confirm: true });
                      } catch (e) {
                        console.error("Failed to stop task:", e);
                      }
                    }

                    // Ask whether to delete markers
                    const deleteMarkers = await new Promise<boolean>((resolve) => {
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t<string>("Delete marker files?"),
                        size: "md",
                        footer: {
                          actions: ["cancel", "ok"],
                          okProps: {
                            text: t<string>("Delete markers"),
                            color: "warning",
                          },
                          cancelProps: {
                            text: t<string>("Keep markers"),
                          },
                        },
                        onOk: () => resolve(true),
                        onClose: () => resolve(false),
                        children: (
                          <div className={"flex flex-col gap-2"}>
                            <div>
                              {t<string>(
                                "Do you want to delete all .bakabase.json marker files?"
                              )}
                            </div>
                            <div className={"text-sm opacity-70"}>
                              {t<string>(
                                "If you keep them, existing markers will remain in the folders."
                              )}
                            </div>
                          </div>
                        ),
                      });
                    });

                    // Patch options first (without deletion)
                    await BApi.options.patchResourceOptions({
                      keepResourcesOnPathChange: false,
                    });

                    // If user chose to delete markers, show progress modal
                    if (deleteMarkers) {
                      await new Promise<void>((resolve) => {
                        const DeletionProgressModal = () => {
                          const [progressState, setProgressState] = useState({
                            percentage: 0,
                            total: 0,
                            processed: 0,
                            deleted: 0,
                            failed: 0,
                            currentPath: "",
                          });
                          const [visible, setVisible] = useState(true);
                          const [showStopConfirm, setShowStopConfirm] = useState(false);
                          const [isStopping, setIsStopping] = useState(false);
                          const [isCancelled, setIsCancelled] = useState(false);
                          const abortControllerRef = useRef<AbortController | null>(null);

                          useEffect(() => {
                            const abortController = new AbortController();
                            abortControllerRef.current = abortController;

                            const startDeletion = async () => {
                              try {
                                const url = `${envConfig.apiEndpoint}/options/resource/delete-markers`;
                                const resp = await fetch(url, {
                                  method: "POST",
                                  signal: abortController.signal,
                                });

                                if (!resp.ok || !resp.body) {
                                  throw new Error(`Request failed: ${resp.status}`);
                                }

                                const reader = resp.body.getReader();
                                const decoder = new TextDecoder();
                                let buffer = "";

                                while (true) {
                                  const { value, done } = await reader.read();
                                  if (done) break;

                                  buffer += decoder.decode(value, { stream: true });
                                  let idx;

                                  while ((idx = buffer.indexOf("\n")) >= 0) {
                                    const line = buffer.slice(0, idx).trim();
                                    buffer = buffer.slice(idx + 1);
                                    if (!line) continue;

                                    try {
                                      const obj = JSON.parse(line) as {
                                        type: string;
                                        total?: number;
                                        processed?: number;
                                        deleted?: number;
                                        failed?: number;
                                        percentage?: number;
                                        currentResourcePath?: string;
                                        message?: string;
                                      };

                                      if (obj.type === "progress" || obj.type === "complete") {
                                        setProgressState({
                                          percentage: obj.percentage || 0,
                                          total: obj.total || 0,
                                          processed: obj.processed || obj.total || 0,
                                          deleted: obj.deleted || 0,
                                          failed: obj.failed || 0,
                                          currentPath: obj.currentResourcePath || "",
                                        });
                                      } else if (obj.type === "cancelled") {
                                        setIsCancelled(true);
                                        toast.warning(t<string>("Operation stopped. Some marker files may remain and need to be deleted manually."));
                                      } else if (obj.type === "error") {
                                        toast.danger(obj.message || "Error deleting markers");
                                      }
                                    } catch {
                                      // ignore broken line
                                    }
                                  }
                                }
                              } catch (e) {
                                if ((e as Error).name !== "AbortError") {
                                  toast.danger(t<string>("Failed to delete markers"));
                                } else if (isStopping) {
                                  setIsCancelled(true);
                                }
                              } finally {
                                setVisible(false);
                                setTimeout(() => resolve(), 500);
                              }
                            };

                            startDeletion();

                            return () => {
                              abortController.abort();
                            };
                          }, []);

                          const handleStopClick = () => {
                            setShowStopConfirm(true);
                          };

                          const handleConfirmStop = () => {
                            setIsStopping(true);
                            setShowStopConfirm(false);
                            if (abortControllerRef.current) {
                              abortControllerRef.current.abort();
                            }
                          };

                          return (
                            <>
                              <Modal
                                visible={visible}
                                title={t<string>("Deleting resource markers")}
                                size="md"
                                isDismissable={false}
                                hideCloseButton={true}
                                footer={
                                  <div className={"flex justify-end"}>
                                    <Button
                                      color="danger"
                                      variant="light"
                                      onClick={handleStopClick}
                                      isDisabled={isStopping || isCancelled}
                                    >
                                      {isStopping ? t<string>("Stopping...") : t<string>("Stop")}
                                    </Button>
                                  </div>
                                }
                              >
                                <div className={"flex flex-col gap-4 py-4"}>
                                  <div className={"flex items-center gap-3"}>
                                    <div className={"animate-spin rounded-full h-8 w-8 border-b-2 border-primary"}></div>
                                    <div className={"flex-1"}>
                                      <div className={"font-medium"}>
                                        {t<string>("Processing {{processed}} of {{total}} resources", {
                                          processed: progressState.processed,
                                          total: progressState.total,
                                        })}
                                      </div>
                                      <div className={"text-sm text-gray-500"}>
                                        {t<string>("Deleted: {{deleted}}, Failed: {{failed}}", {
                                          deleted: progressState.deleted,
                                          failed: progressState.failed,
                                        })}
                                      </div>
                                    </div>
                                    <div className={"text-xl font-bold"}>{progressState.percentage}%</div>
                                  </div>
                                  {progressState.currentPath && (
                                    <div className={"text-xs text-gray-500 truncate"}>
                                      {progressState.currentPath}
                                    </div>
                                  )}
                                  <div className={"w-full bg-gray-200 rounded-full h-2"}>
                                    <div
                                      className={"bg-primary h-2 rounded-full transition-all duration-300"}
                                      style={{ width: `${progressState.percentage}%` }}
                                    />
                                  </div>
                                </div>
                              </Modal>

                              <Modal
                                visible={showStopConfirm}
                                title={t<string>("Stop deletion operation?")}
                                size="md"
                                onClose={() => setShowStopConfirm(false)}
                                footer={
                                  <div className={"flex justify-end gap-2"}>
                                    <Button
                                      variant="light"
                                      onClick={() => setShowStopConfirm(false)}
                                    >
                                      {t<string>("Cancel")}
                                    </Button>
                                    <Button
                                      color="danger"
                                      onClick={handleConfirmStop}
                                    >
                                      {t<string>("Stop operation")}
                                    </Button>
                                  </div>
                                }
                              >
                                <div className={"flex flex-col gap-4"}>
                                  <div className={"text-warning font-semibold"}>
                                    {t<string>("Warning")}
                                  </div>
                                  <div>
                                    {t<string>("If you stop this operation now, you will need to delete the remaining marker files manually.")}
                                  </div>
                                  <div className={"bg-[var(--bakaui-overlap-background)] p-3 rounded"}>
                                    <div className={"font-medium mb-2"}>
                                      {t<string>("Marker file information:")}
                                    </div>
                                    <div className={"text-sm space-y-1"}>
                                      <div>
                                        {t<string>("File name: {{filename}}", { filename: ".bakabase.json" })}
                                      </div>
                                      <div>
                                        {t<string>("Location: Inside folder resources")}
                                      </div>
                                      <div>
                                        {t<string>("Attribute: Hidden file")}
                                      </div>
                                    </div>
                                  </div>
                                  <div className={"text-sm text-gray-600"}>
                                    {t<string>("You can search for and delete these files using your file manager or terminal.")}
                                  </div>
                                </div>
                              </Modal>
                            </>
                          );
                        };

                        createPortal(DeletionProgressModal, {});
                      });
                    }
                  } else {
                    await BApi.options.patchResourceOptions({
                      keepResourcesOnPathChange: true,
                    });
                  }
                  toast.success(t<string>("Saved"));
                }}
              />
            </div>
          </Tooltip>
        </div>
        <div>
          <Button
            color={"warning"}
            size={"sm"}
            variant={"flat"}
            onPress={() => setSortBy(SortBy.Path == sortBy ? SortBy.Template : SortBy.Path)}
          >
            <FaSort className={"text-base"} />
            {t<string>("MediaLibrary.SortBy", {
              sortBy: t<string>(`MediaLibrary.SortBy.${SortBy[sortBy]}`),
            })}
          </Button>
        </div>
      </div>
      {renderTable()}
    </div >
  );
};

MediaLibraryPage.displayName = "MediaLibraryPage";

export default MediaLibraryPage;

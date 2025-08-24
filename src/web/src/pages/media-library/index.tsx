"use client";

import type { Key } from "@react-types/shared";
import type { MediaLibraryTemplatePage } from "../media-library-template/models";
import type { MediaLibrary, MediaLibraryPlayer } from "./models";

import { useTranslation } from "react-i18next";
import { useEffect, useRef, useState } from "react";
import { useUpdate, useUpdateEffect } from "react-use";
import { FaRegSave, FaSort } from "react-icons/fa";
import toast from "react-hot-toast";
import {
  AiOutlineEdit,
  AiOutlineFolderOpen,
  AiOutlineImport,
  AiOutlinePlusCircle,
  AiOutlineProduct,
  AiOutlineSearch,
} from "react-icons/ai";
import { MdOutlineDelete } from "react-icons/md";
import { IoIosSync, IoMdExit } from "react-icons/io";
import { TbTemplate } from "react-icons/tb";
import { PiEmpty } from "react-icons/pi";
import { BsController } from "react-icons/bs";
import { useNavigate } from "react-router-dom";

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
  ColorPicker,
  Table,
  TableHeader,
  TableBody,
  TableColumn,
  TableRow,
  TableCell,
} from "@/components/bakaui";
import PathAutocomplete from "@/components/PathAutocomplete";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import { isNotEmpty, splitPathIntoSegments } from "@/components/utils";
import PresetTemplateBuilder from "@/pages/media-library-template/components/PresetTemplateBuilder";
import TemplateModal from "@/pages/media-library-template/components/TemplateModal";
import { InternalProperty, PropertyPool, SearchOperation } from "@/sdk/constants";
import { buildColorValueString } from "@/components/bakaui/components/ColorPicker";

enum SortBy {
  Path = 1,
  Template = 2,
}

const validate = (mls: Partial<MediaLibrary>[]): boolean => {
  return mls.every(
    (ml) =>
      Array.isArray(ml.paths) &&
      ml.paths.length > 0 &&
      ml.paths.every((p) => !!p) &&
      new Set(ml.paths).size === ml.paths.length &&
      isNotEmpty(ml.name) &&
      ml.templateId != undefined &&
      ml.templateId > 0,
  );
};

const MediaLibraryPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const navigate = useNavigate();

  const [mediaLibraries, setMediaLibraries] = useState<MediaLibrary[]>([]);
  const [templates, setTemplates] = useState<MediaLibraryTemplatePage[]>([]);
  const templatesRef = useRef<Record<number, MediaLibraryTemplatePage>>({});
  const [sortBy, setSortBy] = useState<SortBy>(SortBy.Path);
  const [editingMediaLibraries, setEditingMediaLibraries] = useState<
    Partial<MediaLibrary>[] | undefined
  >();
  const forceUpdate = useUpdate();
  const mediaLibrariesBeforeEditRef = useRef<MediaLibrary[] | undefined>();
  const outdatedModalRef = useRef<{ check: () => Promise<void> } | null>(null);

  const loadMediaLibraries = async (): Promise<MediaLibrary[]> => {
    const r = await BApi.mediaLibraryV2.getAllMediaLibraryV2();
    const list = r.data ?? [];

    setMediaLibraries(list);

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
    Promise.all([
      loadTemplates(),
      loadMediaLibraries(),
    ]).then(() => {
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
    return (
      <div className={"flex flex-col gap-1"}>
        {(ml.paths ?? []).map((p, idx) => (
          <Button
            key={idx}
            className={"justify-start"}
            color={"default"}
            size={"sm"}
            variant={"flat"}
            onPress={() => BApi.tool.openFileOrDirectory({ path: p })}
          >
            <AiOutlineFolderOpen className={"text-lg"} />
            {p}
          </Button>
        ))}
      </div>
    );
  };

  const renderTable = () => {
    const isEditing = !!editingMediaLibraries;
    const rows = isEditing ? (editingMediaLibraries as Partial<MediaLibrary>[]) : mediaLibraries;

    if (!isEditing && rows.length === 0) {
      return (
        <div className={"flex items-center gap-2 grow justify-center"}>
          <PiEmpty className={"text-2xl"} />
          {t<string>("MediaLibrary.NoMediaLibrariesFound")}
        </div>
      );
    }

    return (
      <Table
        removeWrapper
        className="mt-2"
        topContent={
          isEditing ? (
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Button
                  color="primary"
                  size="sm"
                  onPress={() =>
                    setEditingMediaLibraries([{}].concat(rows as Partial<MediaLibrary>[]))
                  }
                >
                  <AiOutlinePlusCircle className="text-base" />
                  {t<string>("MediaLibrary.Add")}
                </Button>
              </div>
              <div className="flex items-center gap-2">
                <Button
                  color={"primary"}
                  isDisabled={!validate(editingMediaLibraries as Partial<MediaLibrary>[])}
                  size={"sm"}
                  onPress={async () => {
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>("MediaLibrary.SaveAll"),
                      children: t<string>("MediaLibrary.SaveAllConfirm"),
                      onOk: async () => {
                        const data = editingMediaLibraries as MediaLibrary[];
                        const r = await BApi.mediaLibraryV2.saveAllMediaLibrariesV2(data);

                        if (!r.code) {
                          setEditingMediaLibraries(undefined);
                          toast.success(t<string>("MediaLibrary.Saved"));

                          outdatedModalRef.current?.check();
                          mediaLibrariesBeforeEditRef.current = undefined;
                        }
                      },
                    });
                  }}
                >
                  <FaRegSave className={"text-base"} />
                  {t<string>("MediaLibrary.Save")}
                </Button>
                <Button
                  color={"default"}
                  size={"sm"}
                  variant={"flat"}
                  onPress={() => {
                    setEditingMediaLibraries(undefined);
                    mediaLibrariesBeforeEditRef.current = undefined;
                  }}
                >
                  <IoMdExit className={"text-base"} />
                  {t<string>("MediaLibrary.ExitEditingMode")}
                </Button>
              </div>
            </div>
          ) : undefined
        }
      >
        <TableHeader>
          <TableColumn>{t<string>("MediaLibrary.Name")}</TableColumn>
          <TableColumn>{t<string>("MediaLibrary.Path")}</TableColumn>
          <TableColumn>{t<string>("MediaLibrary.Template")}</TableColumn>
          <TableColumn>{t<string>("MediaLibrary.Players")}</TableColumn>
          <TableColumn width="200">{t<string>("Operations")}</TableColumn>
        </TableHeader>
        <TableBody>
          {rows.map((row, i) => {
            if (isEditing) {
              const e = row as Partial<MediaLibrary>;
              const paths = Array.isArray(e.paths) ? e.paths : (e.paths = []);

              if (paths.length === 0) paths.push("");

              return (
                <TableRow key={i}>
                  {/* Name */}
                  <TableCell>
                    <Input
                      isRequired
                      isInvalid={e.name == undefined || e.name.length == 0}
                      label={t<string>("MediaLibrary.Name")}
                      placeholder={t<string>("MediaLibrary.NamePlaceholder")}
                      size={"sm"}
                      value={e.name}
                      onValueChange={(v) => {
                        e.name = v;
                        forceUpdate();
                      }}
                    />
                  </TableCell>
                  {/* Paths */}
                  <TableCell>
                    <div className={"flex flex-col gap-1"}>
                      {paths.map((p, idx) => (
                        <div
                          key={idx}
                          style={{
                            display: "flex",
                            alignItems: "center",
                            gap: 4,
                          }}
                        >
                          <PathAutocomplete
                            endContent={
                              <div className="flex items-center gap-1">
                                <Button
                                  isIconOnly
                                  color="primary"
                                  size="sm"
                                  variant="light"
                                  onPress={() => {
                                    paths.push("");
                                    e.paths = paths;
                                    forceUpdate();
                                  }}
                                >
                                  <AiOutlinePlusCircle className="text-lg" />
                                </Button>
                                {paths.length > 1 && (
                                  <Button
                                    isIconOnly
                                    color="danger"
                                    size="sm"
                                    variant="light"
                                    onPress={() => {
                                      paths.splice(idx, 1);
                                      e.paths = paths;
                                      forceUpdate();
                                    }}
                                  >
                                    <MdOutlineDelete className={"text-lg"} />
                                  </Button>
                                )}
                              </div>
                            }
                            isInvalid={!p}
                            isRequired={idx === 0}
                            label={
                              paths.length > 1
                                ? `${t<string>("MediaLibrary.Path")}${idx + 1}`
                                : t<string>("MediaLibrary.Path")
                            }
                            pathType="folder"
                            placeholder={t<string>("MediaLibrary.PathPlaceholder")}
                            size="sm"
                            value={p}
                            onChange={(v) => {
                              paths[idx] = v;
                              e.paths = paths.filter(Boolean);
                              forceUpdate();
                            }}
                            onSelectionChange={(selectedPath) => {
                              paths[idx] = selectedPath;
                              e.paths = paths.filter(Boolean);
                              forceUpdate();
                            }}
                          />
                        </div>
                      ))}
                    </div>
                  </TableCell>
                  {/* Template */}
                  <TableCell>
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
                      isInvalid={e.templateId == undefined || e.templateId <= 0}
                      label={t<string>("MediaLibrary.Template")}
                      placeholder={t<string>("MediaLibrary.TemplatePlaceholder")}
                      selectedKeys={e.templateId ? [e.templateId.toString()] : undefined}
                      size={"sm"}
                      onSelectionChange={(keys) => {
                        const arr = Array.from(keys);

                        if (arr.length > 0) {
                          const idStr = arr[0] as string;
                          const value = parseInt(idStr, 10);

                          if (value == -1) {
                            createPortal(PresetTemplateBuilder, {
                              onSubmitted: async (id) => {
                                e.templateId = id;
                                await loadTemplates();
                              },
                            });
                          } else {
                            e.templateId = value;
                            forceUpdate();
                          }
                        }
                      }}
                    />
                  </TableCell>
                  {/* Players (empty in edit) */}
                  <TableCell>{null}</TableCell>
                  {/* Actions */}
                  <TableCell>
                    <Button
                      isIconOnly
                      color={"danger"}
                      variant={"light"}
                      onPress={() => {
                        editingMediaLibraries!.splice(i, 1);
                        forceUpdate();
                      }}
                    >
                      <MdOutlineDelete className={"text-lg"} />
                    </Button>
                  </TableCell>
                </TableRow>
              );
            }

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
                    <div style={{ color: ml.color ?? textColor }}>{ml.name}</div>
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
                    <Chip
                      color={"success"}
                      size={"sm"}
                      startContent={<AiOutlineProduct className={"text-lg"} />}
                      variant={"light"}
                    >
                      {ml.resourceCount}
                    </Chip>
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

                              navigate(`/resource?query=${query}`);
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
                    <SyncStatus
                      id={ml.id}
                      onSyncCompleted={() => {
                        BApi.mediaLibraryV2.getMediaLibraryV2(ml.id).then((r) => {
                          if (!r.data) return;
                          const updatedMediaLibraries = mediaLibraries.map((m) =>
                            m.id === ml.id ? r.data! : m,
                          );

                          setMediaLibraries(updatedMediaLibraries);
                        });
                      }}
                    />
                    <Button
                      isIconOnly
                      color={"danger"}
                      variant={"light"}
                      onPress={() => {
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
                            forceUpdate();
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
                      }}
                    >
                      <MdOutlineDelete className={"text-lg"} />
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    );
  };

  return (
    <div className={"h-full flex flex-col"}>
      <OutdatedModal ref={(r) => (outdatedModalRef.current = r)} />
      <div className={"flex items-center justify-between"}>
        <div className={"flex items-center gap-1"}>
          {editingMediaLibraries ? null : (
            <>
              <Button
                color={"primary"}
                size={"sm"}
                onPress={() => {
                  mediaLibrariesBeforeEditRef.current = JSON.parse(
                    JSON.stringify(mediaLibraries),
                  ) as MediaLibrary[];
                  setEditingMediaLibraries(
                    mediaLibraries.length == 0
                      ? [{}]
                      : (JSON.parse(JSON.stringify(mediaLibraries)) as Partial<MediaLibrary>[]),
                  );
                }}
              >
                <AiOutlineEdit className={"text-base"} />
                {t<string>("MediaLibrary.AddOrEdit")}
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
            </>
          )}
        </div>
        {!editingMediaLibraries && (
          <div>
            <Button
              color={"default"}
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
        )}
      </div>
      {editingMediaLibraries ? (
        <div className="flex flex-col gap-2 w-full h-full">{renderTable()}</div>
      ) : (
        renderTable()
      )}
    </div>
  );
};

MediaLibraryPage.displayName = "MediaLibraryPage";

export default MediaLibraryPage;

"use client";

import type { Key } from "@react-types/shared";
import type { MediaLibraryTemplate } from "../media-library-template/models";
import type { components } from "@/sdk/BApi2";

import { useTranslation } from "react-i18next";
import React, { useEffect, useRef, useState } from "react";
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
import { useNavigate } from "react-router-dom";

import SyncStatus from "./components/SyncStatus";

import {
  Button,
  Chip,
  Input,
  Modal,
  Select,
  Tooltip,
  ColorPicker,
  Divider,
} from "@/components/bakaui";
import PathAutocomplete from "@/components/PathAutocomplete";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import { isNotEmpty } from "@/components/utils";
import PresetTemplateBuilder from "@/pages/media-library-template/components/PresetTemplateBuilder";
import TemplateModal from "@/pages/media-library-template/components/TemplateModal";
import {
  InternalProperty,
  PropertyPool,
  SearchOperation,
} from "@/sdk/constants";
import { buildColorValueString } from "@/components/bakaui/components/ColorPicker";

type MediaLibrary =
  components["schemas"]["Bakabase.Abstractions.Models.Domain.MediaLibraryV2"];

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

export default () => {
  const { t } = useTranslation();
  const { createPortal, color } = useBakabaseContext();
  const navigate = useNavigate();

  const [mediaLibraries, setMediaLibraries] = useState<MediaLibrary[]>([]);
  const [templates, setTemplates] = useState<MediaLibraryTemplate[]>([]);
  const templatesRef = useRef<Record<number, MediaLibraryTemplate>>({});
  const [sortBy, setSortBy] = useState<SortBy>(SortBy.Path);
  const [editingMediaLibraries, setEditingMediaLibraries] =
    useState<Partial<MediaLibrary>[]>();
  const forceUpdate = useUpdate();

  const loadMediaLibraries = async () => {
    const r = await BApi.mediaLibraryV2.getAllMediaLibraryV2();

    setMediaLibraries(r.data ?? []);
  };

  const loadTemplates = async () => {
    const r = await BApi.mediaLibraryTemplate.getAllMediaLibraryTemplates();
    const tpls = r.data ?? [];

    // @ts-ignore
    setTemplates(tpls);
    templatesRef.current = tpls.reduce((s, t) => {
      s[t.id] = t;

      return s;
    }, {});
  };

  useEffect(() => {
    loadTemplates();
    loadMediaLibraries();
  }, []);

  useUpdateEffect(() => {
    switch (sortBy) {
      case SortBy.Path:
        mediaLibraries.sort((a, b) =>
          (a.paths[0] ?? "").localeCompare(b.paths[0] ?? ""),
        );
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

  return (
    <div className={"h-full flex flex-col"}>
      <div className={"flex items-center justify-between"}>
        <div className={"flex items-center gap-1"}>
          {editingMediaLibraries ? null : (
            <>
              <Button
                color={"primary"}
                size={"sm"}
                onPress={() =>
                  setEditingMediaLibraries(
                    mediaLibraries.length == 0
                      ? [{}]
                      : (JSON.parse(
                          JSON.stringify(mediaLibraries),
                        ) as Partial<MediaLibrary>[]),
                  )
                }
              >
                <AiOutlineEdit className={"text-base"} />
                {t<string>("MediaLibrary.AddOrEdit")}
              </Button>
              <Button
                // variant={'flat'}
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
              onPress={() =>
                setSortBy(SortBy.Path == sortBy ? SortBy.Template : SortBy.Path)
              }
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
        <div className="flex flex-col gap-2 w-full h-full">
          <div
            className={
              "grid gap-1 mt-2 items-center min-h-0 grow overflow-auto"
            }
            style={{ gridTemplateColumns: "auto 1fr 2fr 1fr auto" }}
          >
            {editingMediaLibraries.map((e, i) => {
              const paths = Array.isArray(e.paths) ? e.paths : (e.paths = []);

              if (paths.length == 0) {
                paths.push("");
              }
              const pathsValid =
                paths.length > 0 &&
                paths.every((p) => !!p) &&
                new Set(paths).size === paths.length;

              return (
                <>
                  <div className={"flex justify-center items-center"}>
                    #{i + 1}
                  </div>
                  <Input
                    isRequired
                    isInvalid={e.name == undefined || e.name.length == 0}
                    label={t<string>("MediaLibrary.Name")}
                    placeholder={t<string>("MediaLibrary.NamePlaceholder")}
                    size={"sm"}
                    value={e.name}
                    // variant="underlined"
                    onValueChange={(v) => {
                      e.name = v;
                      forceUpdate();
                    }}
                  />
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
                          placeholder={t<string>(
                            "MediaLibrary.PathPlaceholder",
                          )}
                          size="sm"
                          value={p}
                          // variant="underlined"
                          onChange={(v) => {
                            paths[idx] = v;
                            // 保证无空字符串
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
                          textValue: t<string>(
                            "MediaLibrary.CreateNewTemplate",
                          ),
                        },
                      ])}
                    isInvalid={e.templateId == undefined || e.templateId <= 0}
                    label={t<string>("MediaLibrary.Template")}
                    placeholder={t<string>("MediaLibrary.TemplatePlaceholder")}
                    selectedKeys={
                      e.templateId ? [e.templateId.toString()] : undefined
                    }
                    size={"sm"}
                    // variant="underlined"
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
                  {/* <div className={'flex items-center'}>
                    <ColorPicker
                      color={e.color}
                      onChange={v => {
                        if (typeof v === 'string') {
                          e.color = v;
                        } else if ('r' in v && 'g' in v && 'b' in v && 'a' in v) {
                          e.color = `rgba(${v.r},${v.g},${v.b},${v.a})`;
                        } else if ('h' in v && 's' in v && 'l' in v && 'a' in v) {
                          e.color = `hsla(${v.h},${v.s}%,${v.l}%,${v.a})`;
                        } else {
                          e.color = '';
                        }
                        forceUpdate();
                      }}
                    />
                  </div> */}
                  <div className={"flex items-center gap-1"}>
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
                  </div>
                  {editingMediaLibraries.length > i + 1 && (
                    <Divider className="col-span-full" />
                  )}
                </>
              );
            })}
          </div>
          <div className={"flex items-center gap-2 justify-between py-2"}>
            <div className={"flex items-center gap-2 mt-2"}>
              <Button
                color={"default"}
                size={"sm"}
                onPress={() =>
                  setEditingMediaLibraries(
                    editingMediaLibraries!.concat([
                      {} as Partial<MediaLibrary>,
                    ]),
                  )
                }
              >
                <AiOutlinePlusCircle className={"text-base"} />
                {t<string>("MediaLibrary.Add")}
              </Button>
            </div>
            <div className={"flex items-center gap-2 mt-2"}>
              <Button
                color={"primary"}
                isDisabled={!validate(editingMediaLibraries)}
                size={"sm"}
                onPress={async () => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t<string>("MediaLibrary.SaveAll"),
                    children: t<string>("MediaLibrary.SaveAllConfirm"),
                    onOk: async () => {
                      const data = editingMediaLibraries as MediaLibrary[];
                      const r =
                        await BApi.mediaLibraryV2.saveAllMediaLibrariesV2(data);

                      if (!r.code) {
                        setEditingMediaLibraries(undefined);
                        toast.success(t<string>("MediaLibrary.Saved"));
                        await loadMediaLibraries();
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
                onPress={() => setEditingMediaLibraries(undefined)}
              >
                <IoMdExit className={"text-base"} />
                {t<string>("MediaLibrary.ExitEditingMode")}
              </Button>
            </div>
          </div>
        </div>
      ) : mediaLibraries && mediaLibraries.length > 0 ? (
        <div>
          <div
            className={"inline-grid gap-2 mt-2 items-center"}
            style={{ gridTemplateColumns: "repeat(4, auto)" }}
          >
            {mediaLibraries.map((ml, i) => {
              const template = templates.find((t) => t.id == ml.templateId);
              const getCssVariable = (variableName: string) => {
                if (typeof document === "undefined") return "";

                return getComputedStyle(
                  document.documentElement,
                ).getPropertyValue(variableName);
              };
              const textColor = getCssVariable("--theme-text") || "#222";

              return (
                <>
                  {renderPath(ml)}
                  <div className="flex items-center gap-1">
                    <div style={{ color: ml.color ?? textColor }}>
                      {ml.name}
                    </div>
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
                  <div className="flex items-center">
                    <Chip
                      color={"success"}
                      size={"sm"}
                      startContent={<AiOutlineProduct className={"text-lg"} />}
                      variant={"light"}
                    >
                      {ml.resourceCount}
                    </Chip>
                    <Tooltip
                      content={t<string>("MediaLibrary.SearchResources")}
                      placement="top"
                    >
                      <Button
                        isIconOnly
                        className={""}
                        radius={"sm"}
                        size={"sm"}
                        variant={"light"}
                        onPress={() => {
                          createPortal(Modal, {
                            title: t<string>("MediaLibrary.Confirm"),
                            children: t<string>(
                              "MediaLibrary.LeavePageConfirm",
                            ),
                            defaultVisible: true,
                            onOk: async () => {
                              // 先调用GetFilterValueProperty接口获取valueProperty
                              const valuePropertyResponse =
                                await BApi.resource.getFilterValueProperty({
                                  propertyPool: PropertyPool.Internal,
                                  propertyId: InternalProperty.MediaLibraryV2, // MediaLibrary 属性ID
                                  operation: SearchOperation.Equals, // Equal
                                });

                              // 创建搜索表单，包含媒体库ID过滤条件
                              const searchForm = {
                                group: {
                                  combinator: 1, // And
                                  disabled: false,
                                  filters: [
                                    {
                                      propertyPool: PropertyPool.Internal,
                                      propertyId:
                                        InternalProperty.MediaLibraryV2, // MediaLibrary 属性ID
                                      operation: SearchOperation.Equals, // Equal
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

                              // 跳转到Resource页面并带上搜索参数
                              const query = encodeURIComponent(
                                JSON.stringify(searchForm),
                              );

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
                        BApi.mediaLibraryV2
                          .getMediaLibraryV2(ml.id)
                          .then((r) => {
                            if (!r.data) return;
                            const updatedMediaLibraries = mediaLibraries.map(
                              (m) => (m.id === ml.id ? r.data! : m),
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
                                {t<string>(
                                  "Be careful, this operation can not be undone",
                                )}
                              </span>
                            </div>
                          ),
                          onOk: async () => {
                            await BApi.mediaLibraryV2.deleteMediaLibraryV2(
                              ml.id,
                            );
                            await loadMediaLibraries();
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
                  {mediaLibraries.length > i + 1 && (
                    <Divider className="col-span-full" />
                  )}
                </>
              );
            })}
          </div>
        </div>
      ) : (
        <div className={"flex items-center gap-2 grow justify-center"}>
          <PiEmpty className={"text-2xl"} />
          {t<string>("MediaLibrary.NoMediaLibrariesFound")}
        </div>
      )}
    </div>
  );
};

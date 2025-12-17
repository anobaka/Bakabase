"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsDomainPathConfiguration } from "@/sdk/Api";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { DeleteOutlined, FolderOpenOutlined } from "@ant-design/icons";
import { useUpdate } from "react-use";

import BApi from "@/sdk/BApi";
import {
  buildLogger,
  splitPathIntoSegments,
  standardizePath,
} from "@/components/utils";
import { MediaLibraryAdditionalItem } from "@/sdk/constants";
import PathSegmentsConfiguration, {
  PathSegmentConfigurationPropsMatcherOptions,
} from "@/components/PathSegmentsConfiguration";
import { Button, Chip, Divider, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { PscPropertyType } from "@/components/PathSegmentsConfiguration/models/PscPropertyType";
import { PscMatcherValue } from "@/components/PathSegmentsConfiguration/models/PscMatcherValue";
import {
  convertToPathConfigurationDtoFromPscValue,
  convertToPscValueFromPathConfigurationDto,
} from "@/components/PathSegmentsConfiguration/helpers";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";

const log = buildLogger("PathConfigurationDialog");

type Library = {
  id: number;
  pathConfigurations: BakabaseAbstractionsModelsDomainPathConfiguration[];
  name: string;
};

interface Props extends DestroyableProps {
  libraryId: number;
  pcIdx: number;
  onClosed: (pc: BakabaseAbstractionsModelsDomainPathConfiguration) => any;
}
const PathConfigurationDialog = ({
  libraryId,
  pcIdx,
  onClosed,
  ...props
}: Props) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const { createPortal } = useBakabaseContext();

  const [library, setLibrary] = useState<Library>();
  const [customPropertyMap, setCustomPropertyMap] = useState<
    Record<number, { name: string }>
  >({});

  useEffect(() => {
    log("Initialize with", props);

    loadLibrary();

    BApi.customProperty.getAllCustomProperties().then((r) => {
      const ps = r.data || [];

      setCustomPropertyMap(
        ps.reduce<Record<number, { name: string }>>((s, t) => {
          s[t.id!] = { name: t.name! };

          return s;
        }, {}),
      );
    });
  }, []);

  const loadLibrary = async () => {
    const r = await BApi.mediaLibrary.getMediaLibrary(libraryId, {
      additionalItems:
        MediaLibraryAdditionalItem.PathConfigurationBoundProperties,
    });
    const l = r.data;

    if (l) {
      setLibrary({
        id: l.id!,
        name: l.name!,
        pathConfigurations: l.pathConfigurations ?? [],
      });
    }
  };

  if (!library) {
    return null;
  }

  const pc = library.pathConfigurations[pcIdx];

  const save = async (
    patches: Partial<BakabaseAbstractionsModelsDomainPathConfiguration>,
  ) => {
    const newPcs = library.pathConfigurations.slice();

    newPcs.splice(pcIdx, 1, { ...pc, ...patches });
    log(
      `Saving ${pcIdx} of ${library.pathConfigurations.length} in ${library.name}`,
      pc,
      newPcs,
    );
    await BApi.mediaLibrary.patchMediaLibrary(library.id, {
      pathConfigurations: newPcs,
    });
    await loadLibrary();
  };

  const showPsc = (
    segments: string[],
    pc: BakabaseAbstractionsModelsDomainPathConfiguration,
    isDirectory: boolean,
  ) => {
    const simpleMatchers = {
      [PscPropertyType.RootPath]: true,
      [PscPropertyType.Resource]: false,
      [PscPropertyType.ParentResource]: false,
      [PscPropertyType.CustomProperty]: false,
      [PscPropertyType.Rating]: false,
      [PscPropertyType.Introduction]: false,
    };
    const matchers = Object.keys(simpleMatchers).reduce<
      PathSegmentConfigurationPropsMatcherOptions[]
    >((ts, t) => {
      ts.push(
        new PathSegmentConfigurationPropsMatcherOptions({
          propertyType: parseInt(t, 10),
          readonly: simpleMatchers[t],
        }),
      );

      return ts;
    }, []);

    let tmpValue = convertToPscValueFromPathConfigurationDto(pc);

    createPortal(Modal, {
      defaultVisible: true,
      size: "xl",
      children: (
        <PathSegmentsConfiguration
          defaultValue={tmpValue}
          isDirectory={isDirectory || false}
          matchers={matchers}
          segments={segments!}
          onChange={(value) => {
            tmpValue = value;
          }}
        />
      ),
      onOk: async () => {
        const dto = convertToPathConfigurationDtoFromPscValue(tmpValue);

        console.log(5555, dto);
        await save(dto);
        onClosed(dto);
        // await save({
        //   rpmValues: dto.rpmValues ?? undefined,
        //   path: dto.path ?? undefined,
        // });
      },
    });
  };

  const renderRpmValues = () => {
    const values = pc?.rpmValues || [];

    if (values.length == 0) {
      return null;
    }

    // console.log(values, 55555);

    return (
      <div className="flex flex-col gap-1 my-1">
        {values.map((s, i) => {
          let label = s.propertyName;

          if (label == undefined || label.length == 0) {
            label = t<string>("Unknown property");
          }
          let isDeletable = !s.isResourceProperty;

          return (
            <div className={"flex items-center gap-2"}>
              <Chip
                color={s.isResourceProperty ? "primary" : "default"}
                radius={"sm"}
                size={"sm"}
              >
                {label}
              </Chip>
              <div className="value select-text">
                {PscMatcherValue.ToString(t, {
                  fixedText: s.fixedText ?? undefined,
                  layer: s.layer ?? undefined,
                  regex: s.regex ?? undefined,
                  valueType: s.valueType!,
                })}
              </div>
              {isDeletable && (
                <Button
                  isIconOnly
                  color={"danger"}
                  size={"sm"}
                  variant={"light"}
                  onClick={() => {
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>(
                        "Deleting property locator for {{propertyName}}",
                        { propertyName: label },
                      ),
                      onOk: async () => {
                        pc.rpmValues?.splice(i, 1);
                        await BApi.mediaLibrary.patchMediaLibrary(libraryId, {
                          pathConfigurations: library.pathConfigurations,
                        });
                        await loadLibrary();
                      },
                    });
                  }}
                >
                  <DeleteOutlined className={"text-base"} />
                </Button>
              )}
            </div>
          );
        })}
      </div>
    );
  };

  const rootPathIsSet = !!pc?.path;

  return (
    <>
      <Modal
        defaultVisible
        footer={{
          actions: ["cancel"],
        }}
        size={"xl"}
        title={t<string>("Path configuration")}
        onClose={() => {
          onClosed?.(pc);
        }}
        onDestroyed={props.onDestroyed}
      >
        <div className="flex flex-col gap-2 shadow">
          <section>
            <div className="text-base font-bold mb-1">
              {t<string>("Root path")}
            </div>
            <div>
              <Button
                onClick={() => {
                  createPortal(FileSystemSelectorModal, {
                    startPath: pc.path ?? undefined,
                    targetType: 'folder',
                    onSelected: e => {
                      save({ path: e.path });
                    },
                    defaultSelectedPath: pc?.path ?? undefined,
                  });
                }}
                variant={'light'}
                // size={'sm'}
                color={'primary'}
              >
                {pc?.path ?? t<string>("Setup")}
              </Button>
              {rootPathIsSet && (
                <FolderOpenOutlined
                  className={"text-base cursor-pointer ml-1"}
                  onClick={(e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    BApi.tool.openFileOrDirectory({ path: pc.path! });
                  }}
                />
              )}
            </div>
          </section>
          <Divider />
          <section className="">
            <div className="text-base font-bold">
              {t<string>("Setup how to find resources and properties")}
            </div>
            {pc?.path && (
              <div className={"opacity-60 italic"}>
                {t<string>(
                  "To setup this item, you should pick up a file first within your root path.",
                )}
                <br />
                {t<string>(
                  "If you want to populate properties as many as possible, you should pick up a file with more layers in path.",
                )}
              </div>
            )}
            {renderRpmValues()}
            <div className={""}>
              {pc?.path ? (
                <Button
                  // size={'sm'}
                  color={"primary"}
                  variant={"light"}
                  onClick={() => {
                    createPortal(FileSystemSelectorDialog, {
                      // targetType: 'folder',
                      startPath: pc.path!,
                      // targetType: 'file',
                      onSelected: (e) => {
                        const std = standardizePath(e.path)!;
                        const stdPrev = standardizePath(pc.path!);

                        if (stdPrev && std.startsWith(stdPrev)) {
                          const segments = splitPathIntoSegments(e.path);

                          showPsc(segments, pc, e.isDirectory);
                        } else {
                          createPortal(Modal, {
                            defaultVisible: true,
                            title: t<string>("Error"),
                            children: t<string>(
                              "You cannot select a file outside the root folder. If you want to change the root folder of your library, you should click on your root folder.",
                            ),
                            footer: {
                              actions: ["cancel"],
                            },
                          });
                        }
                      },
                    });
                  }}
                >
                  {t<string>("Setup")}
                </Button>
              ) : (
                <Chip color={"warning"} size={"sm"} variant={"light"}>
                  {t<string>(
                    "You need to set up root path before configuring this item",
                  )}
                </Chip>
              )}
            </div>
          </section>
        </div>
      </Modal>
    </>
  );
};

PathConfigurationDialog.displayName = "PathConfigurationDialog";

export default PathConfigurationDialog;

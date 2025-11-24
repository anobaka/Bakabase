"use client";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { DeleteOutlined, FolderOpenOutlined, PlusCircleOutlined } from "@ant-design/icons";
import { MdWarning } from "react-icons/md";
import moment from "moment";
import { MdFolder } from "react-icons/md";
import dayjs from "dayjs";
import { AiOutlineProduct } from "react-icons/ai";

import {
  Button,
  Checkbox,
  Switch,
  Tooltip,
  Modal,
  Dropdown,
  Icon,
  Input,
  Table,
  TimeInput,
  DropdownMenu,
  DropdownItem,
  DropdownTrigger,
  TableHeader,
  TableBody,
  TableColumn,
  TableCell,
  TableRow,
  Textarea,
} from "@/components/bakaui";
import { toast } from "@/components/bakaui";
import { FileSystemSelectorButton, FileSystemSelectorModal } from "@/components/FileSystemSelector";

import "./index.scss";

import AnimatedArrow from "@/components/AnimatedArrow";
import BApi from "@/sdk/BApi";
import { useFileMovingProgressesStore } from "@/stores/fileMovingProgresses";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MediaLibraryPathSelectorV2 from "@/components/MediaLibraryPathSelectorV2";
import { useFileSystemOptionsStore } from "@/stores/options";

interface IValue {
  targets: {
    path: string;
    sources: string[];
    overwrite: boolean;
  }[];
  enabled: boolean;
  delay?: string;
}

class Value implements IValue {
  delay?: string;
  enabled: boolean = false;
  targets: {
    path: string;
    sources: string[];
    overwrite: boolean;
  }[] = [];
}
const FileMoverPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const fsOptionsStore = useFileSystemOptionsStore();

  const [preferredTarget, setPreferredTarget] = useState<string | undefined>();
  const [preferredSource, setPreferredSource] = useState<string | undefined>();

  const value = fsOptionsStore.data.fileMover ?? new Value();

  const [quickEditModeData, setQuickEditModeData] = useState<
    {
      path?: string;
      sources?: string[];
    }[]
  >();

  const progresses = useFileMovingProgressesStore((state) => state.progresses);

  useEffect(() => {}, []);

  const save = (patches = {}, cb?: () => any) => {
    const options = {
      ...(value || {}),
      ...(patches || {}),
    };

    fsOptionsStore
      .patch({
        // @ts-ignore
        fileMover: options,
      })
      .then(() => {
        toast.success(t<string>("Saved"));
        cb && cb();
      });
  };

  const addTarget = (targetPath: string, cb: () => void = () => {}) => {
    if (!targets.some((t) => t.path == targetPath)) {
      targets.push({
        path: targetPath,
        sources: [],
        overwrite: false,
      });
      save(
        {
          targets,
        },
        () => {
          setPreferredTarget(targetPath);
          cb();
        },
      );
    }
  };

  const updateTarget = (targetPath: string, newTargetPath: string, cb: () => void = () => {}) => {
    const targetIndex = targets.findIndex((a) => a.path == targetPath);
    const target = targets[targetIndex]!;

    target.path = newTargetPath;
    targets.splice(targetIndex, 1, target);
    save(
      {
        targets,
      },
      () => {
        setPreferredTarget(targetPath);
        cb();
      },
    );
  };

  const addSource = (targetPath: string, sourcePath: string) => {
    const target = targets.find((a) => a.path == targetPath)!;

    console.log("Adding source:", { targetPath, sourcePath, target });

    if (!target.sources) {
      target.sources = [];
    }

    if (target.sources.indexOf(sourcePath) == -1) {
      target.sources.push(sourcePath);
      console.log("Source added, new sources:", target.sources);
      save(
        {
          targets,
        },
        () => {
          setPreferredSource(sourcePath);
        },
      );
    } else {
      console.log("Source already exists");
      setPreferredTarget(targetPath);
    }
  };

  const updateSource = (targetPath: string, sourcePath: string, newSourcePath: string) => {
    const target = targets.find((a) => a.path == targetPath);

    if (!target || !target.sources) return;

    const idx = target.sources.indexOf(sourcePath);

    if (idx > -1) {
      if (target.sources.indexOf(newSourcePath) == -1) {
        target.sources[idx] = newSourcePath;
        save({
          targets,
        });
      }
    } else {
      setPreferredSource(sourcePath);
    }
  };

  const { enabled = false, targets = [] } = value;

  type Item = {
    target: string;
    rowSpan: number | undefined;
    source: string;
    overwrite: boolean;
  };

  const ds: Item[] =
    (targets ?? []).reduce<Item[]>((s, t) => {
      const sources = (t.sources || []).slice();
      // sources.push(null);
      const newArr = sources.map((x, i) => ({
        target: t.path,
        rowSpan: i == 0 ? sources.length : undefined,
        source: x,
        overwrite: t.overwrite,
      }));

      if (sources.length == 0) {
        newArr.push({
          target: t.path,
          rowSpan: undefined,
          source: "",
          overwrite: false,
        });
      }

      newArr.sort((a, b) => {
        return (a.source == preferredSource ? -1 : 0) - (b.source == preferredSource ? -1 : 0);
      });
      if (t.path == preferredTarget) {
        return newArr.concat(s);
      } else {
        return s.concat(newArr);
      }
    }, []) || [];

  ds.splice(0, 0, {
    target: "",
    rowSpan: 1,
    source: "",
    overwrite: false,
  });

  // console.log(value, ds);

  const renderCommonOperations = (path: string) => {
    return (
      <>
        <Button
          isIconOnly
          size={"sm"}
          variant={"light"}
          onPress={() => {
            BApi.tool.openFileOrDirectory({
              path,
            });
          }}
        >
          <FolderOpenOutlined className={"text-base"} />
        </Button>
      </>
    );
  };

  const setOverwrite = (targetPath: string, overwrite: boolean) => {
    const targetIndex = targets.findIndex((a) => a.path == targetPath);
    const target = targets[targetIndex]!;

    target.overwrite = overwrite;
    targets.splice(targetIndex, 1, target);
    save({
      targets,
    });
  };

  const renderQuickEditMode = () => {
    return (
      <Table isCompact removeWrapper className={"quick-edit-mode-table"}>
        <TableHeader>
          <TableColumn>{t<string>("Target")}</TableColumn>
          <TableColumn align="center" width={90}>
            {t<string>("Moving direction")}
          </TableColumn>
          <TableColumn>{t<string>("Source")}</TableColumn>
        </TableHeader>
        <TableBody>
          {quickEditModeData?.map((row, i) => (
            <TableRow key={i}>
              <TableCell>
                {i === quickEditModeData.length - 1 ? (
                  <Button
                    color={"primary"}
                    size={"sm"}
                    onPress={() => {
                      quickEditModeData?.splice(i, 0, {});
                      setQuickEditModeData([...quickEditModeData!]);
                    }}
                  >
                    {t<string>("Add")}
                  </Button>
                ) : (
                  <div className={"target"}>
                    <Input
                      placeholder={t<string>("Target path")}
                      value={row.path}
                      onValueChange={(v: string) => {
                        quickEditModeData![i].path = v;
                        setQuickEditModeData([...quickEditModeData!]);
                      }}
                    />
                    <Button
                      isIconOnly
                      color={"danger"}
                      size={"sm"}
                      variant={"light"}
                      onPress={() => {
                        quickEditModeData!.splice(i, 1);
                        setQuickEditModeData([...quickEditModeData!]);
                      }}
                    >
                      <DeleteOutlined className={"text-base"} />
                    </Button>
                  </div>
                )}
              </TableCell>
              <TableCell align="center">
                <AnimatedArrow direction={"left"} />
              </TableCell>
              <TableCell>
                {i === quickEditModeData.length - 1 ? null : (
                  <Textarea
                    maxRows={100}
                    minRows={2}
                    placeholder={t<string>("One path per line")}
                    size={"sm"}
                    style={{ width: "100%" }}
                    value={row.sources?.join("\n")}
                    onValueChange={(v: string) => {
                      quickEditModeData![i].sources = v.split("\n");
                      setQuickEditModeData([...quickEditModeData!]);
                    }}
                  />
                )}
              </TableCell>
            </TableRow>
          )) || []}
        </TableBody>
      </Table>
    );
  };

  const renderNormalEditMode = () => {
    return (
      <Table isCompact removeWrapper>
        <TableHeader>
          <TableColumn>{t<string>("Target")}</TableColumn>
          <TableColumn align="center" width={90}>
            {t<string>("Moving direction")}
          </TableColumn>
          <TableColumn>{t<string>("Source")}</TableColumn>
        </TableHeader>
        <TableBody>
          {ds.map((record, i) => (
            <TableRow key={i}>
              <TableCell rowSpan={record.rowSpan}>
                {/* Target cell content */}
                {(() => {
                  const target = record.target;

                  return (
                    <div className={"target"}>
                      <div className="left">
                        <Dropdown>
                          <DropdownTrigger>
                            <Button color="primary" size="sm" variant="light">
                              {target || t<string>("Add target path")}
                            </Button>
                          </DropdownTrigger>
                          <DropdownMenu>
                            <DropdownItem
                              key="select-from-file-system"
                              startContent={<MdFolder className="text-base" />}
                              onPress={() => {
                                createPortal(FileSystemSelectorModal, {
                                  targetType: "folder",
                                  onSelected: (e: any) => {
                                    if (!target) {
                                      addTarget(e.path);
                                    } else {
                                      updateTarget(target, e.path);
                                    }
                                  },
                                  defaultSelectedPath: target ?? null,
                                });
                              }}
                            >
                              {t<string>("Select from file system")}
                            </DropdownItem>
                            <DropdownItem
                              key="select-from-media-library"
                              startContent={<AiOutlineProduct className="text-base" />}
                              onPress={() => {
                                createPortal(MediaLibraryPathSelectorV2, {
                                  onSelect: (id, path, isLegacyMediaLibrary) => {
                                    if (!target) {
                                      addTarget(path);
                                    } else {
                                      updateTarget(target, path);
                                    }
                                  },
                                });
                              }}
                            >
                              {t<string>("Select from media library")}
                            </DropdownItem>
                          </DropdownMenu>
                        </Dropdown>
                      </div>
                      {target && (
                        <div className={"flex items-center gap-1"}>
                          <Tooltip content={t<string>("Overwrite files in target path")}>
                            <Checkbox
                              isSelected={record.overwrite}
                              size={"sm"}
                              onValueChange={(v) => setOverwrite(target, v)}
                            >
                              {t<string>("Overwrite")}
                            </Checkbox>
                          </Tooltip>
                          <FileSystemSelectorButton
                            isIconOnly
                            fileSystemSelectorProps={{
                              onSelected: (e) => {
                                addSource(target, e.path);
                              },
                            }}
                          >
                            <PlusCircleOutlined className={"text-base"} />
                          </FileSystemSelectorButton>
                          {renderCommonOperations(target)}
                          <Button
                            isIconOnly
                            color={"danger"}
                            size={"sm"}
                            variant={"light"}
                            onPress={() => {
                              createPortal(Modal, {
                                defaultVisible: true,
                                title: t<string>("Sure to remove?"),
                                footer: {
                                  actions: ["cancel", "ok"],
                                },
                                onOk: () =>
                                  new Promise((resolve, reject) => {
                                    save(
                                      {
                                        targets: targets.filter((a) => a.path != target),
                                      },
                                      () => {
                                        resolve(undefined);
                                      },
                                    );
                                  }),
                              });
                            }}
                          >
                            <DeleteOutlined className={"text-base"} />
                          </Button>
                        </div>
                      )}
                    </div>
                  );
                })()}
              </TableCell>
              <TableCell align="center">
                <AnimatedArrow direction={"left"} />
              </TableCell>
              <TableCell>
                {/* Source cell content */}
                {i > 0 &&
                  (() => {
                    const s = record.source;
                    const progress = progresses[s];

                    return (
                      <div className={"source"}>
                        <div className="left">
                          <FileSystemSelectorButton
                            fileSystemSelectorProps={{
                              targetType: "folder",
                              onSelected: (e) => {
                                if (!s) {
                                  addSource(record.target, e.path);
                                } else {
                                  updateSource(record.target, s, e.path);
                                }
                              },
                              defaultSelectedPath: record.source,
                            }}
                          />
                          {progress && (
                            <div className={"status"}>
                              {progress.error && (
                                <Button
                                  isIconOnly
                                  color={"danger"}
                                  size={"sm"}
                                  variant={"light"}
                                  onPress={() => {
                                    createPortal(Modal, {
                                      defaultVisible: true,
                                      title: t<string>("Error"),
                                      size: "lg",
                                      children: <pre>{progress.error}</pre>,
                                      footer: {
                                        actions: ["ok"],
                                      },
                                    });
                                  }}
                                >
                                  <MdWarning className={"text-base"} />
                                </Button>
                              )}
                              {progress.moving && <Icon type={"loading"} />}
                              {progress.percentage > 0 &&
                                progress.percentage < 100 &&
                                `${progress.percentage}%`}
                              {progress.percentage == 100 && (
                                <MdFolder
                                  style={{
                                    color: "var(--theme-color-success)",
                                  }}
                                />
                              )}
                            </div>
                          )}
                        </div>
                        {s && (
                          <div className={"flex items-center gap-1"}>
                            {renderCommonOperations(s)}
                            <Button
                              isIconOnly
                              color={"danger"}
                              size={"sm"}
                              variant={"light"}
                              onPress={() => {
                                createPortal(Modal, {
                                  defaultVisible: true,
                                  title: t<string>("Sure to remove?"),
                                  footer: {
                                    actions: ["cancel", "ok"],
                                  },
                                  onOk: () =>
                                    new Promise((resolve, reject) => {
                                      const { target: targetPath } = record;
                                      const target = targets.find((t) => t.path == targetPath);

                                      if (target) {
                                        target.sources = target.sources?.filter((a) => a != s);
                                      }
                                      save(
                                        {
                                          targets,
                                        },
                                        () => {
                                          resolve(undefined);
                                        },
                                      );
                                    }),
                                });
                              }}
                            >
                              <DeleteOutlined className={"text-base"} />
                            </Button>
                          </div>
                        )}
                      </div>
                    );
                  })()}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    );
  };

  return (
    <div className={"file-mover"}>
      <div className="opt">
        <div className="left">
          <div className="enable">
            <div className="label">{t<string>(enabled ? "Enabled" : "Disabled")}</div>
            <Switch
              isSelected={enabled}
              size={"sm"}
              onValueChange={(c) => {
                save({
                  enabled: c,
                });
              }}
            />
          </div>
          <div>
            <TimeInput
              description={t<string>(
                "Files or directories will be moved after the delayed time from the time they are created here. The delay is working for the first layer entries only.",
              )}
              label={t<string>("Delay (minutes)")}
              value={
                value?.delay
                  ? dayjs.duration(moment.duration(value.delay).asMilliseconds())
                  : undefined
              }
              onChange={(c) => {
                save({
                  delay:
                    c?.format && typeof c.format === "function"
                      ? c.format("HH:mm:ss")
                      : (c ?? "00:05:00"),
                });
              }}
            />
          </div>
        </div>
        <div className="right">
          {quickEditModeData && (
            <Button
              color={"primary"}
              size={"sm"}
              onPress={() => {
                const newTargets = quickEditModeData?.filter((d) => !!d.path);

                for (const nt of newTargets) {
                  if (nt.sources) {
                    nt.sources = nt.sources.filter((s) => !!s);
                  }
                }
                save(
                  {
                    targets: newTargets,
                  },
                  () => {
                    setQuickEditModeData(undefined);
                  },
                );
              }}
            >
              {t<string>("Save")}
            </Button>
          )}
          <Button
            size={"sm"}
            onPress={() => {
              if (quickEditModeData) {
                setQuickEditModeData(undefined);
              } else {
                const newData = JSON.parse(JSON.stringify(targets));

                newData.push({});
                setQuickEditModeData(newData);
              }
            }}
          >
            {quickEditModeData
              ? t<string>("Back to normal edit mode")
              : t<string>("Quick edit mode")}
          </Button>
        </div>
      </div>
      {quickEditModeData ? renderQuickEditMode() : renderNormalEditMode()}
    </div>
  );
};

FileMoverPage.displayName = "FileMoverPage";

export default FileMoverPage;

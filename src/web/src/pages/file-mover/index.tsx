"use client";

import React, { useEffect, useState } from "react";
import {
  Dialog,
  Dropdown,
  Icon,
  Input,
  Menu,
  Table,
  TimePicker2,
} from "@alifd/next";
import { useTranslation } from "react-i18next";
import {
  DeleteOutlined,
  FolderOpenOutlined,
  PlusCircleOutlined,
  QuestionCircleOutlined,
} from "@ant-design/icons";

import { toast } from "@/components/bakaui";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";
import "./index.scss";
import CustomIcon from "@/components/CustomIcon";
import AnimatedArrow from "@/components/AnimatedArrow";
import BApi from "@/sdk/BApi";
import { useFileMovingProgressesStore } from "@/models/fileMovingProgresses";
import ClickableIcon from "@/components/ClickableIcon";
import { Button, Checkbox, Switch, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MediaLibraryPathSelectorV2 from "@/components/MediaLibraryPathSelectorV2";

import type { BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptions } from "@/sdk/Api";

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

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [preferredTarget, setPreferredTarget] = useState();
  const [preferredSource, setPreferredSource] = useState();

  const [value, setValue] = useState<IValue>(new Value());

  const [quickEditModeData, setQuickEditModeData] = useState<
    {
      path?: string;
      sources?: string[];
    }[]
  >();

  const progresses = useFileMovingProgressesStore((state) => state.progresses);

  console.log(progresses);

  const loadOptions = () => {
    BApi.options.getFileSystemOptions().then((a) => {
      const fm: BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptions =
        a.data?.fileMover || {};
      const value = {
        delay: fm.delay as string,
        enabled: fm.enabled ?? false,
        targets:
          fm.targets?.map((t) => {
            return {
              path: t.path!,
              sources: t.sources || [],
              overwrite: t.overwrite,
            };
          }) || [],
      };

      // console.log('0-1 options loaded');
      setValue(value);
    });
  };

  useEffect(() => {
    loadOptions();
  }, []);

  const save = (patches = {}, cb?: () => any) => {
    const options = {
      ...(value || {}),
      ...(patches || {}),
    };

    BApi.options
      .patchFileSystemOptions({
        // @ts-ignore
        fileMover: options,
      })
      .then((a) => {
        if (!a.code) {
          toast.success(t<string>("Saved"));
          loadOptions();
          cb && cb();
        }
      });
  };

  const addTarget = (targetPath, cb = () => {}) => {
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

  const updateTarget = (targetPath, newTargetPath, cb = () => {}) => {
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

  const addSource = (targetPath, sourcePath) => {
    const target = targets.find((a) => a.path == targetPath)!;

    if (!target.sources) {
      target.sources = [];
    }
    const { sources = [] } = target;

    if (sources.indexOf(sourcePath) == -1) {
      sources.push(sourcePath);
      save(
        {
          targets,
        },
        () => {
          setPreferredSource(sourcePath);
        },
      );
    } else {
      setPreferredTarget(targetPath);
    }
  };

  const updateSource = (targetPath, sourcePath, newSourcePath) => {
    const sources = targets.find((a) => a.path == targetPath)?.sources || [];
    const idx = sources.indexOf(sourcePath);

    if (idx > -1) {
      if (sources.indexOf(newSourcePath) == -1) {
        sources[idx] = sourcePath;
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

  const ds =
    targets.reduce<Item[]>((s, t) => {
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
          source: undefined,
          overwrite: undefined,
        });
      }

      newArr.sort((a, b) => {
        return (
          (a == preferredSource ? -1 : 0) - (b == preferredSource ? -1 : 0)
        );
      });
      if (t.path == preferredTarget) {
        return newArr.concat(s);
      } else {
        return s.concat(newArr);
      }
    }, []) || [];

  ds.splice(0, 0, {
    rowSpan: 1,
  });

  // console.log(value, ds);

  const renderCommonOperations = (path) => {
    return (
      <>
        <Button isIconOnly size={"sm"} variant={"light"}>
          <FolderOpenOutlined
            className={"text-base"}
            onClick={() => {
              BApi.tool.openFileOrDirectory({
                path,
              });
            }}
          />
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
      <Table
        className={"quick-edit-mode-table"}
        dataSource={quickEditModeData}
        size={"small"}
      >
        <Table.Column
          cell={(path, i, r) => {
            if (i == quickEditModeData!.length - 1) {
              return (
                <Button
                  color={"primary"}
                  size={"sm"}
                  onClick={() => {
                    quickEditModeData?.splice(i, 0, {});
                    setQuickEditModeData([...quickEditModeData!]);
                  }}
                >
                  {t<string>("Add")}
                </Button>
              );
            }
            const target = targets[path];

            return (
              <div className={"target"}>
                <Input
                  placeholder={t<string>("Target path")}
                  value={path}
                  onChange={(v) => {
                    quickEditModeData![i].path = v;
                    setQuickEditModeData([...quickEditModeData!]);
                  }}
                />
                <Button
                  isIconOnly
                  color={"danger"}
                  size={"sm"}
                  variant={"light"}
                >
                  <DeleteOutlined
                    className={"text-base"}
                    onClick={() => {
                      quickEditModeData!.splice(i, 1);
                      setQuickEditModeData([...quickEditModeData!]);
                    }}
                  />
                </Button>
              </div>
            );
          }}
          dataIndex={"path"}
          title={t<string>("Target")}
        />
        <Table.Column
          align={"center"}
          cell={() => {
            return <AnimatedArrow direction={"left"} />;
          }}
          title={t<string>("Moving direction")}
          width={90}
        />
        <Table.Column
          cell={(s, i, r) => {
            if (i == quickEditModeData!.length - 1) {
              return;
            }

            return (
              <Input.TextArea
                autoHeight={{
                  minRows: 2,
                  maxRows: 100,
                }}
                placeholder={t<string>("One path per line")}
                size={"small"}
                value={s?.join("\n")}
                width={"100%"}
                onChange={(v) => {
                  quickEditModeData![i].sources = v.split("\n");
                  setQuickEditModeData([...quickEditModeData!]);
                }}
              />
            );
          }}
          dataIndex={"sources"}
          title={t<string>("Source")}
        />
      </Table>
    );
  };

  const renderNormalEditMode = () => {
    return (
      <Table
        cellProps={(rowIndex, colIndex, dataIndex, record) => {
          if (record.rowSpan && (colIndex == 0 || colIndex == 1)) {
            return {
              rowSpan: record.rowSpan,
            };
          }

          return;
        }}
        dataSource={ds}
        size={"small"}
      >
        <Table.Column
          cell={(target, i, r) => {
            // console.log(`rendering table col ${i}-1`, target, i);
            return (
              <div className={"target"}>
                <div className="left">
                  <Dropdown
                    trigger={
                      <div>
                        <FileSystemSelectorButton
                          fileSystemSelectorProps={{
                            targetType: "folder",
                            onSelected: (e) => {
                              if (!target) {
                                addTarget(e.path);
                              } else {
                                updateTarget(target, e.path);
                              }
                            },
                            defaultSelectedPath: target ?? null,
                          }}
                        >
                          {t<string>("Add target path")}
                        </FileSystemSelectorButton>
                      </div>
                    }
                    triggerType={["hover"]}
                  >
                    <Menu>
                      <Menu.Item
                        onClick={() => {
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
                      </Menu.Item>
                    </Menu>
                  </Dropdown>
                </div>
                {target && (
                  <div className={"flex items-center gap-1"}>
                    <Tooltip
                      content={t<string>("Overwrite files in target path")}
                    >
                      <Checkbox
                        isSelected={r.overwrite}
                        size={"sm"}
                        onValueChange={(v) => setOverwrite(target, v)}
                      >
                        {t<string>("Overwrite")}
                      </Checkbox>
                    </Tooltip>
                    <Tooltip content={t<string>("Add source path")}>
                      <Button isIconOnly size={"sm"} variant={"light"}>
                        <PlusCircleOutlined
                          className={"text-base"}
                          onClick={() => {
                            BApi.gui.openFolderSelector().then((a) => {
                              if (a.data) {
                                addSource(target, a.data);
                              }
                            });
                          }}
                        />
                      </Button>
                    </Tooltip>
                    {renderCommonOperations(target)}
                    <Button
                      isIconOnly
                      color={"danger"}
                      size={"sm"}
                      variant={"light"}
                    >
                      <DeleteOutlined
                        className={"text-base"}
                        onClick={() => {
                          Dialog.confirm({
                            title: t<string>("Sure to remove?"),
                            v2: true,
                            onOk: () =>
                              new Promise((resolve, reject) => {
                                save(
                                  {
                                    targets: targets.filter(
                                      (a) => a.path != target,
                                    ),
                                  },
                                  () => {
                                    resolve(undefined);
                                  },
                                );
                              }),
                          });
                        }}
                      />
                    </Button>
                  </div>
                )}
              </div>
            );
          }}
          dataIndex={"target"}
          title={t<string>("Target")}
        />
        <Table.Column
          align={"center"}
          cell={() => {
            return <AnimatedArrow direction={"left"} />;
          }}
          title={t<string>("Moving direction")}
          width={90}
        />
        <Table.Column
          cell={(s, i, r) => {
            if (i > 0) {
              // console.log(`rendering table col ${i}-2`, s, i);
              const progress = progresses[s];

              return (
                <div className={"source"}>
                  <div className="left">
                    <FileSystemSelectorButton
                      fileSystemSelectorProps={{
                        targetType: "folder",
                        onSelected: (e) => {
                          if (!s) {
                            addSource(r.target, e.path);
                          } else {
                            updateSource(r.target, s, e.path);
                          }
                        },
                      }}
                    />
                    {progress && (
                      <div className={"status"}>
                        {progress.error && (
                          <ClickableIcon
                            colorType={"danger"}
                            type={"warning-circle"}
                            onClick={() => {
                              Dialog.alert({
                                title: t<string>("Error"),
                                v2: true,
                                width: "auto",
                                closeMode: ["esc", "close", "mask"],
                                content: <pre>{progress.error}</pre>,
                              });
                            }}
                          />
                        )}
                        {progress.moving && <Icon type={"loading"} />}
                        {progress.percentage > 0 &&
                          progress.percentage < 100 &&
                          `${progress.percentage}%`}
                        {progress.percentage == 100 && (
                          <CustomIcon
                            style={{ color: "var(--theme-color-success)" }}
                            type={"check-circle"}
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
                      >
                        <DeleteOutlined
                          className={"text-base"}
                          onClick={() => {
                            Dialog.confirm({
                              title: t<string>("Sure to remove?"),
                              v2: true,
                              onOk: () =>
                                new Promise((resolve, reject) => {
                                  const { target: targetPath } = r;
                                  const target = targets.find(
                                    (t) => t.path == targetPath,
                                  )!;

                                  target.sources = target.sources?.filter(
                                    (a) => a != s,
                                  );
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
                        />
                      </Button>
                    </div>
                  )}
                </div>
              );
            }

            return;
          }}
          dataIndex={"source"}
          title={t<string>("Source")}
        />
      </Table>
    );
  };

  return (
    <div className={"file-mover"}>
      <div className="opt">
        <div className="left">
          <div className="enable">
            <div className="label">
              {t<string>(enabled ? "Enabled" : "Disabled")}
            </div>
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
          <div className="delay">
            <Tooltip
              content={t<string>(
                "Files or directories will be moved after the delayed time from the time they are created here. The delay is working for the first layer entries only.",
              )}
              placement={"bottom"}
            >
              <div className={"flex items-center gap-1"}>
                {t<string>("Delay")}
                <QuestionCircleOutlined className={"text-base"} />
              </div>
            </Tooltip>
            <TimePicker2
              value={value?.delay}
              onChange={(c) => {
                save({
                  delay: c?.format("HH:mm:ss") ?? "00:05:00",
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
              onClick={() => {
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
                    loadOptions();
                  },
                );
              }}
            >
              {t<string>("Save")}
            </Button>
          )}
          <Button
            size={"sm"}
            onClick={() => {
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

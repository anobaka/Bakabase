"use client";

import React, { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { DeleteOutlined, FolderOpenOutlined, PlusCircleOutlined, ArrowLeftOutlined } from "@ant-design/icons";
import { MdWarning, MdFolder } from "react-icons/md";
import moment from "moment";
import dayjs from "dayjs";

import {
  Button,
  Checkbox,
  Switch,
  Tooltip,
  Modal,
  Icon,
  Card,
  CardBody,
  TimeInput,
  Chip,
} from "@/components/bakaui";
import { toast } from "@/components/bakaui";
import { FileSystemSelectorButton, FileSystemSelectorModal } from "@/components/FileSystemSelector";

import BApi from "@/sdk/BApi";
import { useFileMovingProgressesStore } from "@/stores/fileMovingProgresses";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
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

  const value = fsOptionsStore.data.fileMover ?? new Value();
  const enabled = value.enabled ?? false;
  const targets = value.targets ?? [];

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

  const addTarget = (targetPath: string) => {
    if (!targets.some((t) => t.path == targetPath)) {
      targets.push({
        path: targetPath,
        sources: [],
        overwrite: false,
      });
      save({ targets });
    }
  };

  const updateTarget = (targetPath: string, newTargetPath: string) => {
    const targetIndex = targets.findIndex((a) => a.path == targetPath);
    const target = targets[targetIndex]!;
    target.path = newTargetPath;
    targets.splice(targetIndex, 1, target);
    save({ targets });
  };

  const addSource = (targetPath: string, sourcePath: string) => {
    const target = targets.find((a) => a.path == targetPath)!;
    if (!target.sources) {
      target.sources = [];
    }
    if (target.sources.indexOf(sourcePath) == -1) {
      target.sources.push(sourcePath);
      save({ targets });
    }
  };

  const updateSource = (targetPath: string, sourcePath: string, newSourcePath: string) => {
    const target = targets.find((a) => a.path == targetPath);
    if (!target || !target.sources) return;

    const idx = target.sources.indexOf(sourcePath);
    if (idx > -1 && target.sources.indexOf(newSourcePath) == -1) {
      target.sources[idx] = newSourcePath;
      save({ targets });
    }
  };

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

  const renderSourceItem = (targetPath: string, sourcePath: string) => {
    const progress = progresses[sourcePath];

    return (
      <div key={sourcePath} className="flex-1 flex items-center justify-between">
        <div className="flex items-center gap-1.5">
          <FileSystemSelectorButton
            fileSystemSelectorProps={{
              targetType: "folder",
              onSelected: (e) => {
                updateSource(targetPath, sourcePath, e.path);
              },
              defaultSelectedPath: sourcePath,
            }}
          />
          {progress && (
            <div className="flex items-center gap-1">
              {progress.error && (
                <Button
                  isIconOnly
                  color="danger"
                  size="sm"
                  variant="light"
                  onPress={() => {
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>("Error"),
                      size: "lg",
                      children: <pre>{progress.error}</pre>,
                      footer: { actions: ["ok"] },
                    });
                  }}
                >
                  <MdWarning className="text-base" />
                </Button>
              )}
              {progress.moving && <Icon type="loading" />}
              {progress.percentage > 0 && progress.percentage < 100 && (
                <Chip size="sm" color="primary" variant="flat">
                  {progress.percentage}%
                </Chip>
              )}
              {progress.percentage === 100 && (
                <MdFolder style={{ color: "var(--theme-color-success)" }} />
              )}
            </div>
          )}
        </div>
        <div className="flex items-center gap-0.5">
          {renderCommonOperations(sourcePath)}
          <Button
            isIconOnly
            color="danger"
            size="sm"
            variant="light"
            onPress={() => {
              const target = targets.find((t) => t.path === targetPath);
              if (target) {
                target.sources = target.sources?.filter((a) => a !== sourcePath);
              }
              save({ targets });
            }}
          >
            <DeleteOutlined className="text-base" />
          </Button>
        </div>
      </div>
    );
  };

  const renderTargetCard = (target: { path: string; sources?: string[]; overwrite: boolean }) => {
    return (
      <Card key={target.path}>
        <CardBody className="flex flex-col gap-1.5 p-2 px-3">
          <div className="flex items-center justify-between pb-1 border-b border-divider">
            <div className="flex items-center gap-2">
              <Button
                color="primary"
                size="sm"
                startContent={<MdFolder className="text-base" />}
                variant="light"
                onPress={() => {
                  createPortal(FileSystemSelectorModal, {
                    targetType: "folder",
                    onSelected: (e: any) => {
                      updateTarget(target.path, e.path);
                    },
                    defaultSelectedPath: target.path,
                  });
                }}
              >
                {target.path}
              </Button>
              <Tooltip content={t<string>("Overwrite files in target path")}>
                <Checkbox
                  isSelected={target.overwrite}
                  size="sm"
                  onValueChange={(v) => setOverwrite(target.path, v)}
                >
                  {t<string>("Overwrite")}
                </Checkbox>
              </Tooltip>
            </div>
            <div className="flex items-center gap-0.5">
              {renderCommonOperations(target.path)}
              <Button
                isIconOnly
                color="danger"
                size="sm"
                variant="light"
                onPress={() => {
                  save({ targets: targets.filter((a) => a.path !== target.path) });
                }}
              >
                <DeleteOutlined className="text-base" />
              </Button>
            </div>
          </div>

          <div>
            <div className="flex items-center justify-between mb-1">
              <span className="flex items-center text-xs text-default-500">
                {t<string>("Sources")}
                {(target.sources ?? []).length > 0 && (
                  <Chip size="sm" variant="flat" className="ml-2">
                    {(target.sources ?? []).length}
                  </Chip>
                )}
              </span>
              <Button
                size="sm"
                color="primary"
                variant="flat"
                startContent={<PlusCircleOutlined className="text-base" />}
                onPress={() => {
                  createPortal(FileSystemSelectorModal, {
                    targetType: "folder",
                    onSelected: (e: any) => {
                      addSource(target.path, e.path);
                    },
                  });
                }}
              >
                {t<string>("Add source")}
              </Button>
            </div>

            {(target.sources ?? []).length > 0 ? (
              <div className="flex flex-col gap-1">
                {(target.sources ?? []).map((sourcePath) => (
                  <div key={sourcePath} className="flex items-center gap-1.5 p-1 px-1.5 bg-default-50 rounded-md">
                    <ArrowLeftOutlined className="text-danger shrink-0" />
                    {renderSourceItem(target.path, sourcePath)}
                  </div>
                ))}
              </div>
            ) : (
              <div className="p-2.5 text-center text-xs text-default-400 bg-default-50 rounded-md">
                {t<string>("No sources configured")}
              </div>
            )}
          </div>
        </CardBody>
      </Card>
    );
  };

  const renderNormalEditMode = () => {
    return (
      <div className="flex flex-col gap-2">
        {(targets ?? []).map((target) => renderTargetCard(target))}

        <Card className="border-2 border-dashed border-default-200 bg-transparent hover:border-primary transition-colors">
          <CardBody className="flex items-center justify-center p-3">
            <Button
              color="primary"
              variant="flat"
              startContent={<PlusCircleOutlined className="text-lg" />}
              onPress={() => {
                createPortal(FileSystemSelectorModal, {
                  targetType: "folder",
                  onSelected: (e: any) => {
                    addTarget(e.path);
                  },
                });
              }}
            >
              {t<string>("Add target path")}
            </Button>
          </CardBody>
        </Card>
      </div>
    );
  };

  return (
    <div className="file-mover">
      <div className="flex items-center gap-3 p-2 px-3 mb-3 bg-default-50 rounded-lg">
        <div className="flex items-center">
          <Switch
            isSelected={enabled}
            size="sm"
            color={enabled ? "success" : "default"}
            onValueChange={(c) => {
              save({ enabled: c });
            }}
          >
            <span className={`text-sm font-medium transition-colors ${enabled ? "text-success" : "text-default-500"}`}>
              {t<string>(enabled ? "Enabled" : "Disabled")}
            </span>
          </Switch>
        </div>

        <div className="w-px h-5 bg-default-200" />

        <div className="flex items-center">
          <Tooltip
            content={t<string>(
              "Files or directories will be moved after the delayed time from the time they are created here. The delay is working for the first layer entries only.",
            )}
          >
            <div className="flex items-center gap-2">
              <span className="text-sm text-default-600 whitespace-nowrap">{t<string>("Delay")}</span>
              <TimeInput
                size="sm"
                hideTimeZone
                granularity="second"
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
          </Tooltip>
        </div>

      </div>
      {renderNormalEditMode()}
    </div>
  );
};

FileMoverPage.displayName = "FileMoverPage";

export default FileMoverPage;

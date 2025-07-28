"use client";

import type { CSSProperties } from "react";

import React from "react";
import { ExclamationCircleOutlined } from "@ant-design/icons";

import { Chip, Input } from "@/components/bakaui";
import FileSystemEntryIcon from "@/components/FileSystemEntryIcon";
import { IconType } from "@/sdk/constants";

type Props = {
  type: "others" | "added" | "deleted" | "default" | "error";
  layer?: number;
  text?: string;
  path?: string;
  editable?: boolean;
  onChange?: (v: string) => any;
  isDirectory?: boolean;
  hideIcon?: boolean;
  className?: string;
};
const FileSystemEntryChangeExampleItem = (props: Props) => {
  const {
    type,
    editable = false,
    layer = 0,
    text = "",
    path,
    onChange,
    isDirectory = false,
    hideIcon,
    className,
  } = props;

  const style: CSSProperties = {
    paddingLeft: `${layer * 24}px`,
  };

  const renderInner = () => {
    switch (type) {
      case "error":
        return (
          <>
            <Chip
              className={"px-0 whitespace-normal h-auto"}
              classNames={{ content: "px-0" }}
              color={"danger"}
              size={"sm"}
              variant={"light"}
            >
              <div className={"flex items-center gap-2"}>
                {!hideIcon && (
                  <ExclamationCircleOutlined style={{ fontSize: "18px" }} />
                )}
                {text}
              </div>
            </Chip>
          </>
        );
      case "others":
        return (
          <>
            <FileSystemEntryIcon size={20} type={IconType.UnknownFile} />
            <Chip
              className={"px-0 whitespace-break-spaces"}
              classNames={{ content: "px-0" }}
              size={"sm"}
              variant={"light"}
            >
              {text}
            </Chip>
          </>
        );
      case "added": {
        if (editable) {
          return (
            <>
              <Chip
                className={"px-0"}
                classNames={{ content: "px-0" }}
                color={"success"}
                size={"sm"}
                variant={"light"}
              >
                <FileSystemEntryIcon size={20} type={IconType.Directory} />
              </Chip>
              <Input
                radius={"none"}
                // classNames={{
                //   inputWrapper: 'px-0',
                // }}
                defaultValue={text}
                size={'sm'}
                onValueChange={v => {
                  onChange?.(v);
                }}
              />
            </>
          );
        } else {
          return (
            <>
              <Chip
                className={"px-0"}
                classNames={{ content: "px-0" }}
                color={"success"}
                size={"sm"}
                variant={"light"}
              >
                <div className={"flex items-center gap-2"}>
                  {!hideIcon && (
                    <FileSystemEntryIcon
                      path={path}
                      size={20}
                      type={
                        isDirectory
                          ? IconType.Directory
                          : path
                            ? IconType.Dynamic
                            : IconType.UnknownFile
                      }
                    />
                  )}
                  {text}
                </div>
              </Chip>
            </>
          );
        }
      }
      case "deleted":
        return (
          <>
            <Chip
              className={"line-through px-0 whitespace-normal h-auto"}
              classNames={{ content: "px-0" }}
              color={"danger"}
              size={"sm"}
              variant={"light"}
            >
              <div className={"flex items-center gap-2"}>
                {!hideIcon && (
                  <FileSystemEntryIcon
                    path={path}
                    size={20}
                    type={
                      isDirectory
                        ? IconType.Directory
                        : path
                          ? IconType.Dynamic
                          : IconType.UnknownFile
                    }
                  />
                )}
                {text}
              </div>
            </Chip>
          </>
        );
      case "default":
        return (
          <>
            <FileSystemEntryIcon size={20} type={IconType.Directory} />
            {text}
          </>
        );
    }
  };

  return (
    <div className={`${className} flex items-center gap-2`} style={style}>
      {renderInner()}
    </div>
  );
};

FileSystemEntryChangeExampleItem.displayName =
  "FileSystemEntryChangeExampleItem";

export default FileSystemEntryChangeExampleItem;

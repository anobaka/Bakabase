"use client";

import type { ValueRendererProps } from "../models";

import { useTranslation } from "react-i18next";
import {
  DeleteOutlined,
  LoadingOutlined,
  UploadOutlined,
  FolderOpenOutlined,
  PlusOutlined,
} from "@ant-design/icons";
import { Img } from "react-image";
import React, { useRef, useState } from "react";
import { AiOutlineFile } from "react-icons/ai";

import envConfig from "@/config/env";
import NotSet, { LightText } from "@/components/StandardValue/ValueRenderer/Renderers/components/LightText";
import { Button, Popover } from "@/components/bakaui";
import { splitPathIntoSegments } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import BApi from "@/sdk/BApi";

type AttachmentValueRendererProps = Omit<
  ValueRendererProps<string[]>,
  "variant"
> & {
  variant: ValueRendererProps<string[]>["variant"];
  size?: "sm" | "md" | "lg";
};

const AttachmentValueRenderer = ({
  value,
  variant,
  editor,
  size,
  isReadonly: propsIsReadonly,
  isEditing: controlledIsEditing,
  defaultEditing = false,
  ...props
}: AttachmentValueRendererProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [addPopoverOpen, setAddPopoverOpen] = useState(false);

  // Internal editing state for uncontrolled mode
  const [internalIsEditing, setInternalIsEditing] = useState(defaultEditing);

  // Use controlled value if provided, otherwise use internal state
  const isEditing = controlledIsEditing ?? internalIsEditing;

  // Default isReadonly to false
  const isReadonly = propsIsReadonly ?? false;

  const v = variant ?? "default";
  const canEdit = !isReadonly && editor;

  const handleClick = () => {
    if (controlledIsEditing === undefined && !isEditing && canEdit) {
      setInternalIsEditing(true);
    }
  };

  const openFile = (path: string) => {
    BApi.tool.openFileOrDirectory({ path });
  };

  const handleFileUpload = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;

    setIsUploading(true);
    try {
      const formData = new FormData();
      formData.append("file", file);

      const response = await fetch(`${envConfig.apiEndpoint}/file/upload`, {
        method: "POST",
        body: formData,
      });

      if (response.ok) {
        const result = await response.json();
        if (result.data) {
          const newValue = (value ?? []).concat([result.data]);
          editor?.onValueChange?.(newValue, newValue);
        }
      }
    } finally {
      setIsUploading(false);
      setAddPopoverOpen(false);
      if (fileInputRef.current) {
        fileInputRef.current.value = "";
      }
    }
  };

  const openFileSystemSelector = () => {
    setAddPopoverOpen(false);
    createPortal(FileSystemSelectorModal, {
      targetType: "file",
      onSelected: (entry) => {
        const newValue = (value ?? []).concat([entry.path]);
        editor?.onValueChange?.(newValue, newValue);
      },
    });
  };

  const triggerFileUpload = () => {
    fileInputRef.current?.click();
  };

  const removeFile = (path: string) => {
    const newValue = (value ?? []).filter((v) => v !== path);
    editor?.onValueChange?.(newValue, newValue);
  };

  const getFileName = (path: string) => {
    const segments = splitPathIntoSegments(path);
    return segments[segments.length - 1] || path;
  };

  // Size mappings
  const sizeConfig = {
    sm: { thumbnail: 60, iconSize: "text-2xl", gap: 1 },
    md: { thumbnail: 80, iconSize: "text-3xl", gap: 2 },
    lg: { thumbnail: 100, iconSize: "text-4xl", gap: 2 },
  };
  const config = sizeConfig[size ?? "md"];

  // Shared popover content for adding files
  const renderAddPopoverContent = () => (
    <div className="flex flex-col gap-1 p-1">
      <Button
        size="sm"
        variant="light"
        startContent={<FolderOpenOutlined />}
        className="justify-start"
        onClick={openFileSystemSelector}
      >
        {t("property.attachment.selectFromFileSystem")}
      </Button>
      <Button
        size="sm"
        variant="light"
        startContent={isUploading ? <LoadingOutlined /> : <UploadOutlined />}
        className="justify-start"
        onClick={triggerFileUpload}
        isDisabled={isUploading}
      >
        {t("property.attachment.uploadFromLocal")}
      </Button>
      <div className="text-xs text-default-400 px-2">
        {t("property.attachment.uploadHint")}
      </div>
    </div>
  );

  const renderAddButton = (lightVariant = false) => {
    if (!canEdit) return null;

    const trigger = lightVariant ? (
      <Button
        isIconOnly
        color="primary"
        size="sm"
        variant="light"
        className="min-w-0 w-5 h-5"
      >
        <PlusOutlined className="text-xs" />
      </Button>
    ) : (
      <Button
        isIconOnly
        color="primary"
        size={size}
        variant="flat"
        className="min-w-0"
        style={{
          width: config.thumbnail,
          height: config.thumbnail,
        }}
      >
        <PlusOutlined className={config.iconSize} />
      </Button>
    );

    return (
      <>
        <input
          ref={fileInputRef}
          type="file"
          className="hidden"
          onChange={handleFileUpload}
        />
        <Popover
          isOpen={addPopoverOpen}
          onOpenChange={setAddPopoverOpen}
          trigger={trigger}
        >
          {renderAddPopoverContent()}
        </Popover>
      </>
    );
  };

  const renderAttachment = (path: string, showDelete: boolean) => {
    const fileName = getFileName(path);

    return (
      <div
        key={path}
        className="flex flex-col items-center gap-1 relative group"
        style={{ maxWidth: config.thumbnail }}
      >
        <div
          className="cursor-pointer relative"
          onClick={() => openFile(path)}
          title={path}
        >
          <Img
            alt={fileName}
            loader={<LoadingOutlined className={config.iconSize} />}
            src={[
              `${envConfig.apiEndpoint}/tool/thumbnail?path=${encodeURIComponent(path)}`,
            ]}
            style={{
              maxWidth: config.thumbnail,
              maxHeight: config.thumbnail,
              objectFit: "cover",
            }}
            unloader={
              <div
                className="flex items-center justify-center bg-default-100 rounded"
                style={{
                  width: config.thumbnail,
                  height: config.thumbnail,
                }}
              >
                <AiOutlineFile className={config.iconSize} />
              </div>
            }
          />
        </div>
        <span
          className="text-xs text-default-500 truncate w-full text-center"
          title={fileName}
        >
          {fileName}
        </span>
        {showDelete && (
          <Button
            isIconOnly
            color="danger"
            size="sm"
            variant="flat"
            className="absolute -top-1 -right-1 min-w-0 w-5 h-5 opacity-0 group-hover:opacity-100 transition-opacity"
            onClick={(e) => {
              e.stopPropagation();
              removeFile(path);
            }}
          >
            <DeleteOutlined className="text-xs" />
          </Button>
        )}
      </div>
    );
  };

  // Default variant
  if (v === "default") {
    const showEditControls = isEditing === true || (controlledIsEditing === undefined && !!canEdit);

    if (!value || value.length === 0) {
      // If readonly, always show NotSet regardless of isEditing
      if (isReadonly) {
        return <NotSet size={size} />;
      }
      if (showEditControls) {
        return renderAddButton();
      }
      // Only show click handler if has editor
      return <NotSet size={size} onClick={canEdit ? handleClick : undefined} />;
    }

    return (
      <div className={`flex items-start gap-${config.gap} flex-wrap`}>
        {value.map((path) => renderAttachment(path, showEditControls))}
        {showEditControls && renderAddButton()}
      </div>
    );
  }

  // Light variant
  if (v === "light") {
    const showEditControls = isEditing === true || (controlledIsEditing === undefined && !!canEdit);

    if (!value || value.length === 0) {
      // If readonly, always show NotSet without click handler
      if (isReadonly) {
        return <NotSet size={size} />;
      }
      // Light variant always uses text style - show "click to set" with popover for adding
      if (showEditControls) {
        return (
          <>
            <input
              ref={fileInputRef}
              type="file"
              className="hidden"
              onChange={handleFileUpload}
            />
            <Popover
              isOpen={addPopoverOpen}
              onOpenChange={setAddPopoverOpen}
              trigger={
                <span className="cursor-pointer">
                  <NotSet size={size} onClick={() => {}} />
                </span>
              }
            >
              {renderAddPopoverContent()}
            </Popover>
          </>
        );
      }
      // Only show click handler if has editor
      return <NotSet size={size} onClick={canEdit ? handleClick : undefined} />;
    }

    // Render filename with optional popover for editing
    const renderLightFilename = (path: string) => {
      const fileName = getFileName(path);
      const filenameSpan = (
        <span
          className={`cursor-pointer hover:underline ${showEditControls ? "text-primary" : ""}`}
          onClick={() => openFile(path)}
        >
          {fileName}
        </span>
      );

      if (showEditControls) {
        return (
          <Popover
            key={path}
            trigger={filenameSpan}
          >
            <div className="flex flex-col gap-1 p-1">
              <Button
                size="sm"
                variant="light"
                color="danger"
                startContent={<DeleteOutlined />}
                className="justify-start"
                onClick={() => removeFile(path)}
              >
                {t("Delete")}
              </Button>
              <div className="border-t border-default-200 my-1" />
              <Button
                size="sm"
                variant="light"
                startContent={<FolderOpenOutlined />}
                className="justify-start"
                onClick={openFileSystemSelector}
              >
                {t("property.attachment.selectFromFileSystem")}
              </Button>
              <Button
                size="sm"
                variant="light"
                startContent={isUploading ? <LoadingOutlined /> : <UploadOutlined />}
                className="justify-start"
                onClick={triggerFileUpload}
                isDisabled={isUploading}
              >
                {t("property.attachment.uploadFromLocal")}
              </Button>
            </div>
          </Popover>
        );
      }

      return <span key={path}>{filenameSpan}</span>;
    };

    return (
      <LightText size={size}>
        <>
          <input
            ref={fileInputRef}
            type="file"
            className="hidden"
            onChange={handleFileUpload}
          />
          {value.map((path, i) => (
            <span key={path}>
              {i !== 0 && ", "}
              {renderLightFilename(path)}
            </span>
          ))}
        </>
      </LightText>
    );
  }

  return null;
};

AttachmentValueRenderer.displayName = "AttachmentValueRenderer";

export default AttachmentValueRenderer;

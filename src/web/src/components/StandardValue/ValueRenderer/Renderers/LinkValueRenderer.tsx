"use client";

import type { ValueRendererProps } from "../models";
import type { LinkValue } from "../../models";

import { EditOutlined, FileTextOutlined, LinkOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { useState } from "react";

import ExternalLink from "@/components/ExternalLink";
import { Button, Input, Popover } from "@/components/bakaui";
import NotSet from "@/components/StandardValue/ValueRenderer/Renderers/components/LightText";

type LinkValueRendererProps = ValueRendererProps<LinkValue> & {
  size?: "sm" | "md" | "lg";
};
const LinkValueRenderer = ({
  value,
  editor,
  variant,
  size,
  isReadonly: propsIsReadonly,
  isEditing,
  ...props
}: LinkValueRendererProps) => {
  const { t } = useTranslation();
  const [editingValue, setEditingValue] = useState<LinkValue>();

  // Default isReadonly to false
  const isReadonly = propsIsReadonly ?? false;

  // Don't show editor if isEditing is explicitly set to false or if readonly
  const canEdit = !isReadonly && editor && isEditing !== false;

  const renderInner = () => {
    if (value?.url) {
      return (
        <ExternalLink className="px-3" size={size} href={value.url}>{value.text ?? value.url}</ExternalLink>
      );
    } else {
      if (value?.text != undefined && value.text.length > 0) {
        return <span>{value.text}</span>;
      }
    }

    return null;
  };

  const inner = renderInner();

  // Show NotSet when no value and not in editable mode
  if (!inner && !canEdit) {
    return <NotSet size={size} />;
  }

  if (canEdit) {
    return (
      <span className={"flex items-center gap-2"}>
        {inner}
        <Popover
          isOpen={!!editingValue}
          trigger={
            <Button isIconOnly size={"sm"}>
              <EditOutlined className={"text-base"} />
            </Button>
          }
          onOpenChange={(isOpen) => {
            if (isOpen) {
              setEditingValue({ ...value });
            } else {
              setEditingValue(undefined);
            }
          }}
        >
          <div className={"flex flex-col gap-3 min-w-[320px] p-1"}>
            <Input
              placeholder={t<string>("Text")}
              size={size}
              variant="bordered"
              startContent={<FileTextOutlined className="text-default-400" />}
              value={editingValue?.text}
              onValueChange={(text) => {
                setEditingValue({
                  ...editingValue,
                  text,
                });
              }}
            />
            <Input
              placeholder={t<string>("Link")}
              size={size}
              variant="bordered"
              startContent={<LinkOutlined className="text-default-400" />}
              value={editingValue?.url}
              onValueChange={(url) => {
                setEditingValue({
                  ...editingValue,
                  url,
                });
              }}
            />
            <div className={"flex items-center gap-2 justify-end"}>
              <Button
                size={size}
                variant="light"
                onClick={() => {
                  setEditingValue(undefined);
                }}
              >
                {t<string>("Cancel")}
              </Button>
              <Button
                color={"primary"}
                size={size}
                onClick={() => {
                  editor?.onValueChange?.(editingValue, editingValue);
                  setEditingValue(undefined);
                }}
              >
                {t<string>("Submit")}
              </Button>
            </div>
          </div>
        </Popover>
      </span>
    );
  } else {
    return inner;
  }
};

LinkValueRenderer.displayName = "LinkValueRenderer";

export default LinkValueRenderer;

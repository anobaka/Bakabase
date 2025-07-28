"use client";

import type { ValueRendererProps } from "../models";
import type { LinkValue } from "../../models";

import { EditOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { useState } from "react";

import ExternalLink from "@/components/ExternalLink";
import { Button, Input, Popover } from "@/components/bakaui";

type LinkValueRendererProps = ValueRendererProps<LinkValue> & {};
const LinkValueRenderer = ({
  value,
  editor,
  variant,
  ...props
}: LinkValueRendererProps) => {
  const { t } = useTranslation();
  const [editingValue, setEditingValue] = useState<LinkValue>();

  const renderInner = () => {
    if (value?.url) {
      return (
        <ExternalLink href={value.url}>{value.text ?? value.url}</ExternalLink>
      );
    } else {
      if (value?.text != undefined && value.text.length > 0) {
        return <span>{value.text}</span>;
      }
    }

    return null;
  };

  const inner = renderInner();

  if (editor) {
    return (
      <span className={"flex items-center gap-2"}>
        {inner}
        <Popover
          isKeyboardDismissDisabled
          isOpen={!!editingValue}
          shouldCloseOnBlur={false}
          trigger={
            <Button isIconOnly size={"sm"}>
              <EditOutlined className={"text-base"} />
            </Button>
          }
          onOpenChange={(isOpen) => {
            if (isOpen) {
              setEditingValue({ ...value });
            }
          }}
        >
          <div className={"flex flex-col gap-1"}>
            <Input
              label={t<string>("Text")}
              size={"sm"}
              value={editingValue?.text}
              onValueChange={(text) => {
                setEditingValue({
                  ...editingValue,
                  text,
                });
              }}
            />
            <Input
              label={t<string>("Link")}
              size={"sm"}
              value={editingValue?.url}
              onValueChange={(url) => {
                setEditingValue({
                  ...editingValue,
                  url,
                });
              }}
            />
            <div className={"flex items-center gap-2 justify-center"}>
              <Button
                color={"primary"}
                size={"sm"}
                onClick={() => {
                  editor?.onValueChange?.(editingValue, editingValue);
                  setEditingValue(undefined);
                }}
              >
                {t<string>("Submit")}
              </Button>
              <Button
                size={"sm"}
                onClick={() => {
                  setEditingValue(undefined);
                }}
              >
                {t<string>("Cancel")}
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

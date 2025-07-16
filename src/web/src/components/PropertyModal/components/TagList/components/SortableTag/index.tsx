"use client";

import type { Tag } from "@/components/Property/models";

import React, { useEffect, useState } from "react";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";
import {
  DeleteOutlined,
  EyeInvisibleOutlined,
  EyeOutlined,
} from "@ant-design/icons";

import DragHandle from "@/components/DragHandle";
import { Button, ColorPicker, Input, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { buildColorValueString } from "@/components/bakaui/components/ColorPicker";
import colors from "@/components/bakaui/colors";

interface IProps {
  id: string;
  tag: Tag;
  onRemove?: (choice: Tag) => any;
  onChange?: (choice: Tag) => any;
  style?: any;
  checkUsage?: (value: string) => Promise<number>;
}

export function SortableTag({
  id,
  tag: propsTag,
  onRemove,
  onChange,
  style: propsStyle,
  checkUsage,
}: IProps) {
  const { t } = useTranslation();
  const { attributes, listeners, setNodeRef, transform, transition } =
    useSortable({ id: id });
  const { createPortal } = useBakabaseContext();

  const [tag, setTag] = useState(propsTag);

  useEffect(() => {
    // console.log(9999, 'new rendering');
  }, []);

  useUpdateEffect(() => {
    onChange?.(tag);
  }, [tag]);

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    ...propsStyle,
  };

  return (
    <div ref={setNodeRef} className={"flex gap-1 items-center"} style={style}>
      <DragHandle {...listeners} {...attributes} />
      <ColorPicker
        color={tag.color ?? colors.color}
        onChange={(color) => {
          setTag({
            ...tag,
            color: buildColorValueString(color),
          });
        }}
      />
      <Input
        placeholder={t<string>("Group of tag, optional")}
        size={"sm"}
        value={tag?.group}
        onValueChange={(group) => {
          setTag({
            ...tag,
            group,
          });
        }}
      />
      <Input
        placeholder={t<string>("Name of tag, required")}
        size={"sm"}
        value={tag?.name}
        onValueChange={(name) => {
          setTag({
            ...tag,
            name,
          });
        }}
      />
      <div className={"flex items-center"}>
        <Button
          isIconOnly
          radius={"sm"}
          size={"sm"}
          title={t<string>("Hide in view")}
          variant={"light"}
          onClick={() => {
            setTag({
              ...tag,
              hide: !tag.hide,
            });
          }}
        >
          {tag.hide ? (
            <EyeInvisibleOutlined className={"text-base"} />
          ) : (
            <EyeOutlined className={"text-base"} />
          )}
        </Button>
        <Button
          isIconOnly
          color={"danger"}
          radius={"sm"}
          size={"sm"}
          variant={"light"}
          onClick={async () => {
            if (checkUsage) {
              const count = await checkUsage(tag.value);

              if (count > 0) {
                createPortal(Modal, {
                  defaultVisible: true,
                  size: "sm",
                  title: t<string>(
                    "Value is being referenced in {{count}} places",
                    { count },
                  ),
                  children: t<string>("Sure to delete?"),
                  onOk: async () => {
                    onRemove?.(tag);
                  },
                });

                return;
              }
            }
            onRemove?.(tag);
          }}
        >
          <DeleteOutlined className={"text-base"} />
        </Button>
      </div>
    </div>
  );
}

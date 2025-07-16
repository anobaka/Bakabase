"use client";

import type { IChoice } from "@/components/Property/models";

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
  choice: IChoice;
  onRemove?: (choice: IChoice) => any;
  onChange?: (choice: IChoice) => any;
  style?: any;
  checkUsage?: (value: string) => Promise<number>;
  onEnterKeyDown?: () => any;
}

export function SortableChoice({
  id,
  choice: propsChoice,
  onRemove,
  onChange,
  style: propsStyle,
  checkUsage,
  onEnterKeyDown,
}: IProps) {
  const { t } = useTranslation();
  const { attributes, listeners, setNodeRef, transform, transition } =
    useSortable({ id: id });
  const { createPortal } = useBakabaseContext();

  const [choice, setChoice] = useState(propsChoice);

  useEffect(() => {}, []);

  useUpdateEffect(() => {
    onChange?.(choice);
  }, [choice]);

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    ...propsStyle,
  };

  return (
    <div ref={setNodeRef} className={"flex gap-1 items-center"} style={style}>
      <DragHandle {...listeners} {...attributes} />
      <ColorPicker
        color={choice.color ?? colors.color}
        onChange={(color) => {
          setChoice({
            ...choice,
            color: buildColorValueString(color),
          });
        }}
      />
      <Input
        autoFocus={!choice.label}
        size={"sm"}
        value={choice?.label}
        onKeyDown={(e) => {
          if (e.key === "Enter") {
            onEnterKeyDown?.();
          }
        }}
        onValueChange={(label) => {
          setChoice({
            ...choice,
            label,
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
            setChoice({
              ...choice,
              hide: !choice.hide,
            });
          }}
        >
          {choice.hide ? (
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
              const count = await checkUsage(choice.value);

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
                    onRemove?.(choice);
                  },
                });

                return;
              }
            }
            onRemove?.(choice);
          }}
        >
          <DeleteOutlined className={"text-base"} />
        </Button>
      </div>
    </div>
  );
}

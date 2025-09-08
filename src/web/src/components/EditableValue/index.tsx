"use client";

import type { ReactNode } from "react";

import { AiOutlineCheck, AiOutlineClose, AiOutlineEdit } from "react-icons/ai";
import React, { useState } from "react";

import { Button } from "@/components/bakaui";
import { isPromise } from "@/components/utils";

// export type SimpleEditableValueProps<TValue> = {
//   onSubmit?: (value?: TValue) => any | Promise<any>;
// };

type ComponentCommonProps<TValue> = {
  value?: TValue;
  label?: ReactNode;
  description?: ReactNode;
};

type ViewerProps<TValue> = ComponentCommonProps<TValue> & {
  isReadOnly?: boolean;
};

type EditorProps<TValue> = ComponentCommonProps<TValue> & {
  onValueChange?: (value: TValue) => void;
};

type Props<
  TValue,
  TEditorProps extends EditorProps<TValue>,
  TViewerProps extends ViewerProps<TValue> = TEditorProps,
> = {
  Viewer: React.FC<TViewerProps>;
  Editor: React.FC<TEditorProps>;
  viewerProps?: Omit<TViewerProps, keyof ComponentCommonProps<TValue>>;
  editorProps?: Omit<TEditorProps, keyof ComponentCommonProps<TValue>>;
  onSubmit?: (value?: TValue) => any | Promise<any>;
  className?: string;
  trigger?: "viewer" | "edit-button",
} & ComponentCommonProps<TValue>;

// & SimpleEditableValueProps<TValue> & EditorProps<TValue> & ViewProps<TValue>;

function EditableValue<
  TValue,
  TEditorProps extends EditorProps<TValue>,
  TViewerProps extends ViewerProps<TValue> = TEditorProps,
>({
  onSubmit,
  Viewer,
  viewerProps,
  Editor,
  editorProps,
  className,
  trigger = "edit-button",
  ...commonProps
}: Props<TValue, TEditorProps, TViewerProps>) {
  const [editing, setEditing] = useState(false);
  const [editingValue, setEditingValue] = useState<TValue>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  console.log(Viewer, viewerProps, commonProps);

  return (
    <div className={`flex items-center gap-2 ${className}`}>
      {editing ? (
        <>
          <Editor
            {...(editorProps as unknown as TEditorProps)}
            {...commonProps}
            value={editingValue}
            onValueChange={(v) => setEditingValue(v)}
          />
          <Button
            isIconOnly
            color={"success"}
            isLoading={isSubmitting}
            size={"sm"}
            variant={"light"}
            onPress={() => {
              const func = onSubmit?.(editingValue);

              if (isPromise(func)) {
                setIsSubmitting(true);
                func.finally(() => {
                  setIsSubmitting(false);
                  setEditing(false);
                });
              }
            }}
          >
            <AiOutlineCheck className={"text-base"} />
          </Button>
          <Button
            isIconOnly
            color={"danger"}
            size={"sm"}
            variant={"light"}
            onPress={() => {
              setEditing(false);
            }}
          >
            <AiOutlineClose className={"text-base"} />
          </Button>
        </>
      ) : (
        <>
          <Viewer
            {...(viewerProps as unknown as TViewerProps)}
            {...commonProps}
            isReadOnly
            onClick={() => {
              if (trigger == "viewer") {
                setEditingValue(commonProps.value);
                setEditing(true);
              }
            }}
          />
          {trigger == "edit-button" && (
            <Button
              isIconOnly
              size={"sm"}
              variant={"light"}
              onPress={() => {
                setEditingValue(commonProps.value);
                setEditing(true);
              }}
            >
              <AiOutlineEdit className={"text-base"} />
            </Button>
          )}
        </>
      )}
    </div>
  );
}

export { EditableValue };

"use client";

import type { ValueRendererProps } from "../models";

import { useTranslation } from "react-i18next";
import { useState } from "react";
import { useUpdateEffect } from "react-use";

import NotSet from "./components/NotSet";

import { Input, Textarea } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";

type StringValueRendererProps = ValueRendererProps<string> & {
  multiline?: boolean;
  size?: "sm" | "md" | "lg";
};

const log = buildLogger("StringValueRenderer");
const StringValueRenderer = (props: StringValueRendererProps) => {
  const {
    value: propsValue,
    multiline,
    variant,
    editor,
    size,
    isReadonly: propsIsReadonly,
    defaultEditing,
    isEditing,
  } = props;
  const { t } = useTranslation();
  // Start in editing mode if defaultEditing is true and not readonly
  const [editing, setEditing] = useState(defaultEditing && !propsIsReadonly && !!editor);
  const [value, setValue] = useState(propsValue);

  // Default isReadonly to true if no editor is provided
  const isReadonly = propsIsReadonly ?? !editor;

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  const startEditing = !isReadonly && editor
    ? () => {
      log("Start editing");
      setEditing(true);
    }
    : undefined;

  // Direct editing mode: always show input/textarea without toggle
  if (isEditing && !isReadonly && editor) {
    const handleChange = (newValue: string) => {
      setValue(newValue);
      editor.onValueChange?.(newValue, newValue);
    };

    if (multiline) {
      return <Textarea size={size} value={value ?? ""} minRows={2} onValueChange={handleChange} />;
    } else {
      return <Input size={size} value={value ?? ""} onValueChange={handleChange} />;
    }
  }

  if (!editing) {
    if (value == undefined || value.length == 0) {
      return <NotSet onClick={startEditing} />;
    }
  }

  const completeEditing = () => {
    editor?.onValueChange?.(value, value);
    setEditing(false);
  };

  if (variant == "light" && !editing) {
    return (
      <span onClick={startEditing} className={startEditing ? "cursor-pointer" : undefined}>
        {value}
      </span>
    );
  }

  if (editing) {
    if (multiline) {
      return <Textarea size={size} minRows={2} autoFocus value={value} onBlur={completeEditing} onValueChange={setValue} />;
    } else {
      return <Input size={size} autoFocus value={value} onBlur={completeEditing} onValueChange={setValue} />;
    }
  } else {
    if (multiline) {
      return (
        <pre
          dangerouslySetInnerHTML={{ __html: value! }}
          className={`whitespace-pre-wrap ${startEditing ? "cursor-pointer" : ""}`}
          onClick={startEditing}
        />
      );
    } else {
      return (
        <span onClick={startEditing} className={startEditing ? "cursor-pointer" : undefined}>
          {value}
        </span>
      );
    }
  }
};

StringValueRenderer.displayName = "StringValueRenderer";

export default StringValueRenderer;

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
};

const log = buildLogger("StringValueRenderer");
const StringValueRenderer = (props: StringValueRendererProps) => {
  const { value: propsValue, multiline, variant, editor } = props;
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(propsValue);

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  const startEditing = editor
    ? () => {
        log("Start editing");
        setEditing(true);
      }
    : undefined;

  if (!editing) {
    if (value == undefined || value.length == 0) {
      return <NotSet onClick={startEditing} />;
    }
  }

  log(props);

  const completeEditing = () => {
    editor?.onValueChange?.(value, value);
    setEditing(false);
  };

  if (variant == "light" && !editing) {
    return <span onClick={startEditing}>{value}</span>;
  }

  if (editing) {
    if (multiline) {
      return (
        <Textarea
          autoFocus
          value={value}
          onBlur={completeEditing}
          onValueChange={setValue}
        />
      );
    } else {
      return (
        <Input
          autoFocus
          value={value}
          onBlur={completeEditing}
          onValueChange={setValue}
        />
      );
    }
  } else {
    if (multiline) {
      return (
        <pre
          dangerouslySetInnerHTML={{ __html: value! }}
          className={"whitespace-pre-wrap"}
          onClick={startEditing}
        />
      );
    } else {
      return <span onClick={startEditing}>{value}</span>;
    }
  }
};

StringValueRenderer.displayName = "StringValueRenderer";

export default StringValueRenderer;

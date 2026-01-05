"use client";

import type { Dayjs } from "dayjs";
import type { ValueRendererProps } from "../models";

import { useEffect, useState } from "react";

import { DateInput } from "@/components/bakaui";
import NotSet from "@/components/StandardValue/ValueRenderer/Renderers/components/NotSet";
import { buildLogger } from "@/components/utils";

type DateTimeValueRendererProps = ValueRendererProps<Dayjs> & {
  format?: string;
  as: "datetime" | "date";
  size?: "sm" | "md" | "lg";
};

const log = buildLogger("DateTimeValueRenderer");
const DateTimeValueRenderer = (props: DateTimeValueRendererProps) => {
  const { value: propsValue, format, as, variant, editor, size, isEditing, isReadonly: propsIsReadonly } = props;

  log(props);
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(propsValue);

  // Default isReadonly to true if no editor is provided
  const isReadonly = propsIsReadonly ?? !editor;

  useEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  const startEditing = !isReadonly && editor
    ? () => {
        setEditing(true);
      }
    : undefined;

  const f =
    format == undefined
      ? as == "datetime"
        ? "YYYY-MM-DD HH:mm:ss"
        : "YYYY-MM-DD"
      : format;

  // Direct editing mode: always show date picker without toggle
  if (isEditing && !isReadonly && editor) {
    return (
      <DateInput
        granularity={as == "datetime" ? "second" : "day"}
        isReadOnly={false}
        value={value}
        size={size}
        onChange={(d) => {
          log("OnChange", d);
          setValue(d);
          editor.onValueChange?.(d, d);
        }}
      />
    );
  }

  if (!editing) {
    if (value == undefined) {
      return <NotSet onClick={startEditing} />;
    }

    return <span onClick={startEditing} className={startEditing ? "cursor-pointer" : undefined}>{value?.format(f)}</span>;
  }

  // console.log(as);

  return (
    <DateInput
      granularity={as == "datetime" ? "second" : "day"}
      isReadOnly={!editor}
      value={value}
      size={size}
      onBlur={() => {
        log("onBlur", value);
        editor?.onValueChange?.(value);
        setEditing(false);
      }}
      onChange={(d) => {
        log("OnChange", d);
        setValue(d);
      }}
      onKeyDown={(e) => {
        if (e.key == "Enter") {
          log("onEnter", value);
          editor?.onValueChange?.(value, value);
          setEditing(false);
        }
      }}
    />
  );
};

DateTimeValueRenderer.displayName = "DateTimeValueRenderer";

export default DateTimeValueRenderer;

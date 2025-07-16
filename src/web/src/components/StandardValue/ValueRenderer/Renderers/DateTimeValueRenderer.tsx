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
};

const log = buildLogger("DateTimeValueRenderer");

export default (props: DateTimeValueRendererProps) => {
  const { value: propsValue, format, as, variant, editor } = props;

  log(props);
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(propsValue);

  useEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  const startEditing = editor
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

  if (!editing) {
    if (value == undefined) {
      return <NotSet onClick={startEditing} />;
    }

    return <span onClick={startEditing}>{value?.format(f)}</span>;
  }

  // console.log(as);

  return (
    <DateInput
      granularity={as == "datetime" ? "second" : "day"}
      isReadOnly={!editor}
      value={value}
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

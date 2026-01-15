"use client";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";

import { Chip, Textarea } from "@/components/bakaui";

type ExtensionsInputProps = {
  defaultValue?: string[];
  onValueChange?: (extensions: string[]) => void;
  label?: string;
  placeholder?: string;
  minRows?: number;
  size?: "sm" | "md" | "lg";
};

function extractExtensions(text: string): string[] {
  return Array.from(
    new Set(
      text
        .replace(/\n/g, " ")
        .split(" ")
        .map((x) => {
          let trimmed = x.trim();

          while (true) {
            const n = trimmed.replace(/^\.+|\.+$/g, "");

            if (n.length == trimmed.length) {
              break;
            } else {
              trimmed = n;
            }
          }

          return trimmed;
        })
        .filter((x) => x.length > 0)
        .map((x) => `.${x}`),
    ),
  );
}

const ExtensionsInput: React.FC<ExtensionsInputProps> = ({
  label,
  placeholder,
  defaultValue,
  onValueChange,
  minRows = 4,
  size,
}) => {
  const { t } = useTranslation();
  const [text, setText] = useState(defaultValue?.join(" "));
  const [extensions, setExtensions] = useState<string[] | undefined>(
    defaultValue,
  );

  return (
    <div>
      <Textarea
        fullWidth
        isClearable
        isMultiline
        label={label ?? t<string>("common.label.extensions")}
        minRows={minRows}
        size={size}
        placeholder={placeholder ?? t<string>("common.placeholder.separateBySpaceOrNewline")}
        value={text}
        onValueChange={(v) => {
          setText(v);
          const newExtensions = extractExtensions(v);

          setExtensions(newExtensions);
          onValueChange?.(newExtensions);
        }}
      />
      {extensions && extensions.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1">
          {extensions.map((ext, i) => (
            <Chip key={i} radius="sm" size="sm" variant="flat">
              {ext}
            </Chip>
          ))}
        </div>
      )}
    </div>
  );
};

export default ExtensionsInput;

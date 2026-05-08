"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import AceEditor from "react-ace";
import "ace-builds/src-noconflict/mode-json";
import "ace-builds/src-noconflict/theme-monokai";
import "ace-builds/src-noconflict/theme-github";
import "ace-builds/src-noconflict/ext-language_tools";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

export interface JsonEditorProps {
  value?: string | null;
  onChange?: (v: string) => void;
  /** Comment block (one per line, prefixed with "// ") prepended at the top when value is empty. */
  sampleHeader?: string;
  /** Sample JSON object (already pretty-printed) appended after the header when value is empty. */
  sampleBody?: string;
  height?: string;
  minLines?: number;
  maxLines?: number;
  readOnly?: boolean;
  placeholder?: string;
}

/**
 * JSON editor backed by Ace. Tolerates `//` line comments — backend invokers
 * are configured to skip them. When `value` is empty and a sample is provided,
 * a commented sample is rendered as the initial content.
 */
const JsonEditor = ({
  value,
  onChange,
  sampleHeader,
  sampleBody,
  height,
  minLines = 6,
  maxLines = 25,
  readOnly,
  placeholder,
}: JsonEditorProps) => {
  const ctx = useBakabaseContext();
  const aceTheme = ctx?.isDarkMode ? "monokai" : "github";

  // Show sample only when input is empty and a sample is provided.
  const initial = useMemo(() => {
    if (value && value.trim().length > 0) return value;
    if (sampleHeader || sampleBody) {
      const parts: string[] = [];
      if (sampleHeader) parts.push(sampleHeader.trim());
      if (sampleBody) parts.push(sampleBody.trim());
      return parts.join("\n\n");
    }
    return "";
  }, []);
  // ESLint will warn that we don't depend on value/sample*; intentional —
  // we only want the sample to seed the editor on first render.

  const [text, setText] = useState(initial);
  const lastEmittedRef = useRef(initial);

  // Sync inbound value changes (e.g. when editing a different record)
  useEffect(() => {
    if (value !== undefined && value !== null && value !== lastEmittedRef.current) {
      setText(value);
      lastEmittedRef.current = value;
    }
  }, [value]);

  const handleChange = (v: string) => {
    setText(v);
    lastEmittedRef.current = v;
    onChange?.(v);
  };

  return (
    <div className="rounded-md overflow-hidden border border-default-200">
      <AceEditor
        mode="json"
        theme={aceTheme}
        value={text}
        onChange={handleChange}
        name={`json-editor-${Math.random().toString(36).slice(2)}`}
        width="100%"
        height={height}
        minLines={minLines}
        maxLines={maxLines}
        readOnly={readOnly}
        placeholder={placeholder}
        showPrintMargin={false}
        wrapEnabled
        editorProps={{ $blockScrolling: true }}
        setOptions={{
          useWorker: false,
          tabSize: 2,
          showLineNumbers: true,
          highlightActiveLine: !readOnly,
          fontSize: 13,
        }}
      />
    </div>
  );
};

/**
 * Strip `//` line comments and `/* ... *\/` block comments from a JSON-ish string.
 * Used right before sending JSON config payloads to the backend.
 */
export const stripJsonComments = (raw: string): string => {
  if (!raw) return raw;
  // Block comments
  const noBlock = raw.replace(/\/\*[\s\S]*?\*\//g, "");
  // Line comments — keep // inside strings intact by tracking quotes.
  const out: string[] = [];
  let inString = false;
  let escape = false;
  for (let i = 0; i < noBlock.length; i++) {
    const c = noBlock[i];
    if (inString) {
      out.push(c);
      if (escape) {
        escape = false;
      } else if (c === "\\") {
        escape = true;
      } else if (c === '"') {
        inString = false;
      }
      continue;
    }
    if (c === '"') {
      inString = true;
      out.push(c);
      continue;
    }
    if (c === "/" && noBlock[i + 1] === "/") {
      // Skip until newline
      while (i < noBlock.length && noBlock[i] !== "\n") i++;
      out.push("\n");
      continue;
    }
    out.push(c);
  }
  return out.join("");
};

export default JsonEditor;

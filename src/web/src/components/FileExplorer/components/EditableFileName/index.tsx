"use client";

import React, {
  forwardRef,
  memo,
  useCallback,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
} from "react";
import { useUpdateEffect } from "react-use";
import { AutoTextSize } from "auto-text-size";

import {
  buildLogger,
  createSelection,
  forceFocus,
  getFileNameWithoutExtension,
  useTraceUpdate,
} from "@/components/utils";
import BApi from "@/sdk/BApi";
import { Input } from "@/components/bakaui";

interface Props {
  path: string;
  name: string;
  isDirectory: boolean;
  disabled?: boolean;
}

export type EditableFileNameRef = {
  beginRename: () => void;
};

const log = buildLogger("EditableText");

const EditableText = forwardRef<EditableFileNameRef, Props>((props, ref) => {
  const { path, name, isDirectory, disabled = false } = props;

  const propsRef = useRef(props);

  propsRef.current = props;

  const [editing, setEditing] = useState(false);
  const editingRef = useRef(editing);
  const submittingRef = useRef(false);
  const nodeRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);

  const [value, setValue] = useState(name);
  const valueRef = useRef(value);

  useTraceUpdate(props, "EditableText");
  log("Rendering", props);

  useUpdateEffect(() => {
    editingRef.current = editing;
    if (editing) {
      const selection = isDirectory
        ? valueRef.current
        : getFileNameWithoutExtension(valueRef.current);

      createSelection(inputRef.current, 0, selection!.length);
    }
  }, [editing]);

  useUpdateEffect(() => {
    valueRef.current = value;
  }, [value]);

  useUpdateEffect(() => {
    if (!editingRef.current) {
      setValue(name);
    }
  }, [name]);

  const cancel = useCallback(() => {
    if (editingRef.current) {
      editingRef.current = false;
      setEditing(false);
      forceFocus(nodeRef.current);
      setValue(propsRef.current.name);
    }
  }, []);

  const submit = useCallback(async () => {
    if (!editingRef.current || submittingRef.current) return;
    if (valueRef.current && valueRef.current != propsRef.current.name) {
      submittingRef.current = true;
      try {
        const rsp = await BApi.file.renameFile({
          fullname: path,
          newName: valueRef.current,
        });

        setValue(rsp.code ? propsRef.current.name : valueRef.current);
      } catch (cause) {
        log("Rename failed", cause);
        setValue(propsRef.current.name);
      } finally {
        editingRef.current = false;
        submittingRef.current = false;
        setEditing(false);
      }
    } else {
      cancel();
    }
  }, [path, cancel]);

  const enterEditingModeKeyDownHandler = useCallback(
    (e: KeyboardEvent) => {
      if (disabled || editingRef.current) {
        return;
      }
      if (e.key == "F2") {
        setEditing(true);
        e.stopPropagation();
      }
    },
    [disabled],
  );

  useImperativeHandle(
    ref,
    () => ({
      beginRename: () => {
        if (!disabled && !editingRef.current) setEditing(true);
      },
    }),
    [disabled],
  );

  const inputKeyDownHandler = useCallback(
    (e: React.KeyboardEvent) => {
      log("Key down", e.key, e.ctrlKey, e.shiftKey, e.altKey, e.metaKey, e);
      // Editing keys belong to the input, not the selectable file row.
      e.stopPropagation();
      switch (e.key) {
        case "Enter":
          submit();
          break;
        case "Escape":
          cancel();
          break;
        case "Delete":
          break;
        default:
          return;
      }
    },
    [submit, cancel],
  );

  // Track parent element for cleanup
  const parentListenerRef = useRef<HTMLElement | null>(null);
  const handlerRef = useRef(enterEditingModeKeyDownHandler);

  // Update handler when it changes, and cleanup on unmount
  useEffect(() => {
    const oldHandler = handlerRef.current;

    handlerRef.current = enterEditingModeKeyDownHandler;

    // If parent exists and handler changed, swap listeners
    if (parentListenerRef.current && oldHandler !== enterEditingModeKeyDownHandler) {
      parentListenerRef.current.removeEventListener("keydown", oldHandler);
      parentListenerRef.current.addEventListener("keydown", enterEditingModeKeyDownHandler);
    }

    return () => {
      if (parentListenerRef.current) {
        parentListenerRef.current.removeEventListener("keydown", enterEditingModeKeyDownHandler);
      }
    };
  }, [enterEditingModeKeyDownHandler]);

  log(props, valueRef.current, editingRef.current);

  return (
    <div
      ref={(r) => {
        if (r) {
          nodeRef.current = r;
          let e: HTMLElement | null = r.parentElement;

          while (e) {
            if (e.className.includes("entry-keydown-listener")) {
              // Remove from old parent if different
              if (parentListenerRef.current && parentListenerRef.current !== e) {
                parentListenerRef.current.removeEventListener(
                  "keydown",
                  enterEditingModeKeyDownHandler,
                );
              }
              // Only add if not already attached to this element
              if (parentListenerRef.current !== e) {
                e.addEventListener("keydown", enterEditingModeKeyDownHandler);
                parentListenerRef.current = e;
              }
              break;
            } else {
              e = e.parentElement;
            }
          }
        }
      }}
      className={`min-w-0 ${editing ? "flex-1" : ""}`}
      role={editing ? undefined : "button"}
      tabIndex={editing ? -1 : 0}
      onKeyDown={(e) => enterEditingModeKeyDownHandler(e.nativeEvent)}
    >
      {editing ? (
        <Input
          // can't remove outline by outline-none or ring-0
          ref={inputRef}
          className={"w-full"}
          data-focus={false}
          radius={"none"}
          onDoubleClick={(e) => {
            log("onDoubleClick", e);
            e.stopPropagation();
            e.preventDefault();
          }}
          // autoFocus
          size={"sm"}
          value={value}
          onBlur={submit}
          onClick={(e) => {
            log("onClick", e);
            e.stopPropagation();
            e.preventDefault();
          }}
          onKeyDown={inputKeyDownHandler}
          onValueChange={(v) => {
            setValue(v);
          }}
        />
      ) : (
        <AutoTextSize maxFontSizePx={14}>{value}</AutoTextSize>
      )}
    </div>
  );
});

export default memo(EditableText);

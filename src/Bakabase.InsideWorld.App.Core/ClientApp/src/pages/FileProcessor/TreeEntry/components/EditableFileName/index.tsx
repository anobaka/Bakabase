import { memo, useCallback, useEffect, useRef, useState } from 'react';
import { Input } from '@alifd/next';
import { useUpdateEffect } from 'react-use';
import { AutoTextSize } from 'auto-text-size';
import { buildLogger, createSelection, forceFocus, getFileNameWithoutExtension, useTraceUpdate } from '@/components/utils';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import { IwFsType } from '@/sdk/constants';
import BApi from '@/sdk/BApi';

interface Props {
  entry: Entry;
}

const EditableText = memo((props: Props) => {
  const {
    entry,
  } = props;
  const [editing, setEditing] = useState(false);
  const editingRef = useRef(editing);
  const nodeRef = useRef<HTMLDivElement | null>(null);

  const [value, setValue] = useState(entry.name);
  const valueRef = useRef(value);

  const originalValueRef = useRef(entry.name);

  const log = buildLogger('EditableText');
  useTraceUpdate(props, 'EditableText');
  // log('Rendering', props);

  useUpdateEffect(() => {
    editingRef.current = editing;
  }, [editing]);

  useUpdateEffect(() => {
    valueRef.current = value;
  }, [value]);

  useEffect(() => {
    // log('Init');

    return () => {
    };
  }, []);

  const nameInputRefCallback = useCallback((node: any) => {
    if (node) {
      const selection = entry.type == IwFsType.Directory ? valueRef.current : getFileNameWithoutExtension(valueRef.current);
      const input = node.getInputNode();
      createSelection(input, 0, selection!.length);
      forceFocus(input);
    }
  }, []);

  const onKeyDown = useCallback(
    (e) => {
      log('Key down', e.key, e.ctrlKey, e.shiftKey, e.altKey, e.metaKey, e);
      switch (e.key) {
        case 'F2':
          if (!editingRef.current) {
            setEditing(true);
          }
          break;
        case 'Enter':
          submit();
          break;
        case 'Escape':
          if (editingRef.current) {
            cancel();
          } else {
            return;
          }
          break;
        default:
          return;
      }
      e.stopPropagation();
    }, []);

  const cancel = useCallback(() => {
    if (editingRef.current) {
      setEditing(false);
      setValue(originalValueRef.current);
      forceFocus(nodeRef.current);
    }
  }, []);

  const submit = useCallback(async () => {
    if (valueRef.current && valueRef.current != entry.name) {
      const rsp = await BApi.file.renameFile({
        fullname: entry.path,
        newName: valueRef.current,
      });
      if (!rsp.code) {
        originalValueRef.current = valueRef.current;
        setEditing(false);
      }
    } else {
      cancel();
    }
  }, []);

  // log(originalValueRef.current);

  return (
    <div
      className={'fp-te-et'}
      style={editing ? { flex: 1 } : undefined}
      onKeyDown={onKeyDown}
      ref={r => {
        if (r) {
          nodeRef.current = r;
          let e: HTMLElement | null = r;
          while (e) {
            if (e.className.includes('entry-keydown-listener')) {
              e.addEventListener('keydown', onKeyDown);
              break;
            } else {
              e = e.parentElement;
            }
          }
        }
      }}
    >
      {editing ? (
        <Input
          style={{ width: '100%' }}
          ref={nameInputRefCallback}
          defaultValue={value}
          value={value}
          size={'small'}
          onChange={v => {
            setValue(v);
          }}
          onBlur={submit}
        />
      ) : (
        <AutoTextSize maxFontSizePx={14}>
          {originalValueRef.current}
        </AutoTextSize>
      )}
    </div>
  );
});

export default EditableText;

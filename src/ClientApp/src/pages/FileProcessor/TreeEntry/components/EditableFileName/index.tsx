import { memo, useCallback, useEffect, useRef, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import { AutoTextSize } from 'auto-text-size';
import { buildLogger, createSelection, forceFocus, getFileNameWithoutExtension, useTraceUpdate } from '@/components/utils';
import BApi from '@/sdk/BApi';
import { Input } from '@/components/bakaui';

interface Props {
  path: string;
  name: string;
  isDirectory: boolean;
  disabled?: boolean;
}

const log = buildLogger('EditableText');

const EditableText = memo((props: Props) => {
  const {
    path,
    name,
    isDirectory,
    disabled = false,
  } = props;

  const propsRef = useRef(props);

  const [editing, setEditing] = useState(false);
  const editingRef = useRef(editing);
  const nodeRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);

  const [value, setValue] = useState(name);
  const valueRef = useRef(value);

  useTraceUpdate(props, 'EditableText');
  log('Rendering', props);

  useEffect(() => {
    // log('Init');

    return () => {
    };
  }, []);

  useUpdateEffect(() => {
    editingRef.current = editing;
    if (editing) {
      const selection = isDirectory ? valueRef.current : getFileNameWithoutExtension(valueRef.current);
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

  const enterEditingModeKeyDownHandler = useCallback(
    (e) => {
      if (disabled || editingRef.current) {
        return;
      }
      if (e.key == 'F2') {
        setEditing(true);
        e.stopPropagation();
      }
    }, []);

  const inputKeyDownHandler = useCallback(
    (e) => {
      log('Key down', e.key, e.ctrlKey, e.shiftKey, e.altKey, e.metaKey, e);
      switch (e.key) {
        case 'Enter':
          submit();
          break;
        case 'Escape':
          cancel();
          break;
        case 'Delete':
          break;
        default:
          // Propagation
          return;
      }
      // e.stopPropagation();
    }, []);

  const cancel = useCallback(() => {
    if (editingRef.current) {
      setEditing(false);
      forceFocus(nodeRef.current);
      setValue(propsRef.current.name);
    }
  }, []);

  const submit = useCallback(async () => {
    if (valueRef.current && valueRef.current != propsRef.current.name) {
      const rsp = await BApi.file.renameFile({
        fullname: path,
        newName: valueRef.current,
      });
      if (!rsp.code) {
        setValue(valueRef.current);
      }
      setEditing(false);
    } else {
      cancel();
    }
  }, []);

  log(props, valueRef.current, editingRef.current);

  return (
    <div
      className={'fp-te-et'}
      style={editing ? { flex: 1 } : undefined}
      onKeyDown={enterEditingModeKeyDownHandler}
      ref={r => {
        if (r) {
          nodeRef.current = r;
          let e: HTMLElement | null = r.parentElement;
          while (e) {
            if (e.className.includes('entry-keydown-listener')) {
              e.removeEventListener('keydown', enterEditingModeKeyDownHandler);
              e.addEventListener('keydown', enterEditingModeKeyDownHandler);
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
          // can't remove outline by outline-none or ring-0
          className={'w-full'}
          ref={inputRef}
          value={value}
          onClick={e => {
            log('onClick', e);
            e.stopPropagation();
            e.preventDefault();
          }}
          onDoubleClick={e => {
            log('onDoubleClick', e);
            e.stopPropagation();
            e.preventDefault();
          }}
          // autoFocus
          size={'sm'}
          radius={'none'}
          data-focus={false}
          onKeyDown={inputKeyDownHandler}
          onValueChange={v => {
            setValue(v);
          }}
          onBlur={submit}
        />
      ) : (
        <AutoTextSize maxFontSizePx={14}>
          {value}
        </AutoTextSize>
      )}
    </div>
  );
});

export default EditableText;

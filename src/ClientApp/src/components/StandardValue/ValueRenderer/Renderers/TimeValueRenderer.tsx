import { useRef, useState } from 'react';
import type { Duration } from 'dayjs/plugin/duration';
import type { ValueRendererProps } from '../models';
import { DateInput, TimeInput } from '@/components/bakaui';
import NotSet from '@/components/StandardValue/ValueRenderer/Renderers/components/NotSet';
type TimeValueRendererProps = ValueRendererProps<Duration, Duration> & {
  format?: string;
};

export default ({ value, format, variant, editor, ...props }: TimeValueRendererProps) => {
  const [editing, setEditing] = useState(false);
  const editingValueRef = useRef<Duration>();

  const f = format == undefined ? 'HH:mm:ss' : format;

  const startEditing = editor ? () => {
    editingValueRef.current = value;
    setEditing(true);
  } : undefined;

  if (editing) {
    return (
      <TimeInput
        value={value}
        isReadOnly={!editor}
        onChange={x => editingValueRef.current = x}
        onBlur={x => {
          editor?.onValueChange?.(editingValueRef.current, editingValueRef.current);
          setEditing(false);
        }}
      />
    );
  }

  if (value == undefined) {
    return (
      <NotSet onClick={startEditing} />
    );
  } else {
    return (
      <span onClick={startEditing}>{value?.format(f)}</span>
    );
  }
};

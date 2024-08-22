import { useState } from 'react';
import type { Duration } from 'dayjs/plugin/duration';
import type { ValueRendererProps } from '../models';
import { DateInput, TimeInput } from '@/components/bakaui';
type TimeValueRendererProps = ValueRendererProps<Duration, Duration> & {
  format?: string;
};

export default ({ value, format, variant, editor, ...props }: TimeValueRendererProps) => {
  const [editing, setEditing] = useState(false);

  const f = format == undefined ? 'HH:mm:ss' : format;

  if (variant == 'light' && !editing) {
    return (
      <span onClick={editor ? () => {
        setEditing(true);
      } : undefined}
      >{value?.format(f)}</span>
    );
  }

  return (
    <TimeInput
      value={value}
      isReadOnly={!editor}
      onChange={x => editor?.onValueChange?.(x, x)}
    />
  );
};

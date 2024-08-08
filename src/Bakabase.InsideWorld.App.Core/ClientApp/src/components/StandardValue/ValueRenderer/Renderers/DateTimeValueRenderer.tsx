import type { Dayjs } from 'dayjs';
import { useState } from 'react';
import type { ValueRendererProps } from '../models';
import { DateInput, TimeInput } from '@/components/bakaui';
import NotSet from '@/components/StandardValue/ValueRenderer/Renderers/components/NotSet';
type DateTimeValueRendererProps = ValueRendererProps<Dayjs> & {
  format?: string;
  as: 'datetime' | 'date';
};

export default ({ value, format, as, variant, editor, ...props }: DateTimeValueRendererProps) => {
  const [editing, setEditing] = useState(false);

  const startEditing = editor ? () => {
    setEditing(true);
  } : undefined;

  if (value == undefined) {
    return (
      <NotSet onClick={startEditing} />
    );
  }

  const f = format == undefined ? as == 'datetime' ? 'YYYY-MM-DD HH:mm:ss' : 'HH:mm:ss' : format;

  if (variant == 'light' && !editing) {
    return (
      <span onClick={startEditing}>{value?.format(f)}</span>
    );
  }

  return (
    <DateInput
      granularity={as == 'datetime' ? 'second' : 'day'}
      value={value}
      isReadOnly={!editor}
      onChange={d => editor?.onValueChange?.(d, d)}
    />
  );
};
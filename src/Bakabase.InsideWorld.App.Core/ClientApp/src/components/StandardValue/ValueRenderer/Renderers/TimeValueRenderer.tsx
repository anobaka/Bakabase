import { useState } from 'react';
import type { Duration } from 'dayjs/plugin/duration';
import type { ValueRendererProps } from '../models';
import type { EditableValueProps } from '@/components/StandardValue/models';
import { DateInput, TimeInput } from '@/components/bakaui';
type TimeValueRendererProps = ValueRendererProps<Duration> & EditableValueProps<Duration> & {
  format: string;
};

export default ({ value, format, variant, onValueChange, editable, ...props }: TimeValueRendererProps) => {
  const [editing, setEditing] = useState(false);

  if (variant == 'light' && !editing) {
    return (
      <span onClick={editable ? () => {
        setEditing(true);
      } : undefined}
      >{value?.format(format)}</span>
    );
  }

  return (
    <TimeInput
      value={value}
      isReadOnly={!editable}
      onChange={onValueChange}
    />
  );
};

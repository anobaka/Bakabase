import { useRef, useState } from 'react';
import type { ValueRendererProps } from '../models';
import NumberValueEditor from '../../ValueEditor/Editors/NumberValueEditor';
import StringValueRenderer from './StringValueRenderer';
import type { EditableValueProps } from '@/components/StandardValue/models';
import { Input, Progress } from '@/components/bakaui';
type NumberValueRendererProps = ValueRendererProps<number> & EditableValueProps<number> & {
  precision?: number;
  as?: 'number' | 'progress';
  suffix?: string;
};

export default ({ value, precision, onValueChange, editable, variant, suffix, as, ...props }: NumberValueRendererProps) => {
  const [editing, setEditing] = useState(false);

  if (variant == 'light' && !editing) {
    return (
      <span
        onClick={editable ? () => setEditing(true) : undefined}
      >{value}{suffix}</span>
    );
  }

  if (editing) {
    return (
      <NumberValueEditor
        initValue={value}
        onChange={onValueChange}
      />
    );
  } else {
    const a = as ?? 'number';
    switch (a) {
      case 'number':
        return (
          <span
            onClick={editable ? () => setEditing(true) : undefined}
          >{value}{suffix}</span>
        );
      case 'progress':
        return (
          <Progress
            value={value}
            onClick={editable ? () => setEditing(true) : undefined}
          />
        );
    }
  }
};

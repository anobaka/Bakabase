import { useRef, useState } from 'react';
import type { ValueRendererProps } from '../models';
import NumberValueEditor from '../../ValueEditor/Editors/NumberValueEditor';
import { Input, Progress } from '@/components/bakaui';
type NumberValueRendererProps = ValueRendererProps<number, number> & {
  precision?: number;
  as?: 'number' | 'progress';
  suffix?: string;
};

export default ({ value, precision, editor, variant, suffix, as, ...props }: NumberValueRendererProps) => {
  const [editing, setEditing] = useState(false);

  if (variant == 'light' && !editing) {
    return (
      <span
        onClick={editor ? () => setEditing(true) : undefined}
      >{value}{suffix}</span>
    );
  }

  if (editing) {
    return (
      <NumberValueEditor
        value={value}
        onValueChange={editor?.onValueChange}
      />
    );
  } else {
    const a = as ?? 'number';
    switch (a) {
      case 'number':
        return (
          <span
            onClick={editor ? () => setEditing(true) : undefined}
          >{value}{suffix}</span>
        );
      case 'progress':
        return (
          <Progress
            value={value}
            onClick={editor ? () => setEditing(true) : undefined}
          />
        );
    }
  }
};

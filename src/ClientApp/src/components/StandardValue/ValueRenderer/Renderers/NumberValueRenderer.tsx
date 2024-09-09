import { useRef, useState } from 'react';
import type { ValueRendererProps } from '../models';
import NumberValueEditor from '../../ValueEditor/Editors/NumberValueEditor';
import { Input, Progress } from '@/components/bakaui';
import NotSet from '@/components/StandardValue/ValueRenderer/Renderers/components/NotSet';
type NumberValueRendererProps = ValueRendererProps<number, number> & {
  precision?: number;
  as?: 'number' | 'progress';
  suffix?: string;
};

export default ({ value, precision, editor, variant, suffix, as, ...props }: NumberValueRendererProps) => {
  const [editing, setEditing] = useState(false);

  const startEditing = editor ? () => setEditing(true) : undefined;

  if (editing) {
    return (
      <NumberValueEditor
        value={value}
        onValueChange={editor?.onValueChange}
      />
    );
  }

  if (value == undefined) {
    return (
      <NotSet onClick={startEditing} />
    );
  }

  if (variant == 'light' || as == 'number') {
    return (
      <span
        onClick={startEditing}
      >{value}{suffix}</span>
    );
  } else {
    return (
      <Progress
        size={'sm'}
        value={value}
        onClick={startEditing}
      />
    );
  }
};

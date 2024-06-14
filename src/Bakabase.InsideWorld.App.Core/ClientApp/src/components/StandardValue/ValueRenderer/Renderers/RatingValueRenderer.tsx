import { useRef, useState } from 'react';
import type { ValueRendererProps } from '../models';
import NumberValueEditor from '../../ValueEditor/Editors/NumberValueEditor';
import type { EditableValueProps } from '@/components/StandardValue/models';
import { Input, Progress, Rating } from '@/components/bakaui';
type RatingValueRendererProps = ValueRendererProps<number> & EditableValueProps<number> & {
  allowHalf?: boolean;
};

export default ({ value, onValueChange, editable, variant, allowHalf, ...props }: RatingValueRendererProps) => {
  const [editing, setEditing] = useState(false);

  if (variant == 'light') {
    if (editing) {
      return (
        <NumberValueEditor
          initValue={value}
          onChange={onValueChange}
        />
      );
    } else {
      return (
        <span
          onClick={editable ? () => setEditing(true) : undefined}
        >{value}</span>
      );
    }
  } else {
    return (
      <Rating value={value} disabled={!editable} allowHalf={allowHalf} onChange={onValueChange} />
    );
  }
};

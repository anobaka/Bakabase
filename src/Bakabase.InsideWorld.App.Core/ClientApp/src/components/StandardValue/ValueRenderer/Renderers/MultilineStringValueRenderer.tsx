import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import type { ValueRendererProps } from '../models';
import { Card, CardBody, Textarea } from '@/components/bakaui';

type MultilineStringValueRendererProps = ValueRendererProps<string, string> & {
};

export default ({ value, editor, variant }: MultilineStringValueRendererProps) => {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [editingValue, setEditingValue] = useState(value);

  const v = variant ?? 'default';

  const startEditing = () => {
    setEditing(true);
    setEditingValue(value);
  };

  if (v == 'light' && !editing) {
    return (
      <span onClick={editor ? startEditing : undefined}>{value}</span>
    );
  }

  if (editing) {
    return (
      <Textarea
        value={editingValue}
        onValueChange={setEditingValue}
        onBlur={() => {
          setEditing(false);
          editor?.onValueChange?.(editingValue, editingValue);
        }}
      />
    );
  }

  return (
    <span>
      {(value === null || value === undefined || value.length == 0) ? t('Not set') : value}
    </span>
  );
};

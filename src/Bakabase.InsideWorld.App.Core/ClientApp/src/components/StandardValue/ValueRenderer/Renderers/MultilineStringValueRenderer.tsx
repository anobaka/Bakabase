import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import type { ValueRendererProps } from '../models';
import { Card, CardBody, Textarea } from '@/components/bakaui';
import type { EditableValueProps } from '@/components/StandardValue/models';

type MultilineStringValueRendererProps = ValueRendererProps<string> & EditableValueProps<string>;

export default ({ value, onValueChange, editable, variant }: MultilineStringValueRendererProps) => {
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
      <span onClick={editable ? startEditing : undefined}>{value}</span>
    );
  }

  if (editing) {
    return (
      <Textarea
        value={editingValue}
        onValueChange={setEditingValue}
        onBlur={() => {
          setEditing(false);
          onValueChange?.(editingValue);
        }}
      />
    );
  }

  return (
    <Card onClick={editable ? startEditing : undefined}>
      <CardBody>
        {(value === null || value === undefined || value.length == 0) ? t('Not set') : value}
      </CardBody>
    </Card>
  );
};

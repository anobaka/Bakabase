import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import type { ValueRendererProps } from '../models';
import type { EditableValueProps } from '../../models';
import { Button, Chip, Input, TextArea, Card, CardBody } from '@/components/bakaui';

type StringValueRendererProps = ValueRendererProps<string> & EditableValueProps<string> & {
  multiline?: boolean;
};

export default ({ value, multiline, variant, onValueChange, editable }: StringValueRendererProps) => {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);

  if (!editing || !editable) {
    return (
      <>{value}</>
    );
  }

  if (editing) {
    if (multiline) {
      return (
        <TextArea
          value={value}
          onValueChange={onValueChange}
          onBlur={() => {
            setEditing(false);
          }}
        />
      );
    } else {
      return (
        <Input
          value={value}
          onValueChange={onValueChange}
          onBlur={() => {
            setEditing(false);
          }}
        />
      );
    }
  } else {
    return (
      <Card
        className={'cursor-pointer'}
        onClick={() => {
        setEditing(true);
      }}
      >
        <CardBody>{value ?? t('Click to set')}</CardBody>
      </Card>
    );
  }
};

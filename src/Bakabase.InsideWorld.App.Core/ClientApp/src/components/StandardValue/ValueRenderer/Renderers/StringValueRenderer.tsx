import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { useUpdateEffect } from 'react-use';
import type { ValueRendererProps } from '../models';
import NotSet from './components/NotSet';
import { Card, CardBody, Input, Textarea } from '@/components/bakaui';
import { buildLogger } from '@/components/utils';

type StringValueRendererProps = ValueRendererProps<string> & {
  multiline?: boolean;
};

const log = buildLogger('StringValueRenderer');

export default (props: StringValueRendererProps) => {
  const {
    value: propsValue,
    multiline,
    variant,
    editor,
  } = props;
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(propsValue);

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  const startEditing = editor ? () => {
    log('Start editing');
    setEditing(true);
  } : undefined;

  if (!editing) {
    if (value == undefined || value.length == 0) {
      return (
        <NotSet onClick={startEditing} />
      );
    }
  }

  log(props);

  const completeEditing = () => {
    editor?.onValueChange?.(value, value);
    setEditing(false);
  };

  if (variant == 'light' && !editing) {
    return (
      <span onClick={startEditing}>{value}</span>
    );
  }

  if (editing) {
    if (multiline) {
      return (
        <Textarea
          value={value}
          onValueChange={setValue}
          onBlur={completeEditing}
        />
      );
    } else {
      return (
        <Input
          value={value}
          onValueChange={setValue}
          onBlur={completeEditing}
        />
      );
    }
  } else {
    return (
      <span onClick={startEditing}>{value}</span>
    );
  }
};

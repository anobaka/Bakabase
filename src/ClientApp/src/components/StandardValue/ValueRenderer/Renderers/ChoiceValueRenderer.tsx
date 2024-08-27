import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { ValueRendererProps } from '../models';
import ChoiceValueEditor from '../../ValueEditor/Editors/ChoiceValueEditor';
import NotSet from './components/NotSet';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Chip, Card, CardBody } from '@/components/bakaui';
import { buildLogger } from '@/components/utils';

type Data = {label: string; value: string};

type ListStringValueRendererProps = ValueRendererProps<string[]> & {
  multiple?: boolean;
  getDataSource?: () => Promise<Data[]>;
};

const log = buildLogger('ChoiceValueRenderer');

export default (props: ListStringValueRendererProps) => {
  const { value, editor, variant, getDataSource, multiple } = props;
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  log(props);

  const startEditing = editor ? async () => {
    console.log(editor, getDataSource);
    createPortal(ChoiceValueEditor, {
      value: editor?.value,
      getDataSource: getDataSource ?? (async () => []),
      onValueChange: editor?.onValueChange,
      multiple: multiple ?? false,
    });
  } : undefined;

  const validValues = value?.map(v => v != undefined) || [];
  if (validValues.length == 0) {
    return (
      <NotSet onClick={startEditing} />
    );
  }

  if (variant == 'light') {
    return (
      <span onClick={startEditing}>{value?.join(', ')}</span>
    );
  } else {
    return (
      <div onClick={startEditing} className={'flex flex-wrap gap-1'}>
        {value?.map(d => {
            return (
              <Chip
                size={'sm'}
                radius={'sm'}
              >
                {d}
              </Chip>
            );
          })}
      </div>
    );
  }
};

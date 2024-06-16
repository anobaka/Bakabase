import { useRef, useState } from 'react';
import type { ValueRendererProps } from '../models';
import type { EditableValueProps, MultilevelData } from '../../models';
import MultilevelValueEditor from '../../ValueEditor/Editors/MultilevelValueEditor';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Chip, Card, CardBody } from '@/components/bakaui';

type MultilevelValueRendererProps = ValueRendererProps<string[][]> & EditableValueProps<string[]> & {
  multiple?: boolean;
  getDataSource?: () => Promise<MultilevelData<string>>;
};

export default ({ value, onValueChange, editable, variant, getDataSource, multiple, ...props }: MultilevelValueRendererProps) => {
  const { createPortal } = useBakabaseContext();

  const dataSourceRef = useRef<MultilevelData<string>[]>([]);

  const showEditor = () => {
    createPortal(MultilevelValueEditor<string>, {
      getDataSource: async () => dataSourceRef.current,
      onChange: v => {
        if (v) {
          onValueChange?.(v.map(x => x));
        }
      },
      multiple,
    });
  };

  if (variant == 'light') {
    return (
      <span onClick={editable ? showEditor : undefined}>{value?.map(v => v.join('/')).join(';')}</span>
    );
  } else {
    return (
      <Card onClick={editable ? showEditor : undefined}>
        <CardBody className={'flex flex-wrap gap-1'}>
          {value?.map(v => {
            const str = v.join('/');
            return (
              <Chip
                size={'sm'}
                radius={'sm'}
              >
                {str}
              </Chip>
            );
          })}
        </CardBody>
      </Card>
    );
  }
};

import { useRef, useState } from 'react';
import type { ValueRendererProps } from '../models';
import type { EditableValueProps } from '../../models';
import MultilevelValueEditor from '../../ValueEditor/Editors/MultilevelValueEditor';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Chip, Card, CardBody } from '@/components/bakaui';

type Data = {label: string; value: string};

type ListStringValueRendererProps = ValueRendererProps<string[]> & EditableValueProps<string[]> & {
  multiple?: boolean;
  getDataSource?: () => Promise<Data[]>;
};

export default ({ value, onValueChange, editable, variant, getDataSource, multiple, ...props }: ListStringValueRendererProps) => {
  const { createPortal } = useBakabaseContext();

  const dataSourceRef = useRef<Data[]>([]);

  const selectedData = value?.filter(v => v !== null && v !== undefined && v.length > 0).map(v => dataSourceRef.current.find(d => d.value == v)).filter(x => x) as Data[];

  const showEditor = () => {
    createPortal(MultilevelValueEditor<string>, {
      getDataSource: async () => dataSourceRef.current,
      onChange: v => {
        if (v) {
          onValueChange?.(v);
        }
      },
      multiple,
    });
  };

  if (variant == 'light') {
    return (
      <span onClick={editable ? showEditor : undefined}>{selectedData.map(d => d.label).join(', ')}</span>
    );
  } else {
    return (
      <Card onClick={editable ? showEditor : undefined}>
        <CardBody className={'flex flex-wrap gap-1'}>
          {selectedData.map(d => {
            return (
              <Chip
                size={'sm'}
                radius={'sm'}
              >
                {d.label}
              </Chip>
            );
          })}
        </CardBody>
      </Card>
    );
  }
};

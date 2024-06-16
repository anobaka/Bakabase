import { useRef, useState } from 'react';
import type { ValueRendererProps } from '../models';
import type { EditableValueProps, MultilevelData, TagValue } from '../../models';
import MultilevelValueEditor from '../../ValueEditor/Editors/MultilevelValueEditor';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Chip, Card, CardBody } from '@/components/bakaui';

type TagsValueRendererProps = ValueRendererProps<TagValue[]> & EditableValueProps<string[]> & {
  getDataSource?: () => Promise<TagValue[]>;
};

export default ({ value, onValueChange, editable, variant, getDataSource, ...props }: TagsValueRendererProps) => {
  const { createPortal } = useBakabaseContext();

  const dataSourceRef = useRef<MultilevelData<string>[]>([]);

  const simpleLabels = value?.map(v => {
    if (v.group != undefined && v.group.length > 0) {
      return `${v.group}:${v.name}`;
    }
    return v.name;
  });

  const showEditor = () => {
    createPortal(MultilevelValueEditor<string>, {
      getDataSource: async () => dataSourceRef.current,
      onChange: v => {
        if (v) {
          onValueChange?.(v);
        }
      },
    });
  };

  if (variant == 'light') {
    return (
      <span onClick={editable ? showEditor : undefined}>{simpleLabels?.join(',')}</span>
    );
  } else {
    return (
      <Card onClick={editable ? showEditor : undefined}>
        <CardBody className={'flex flex-wrap gap-1'}>
          {simpleLabels?.map(l => {
            return (
              <Chip
                size={'sm'}
                radius={'sm'}
              >
                {l}
              </Chip>
            );
          })}
        </CardBody>
      </Card>
    );
  }
};

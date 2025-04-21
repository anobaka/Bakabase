import React, { useEffect, useState } from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import { DeleteOutlined, EyeInvisibleOutlined, EyeOutlined } from '@ant-design/icons';
import DragHandle from '@/components/DragHandle';
import { Button, ColorPicker, Input, Modal } from '@/components/bakaui';
import type { Tag } from '@/components/Property/models';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { buildColorValueString } from '@/components/bakaui/components/ColorPicker';
import colors from '@/components/bakaui/colors';

interface IProps {
  id: string;
  tag: Tag;
  onRemove?: (choice: Tag) => any;
  onChange?: (choice: Tag) => any;
  style?: any;
  checkUsage?: (value: string) => Promise<number>;
}

export function SortableTag({
                              id,
                              tag: propsTag,
                              onRemove,
                              onChange,
                              style: propsStyle,
                              checkUsage,
                            }: IProps) {
  const { t } = useTranslation();
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
  } = useSortable({ id: id });
  const { createPortal } = useBakabaseContext();

  const [tag, setTag] = useState(propsTag);

  useEffect(() => {
    // console.log(9999, 'new rendering');
  }, []);

  useUpdateEffect(() => {
    onChange?.(tag);
  }, [tag]);

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    ...propsStyle,
  };

  return (
    <div ref={setNodeRef} style={style} className={'flex gap-1 items-center'}>
      <DragHandle {...listeners} {...attributes} />
      <ColorPicker
        color={tag.color ?? colors.color}
        onChange={color => {
          setTag({
            ...tag,
            color: buildColorValueString(color),
          });
        }}
      />
      <Input
        size={'sm'}
        value={tag?.group}
        placeholder={t('Group of tag, optional')}
        onValueChange={group => {
          setTag({
            ...tag,
            group,
          });
        }}
      />
      <Input
        size={'sm'}
        value={tag?.name}
        placeholder={t('Name of tag, required')}
        onValueChange={name => {
          setTag({
            ...tag,
            name,
          });
        }}
      />
      <div className={'flex items-center'}>
        <Button
          size={'sm'}
          radius={'sm'}
          isIconOnly
          title={t('Hide in view')}
          onClick={() => {
            setTag({
              ...tag,
              hide: !tag.hide,
            });
          }}
          variant={'light'}
        >
          {tag.hide ? <EyeInvisibleOutlined className={'text-base'} /> : <EyeOutlined className={'text-base'} />}
        </Button>
        <Button
          size={'sm'}
          radius={'sm'}
          isIconOnly
          onClick={async () => {
            if (checkUsage) {
              const count = await checkUsage(tag.value);
              if (count > 0) {
                createPortal(Modal, {
                  defaultVisible: true,
                  size: 'sm',
                  title: t('Value is being referenced in {{count}} places', { count }),
                  children: t('Sure to delete?'),
                  onOk: async () => {
                    onRemove?.(tag);
                  },
                });
                return;
              }
            }
            onRemove?.(tag);
          }}
          variant={'light'}
          color={'danger'}
        >
          <DeleteOutlined className={'text-base'} />
        </Button>
      </div>
    </div>
  );
}

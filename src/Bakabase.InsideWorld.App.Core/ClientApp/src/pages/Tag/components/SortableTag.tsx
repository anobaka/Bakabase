import React, { useState } from 'react';
import { Dialog, Input, Message } from '@alifd/next';
import { SketchPicker } from 'react-color';
import i18n from 'i18next';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useUpdateEffect } from 'react-use';
import ButtonsBalloon from '@/components/ButtonsBalloon';

import type { Tag as TagDto } from '@/core/models/Tag';
import CustomIcon from '@/components/CustomIcon';
import { RemoveTag, UpdateTag } from '@/sdk/apis';
import { shadeColor } from '@/components/utils';

const SortableTag = (
  ({
     tag,
     onRemove = (t) => {
     },
     isBulkDeleting,
     onSelect = (selected) => {
     },
     selected,
     isFake,
     disabled,
   }: {
    tag: TagDto;
    onRemove: any;
    isFake: boolean;
    isBulkDeleting: boolean;
  }) => {
    const {
      attributes,
      listeners,
      setNodeRef,
      transform,
      transition,
    } = useSortable({ id: `t${tag.id}` });
    const style = {
      transform: CSS.Translate.toString(transform && {
        ...transform,
        scaleX: 1,
      }),
      transition,
      opacity: isFake ? 0.4 : 1,
    };

    useUpdateEffect(() => {
    }, [disabled]);

    const [editing, setEditing] = useState(false);
    const [name, setName] = useState();
    const [colorPickerValue, setColorPickerValue] = useState();

    const nameComponent = (
      <div
        className={'text'}
        title={`${i18n.t('Drag to sort')}, ${i18n.t('click to edit')}`}
        key={tag.id}
        style={{
          background: shadeColor(tag.color || '#000000', 99, 0.1),
          color: tag.color,
        }}
        onClick={() => {
          // console.log('clicked', tag?.name)
          setName(tag?.name);
          setColorPickerValue({ hex: tag.color });
          setEditing(true);
        }}
      >
        {/* {tag.id}: */}
        {tag.displayName}
      </div>
    );

    const buttonsBalloonOperations = isBulkDeleting ? [] : [
      {
        confirmation: true,
        onClick: () => {
          return RemoveTag({
            id: tag.id,
          })
            .invoke((a) => {
              if (!a.code) {
                onRemove(tag);
              }
            });
        },
        warning: true,
        label: 'Delete',
      },
    ];

    const mainContent = (
      <div
        ref={setNodeRef}
        style={style}
        {...attributes}
        {...listeners}
        onClick={() => {
          console.log('clicked', tag?.name);
        }}
        key={tag.id}
        className={`tag-page-draggable-tag ${selected ? 'selected' : ''}`}
      >
        {isBulkDeleting && (
          <>
            <div
              className="selective"
              onClick={() => {
                onSelect(!selected);
              }}
            >
              <CustomIcon type={'close-circle'} size={'small'} />
            </div>
          </>
        )}
        {nameComponent}
      </div>
    );

    // return mainContent;

    // console.log('rendering tag', buttonsBalloonOperations, tag, disabled, (((buttonsBalloonOperations.length == 0 && (!tag.preferredAlias) || tag.preferredAlias == tag.name)) || disabled));

    return (
      <>
        {((buttonsBalloonOperations.length == 0 && (!tag.preferredAlias || tag.preferredAlias == tag.name)) || disabled) ? (
          mainContent
        ) : (
          <ButtonsBalloon
            delay={300}
            key={tag.id}
            trigger={mainContent}
            operations={buttonsBalloonOperations}
          >
            {tag.preferredAlias && tag.preferredAlias != tag.name && `${i18n.t('Raw name')}: ${tag.name}`}
          </ButtonsBalloon>
        )}
        {editing && (
          <Dialog
            visible
            closeable
            onClose={() => {
              setEditing(false);
            }}
            onCancel={() => {
              setEditing(false);
            }}
            onOk={() => {
              if (name?.length > 0) {
                UpdateTag({
                  id: tag.id,
                  model: {
                    color: colorPickerValue.hex,
                    name,
                  },
                })
                  .invoke((a) => {
                    if (!a.code) {
                      tag.color = colorPickerValue.hex;
                      tag.name = name;
                    }
                    setEditing(false);
                  });
              } else {
                Message.error(i18n.t('Bad name'));
              }
            }}
            title={i18n.t('Editing tag')}
          >
            <div className={'tag-detail-dialog-body'}>
              <Input value={name} onChange={(v) => setName(v)} />
              <div className={'sketch-picker-container'}>
                <SketchPicker
                  color={colorPickerValue}
                  onChange={(v) => {
                    setColorPickerValue(v);
                  }}
                />
              </div>
            </div>
          </Dialog>
        )}
      </>
    );
  });

export default React.memo(SortableTag);

import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TagSelectorValue } from '@/components/TagSelector';
import TagSelector, { TagSelection } from '@/components/TagSelector';
import BApi from '@/sdk/BApi';
import { Modal } from '@/components/bakaui';
import { createPortalOfComponent } from '@/components/utils';

interface IProps {
  resourceIds: number[];
  onSaved?: () => any;
}

type IValue = Record<number, number[]>;

const ResourceTagBinderDialog = ({ resourceIds, onSaved }: IProps) => {
  const { t } = useTranslation();
  const [value, setValue] = useState<IValue>({});

  const [tagSelectorValue, setTagSelectorValue] = useState<TagSelectorValue>();

  useEffect(() => {
    BApi.resource.getResourcesByKeys({ ids: resourceIds }).then(r => {
      const v: IValue = {};
      const tagResourceMap: Record<number, number[]> = {};
      for (const resource of (r.data || [])) {
        const tagIds = resource.tags?.map(t => t.id!) || [];
        v[resource.id!] = tagIds;
        for (const tId of tagIds) {
          (tagResourceMap[tId] ??= []).push(resource.id!);
        }
      }
      setValue(v);

      const tsv: TagSelectorValue = {};
      for (const [tagId, rIds] of Object.entries(tagResourceMap)) {
        tsv[parseInt(tagId, 10)] = rIds.length === resourceIds.length ? TagSelection.Selected : rIds.length == 0 ? TagSelection.NotSelected : TagSelection.Indeterminate;
      }
      setTagSelectorValue(tsv);
    });
  }, []);

  return (
    <Modal
      defaultVisible
      size={'xl'}
      title={t('Bind tags to resources')}
      onOk={async () => {
        const rsp = await BApi.resource.updateResourceTags({
          resourceTagIds: value,
        });
        if (!rsp.code) {
          onSaved?.();
        }
      }}
    >
      {tagSelectorValue && (
        <TagSelector
          onChange={(v, tags) => {
            Object.keys(v).forEach(str => {
              const tagId = parseInt(str, 10);
              const selection = v[tagId];
              switch (selection) {
                case TagSelection.Selected:
                  Object.values(value).forEach(ids => {
                    if (!ids.includes(tagId)) {
                      ids.push(tagId);
                    }
                  });
                  break;
                case TagSelection.NotSelected:
                  Object.values(value).forEach(ids => {
                    const idx = ids.indexOf(tagId);
                    if (idx > -1) {
                      ids.splice(idx, 1);
                    }
                  });
                  break;
                case TagSelection.Indeterminate:
                  break;
              }
            });
            setValue({ ...value });
          }}
          defaultValue={tagSelectorValue}
          enableCreation={false}
        />
      )}
    </Modal>
  );
};

ResourceTagBinderDialog.show = (props: IProps) => createPortalOfComponent(ResourceTagBinderDialog, props);

export default ResourceTagBinderDialog;

import { Dialog } from '@alifd/next';
import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type ResourceDto from '@/core/models/Resource';
import TagSelector from '@/components/TagSelector';
import BApi from '@/sdk/BApi';
import { Modal } from '@/components/bakaui';
import { createPortalOfComponent } from '@/components/utils';

interface IProps {
  resourceIds: number[];
}

const ResourceTagBinderDialog = ({ resourceIds }: IProps) => {
  const { t } = useTranslation();
  const [resources, setResources] = useState<ResourceDto[]>([]);

  const componentValue: {[tagId: number]: number[]} = {};
  for (const r of resources) {
    if (r.tags) {
      for (const t of r.tags) {
        const ids = (componentValue[t.id] ??= []);
        ids.push(r.id);
      }
    }
  }
  const tagIds = Object.keys(componentValue).map(t => parseInt(t, 10));
  let value = {
    tagIds: tagIds,
    tagIdExternalIdsMap: componentValue,
  };

  useEffect(() => {
    BApi.resource.getResourcesByKeys({ ids: resourceIds }).then(r => {
      // @ts-ignore
      setResources(r.data || []);
    });
  }, []);

  return (
    <Modal
      defaultVisible
      size={'xl'}
      title={t('Bind tags to resources')}
      onOk={async () => {
        const resourceTagMap = resources.reduce((s, resource) => {
          s[resource.id] = [];
          Object.keys(value.tagIdExternalIdsMap)
            .forEach((t) => {
              const tagId = parseInt(t, 10);
              const list = value.tagIdExternalIdsMap[tagId] || [];
              if (list.includes(resource.id)) {
                s[resource.id].push(tagId);
              }
            });
          return s;
        }, {});

        const rsp = await BApi.resource.updateResourceTags({
          resourceTagIds: resourceTagMap,
        });
        if (!rsp.code) {
          for (const r of resources) {
            if (r.handler) {
              r.handler.reload();
            }
          }
        }
      }}
    >
      <TagSelector
        onChange={(v, tags) => { value = v; }}
        defaultValue={value}
        externalIds={resources.map(r => r.id)}
        enableCreation={false}
      />
    </Modal>
  );
};

ResourceTagBinderDialog.show = (props: IProps) => createPortalOfComponent(ResourceTagBinderDialog, props);

export default ResourceTagBinderDialog;

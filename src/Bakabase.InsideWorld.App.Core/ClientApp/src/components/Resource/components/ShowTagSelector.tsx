import { Dialog } from '@alifd/next';
import React from 'react';
import type ResourceDto from '@/core/models/Resource';
import TagSelector from '@/components/TagSelector';
import BApi from '@/sdk/BApi';
import type { IResourceHandler } from '@/components/Resource';

interface IResource extends ResourceDto {
  handler?: IResourceHandler;
}

const ShowTagSelector = (resources: IResource[]) => {
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
  return Dialog.show({
    width: 'auto',
    v2: true,
    closeMode: ['close', 'mask', 'esc'],
    centered: true,
    content: (
      <TagSelector
        onChange={(v, tags) => { value = v; }}
        defaultValue={value}
        externalIds={resources.map(r => r.id)}
        enableCreation={false}
      />
    ),
    onOk: () => {
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

      return BApi.resource.updateResourceTags({
        resourceTagIds: resourceTagMap,
      })
        .then((a) => {
          if (!a.code) {
            for (const r of resources) {
              if (r.handler) {
                r.handler.reload();
              }
            }
          }
        });
    },
  });
};

export default ShowTagSelector;

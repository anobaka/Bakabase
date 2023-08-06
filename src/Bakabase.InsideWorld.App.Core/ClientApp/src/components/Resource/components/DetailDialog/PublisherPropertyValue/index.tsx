import React from 'react';
import EditableTree from '@/components/EditableTree';
import Property from '@/components/Resource/components/DetailDialog/PropertyValue';
import { Message } from '@alifd/next';
import i18n from 'i18next';

const renderPublishers = (publishers = []) => {
  if (!publishers) {
    return;
  }
  const components = [];
  for (let i = 0; i < publishers.length; i++) {
    const publisher = publishers[i];
    const sub = publisher.subPublishers;
    let comp;
    if (sub?.length > 0) {
      comp = (
        <>
          <span
            className={'publisher'}
            onClick={() => {
              Message.notice(i18n.t('Under developing'));
            }}
          >{publisher.name}
          </span>({renderPublishers(sub)})
        </>
      );
    } else {
      comp = (<span
        className={'publisher'}
        onClick={() => {
          Message.notice(i18n.t('Under developing'));
        }}
      >{publisher.name}
              </span>);
    }
    components.push(comp);
    if (i < publishers.length - 1) {
      components.push(<>、</>);
    }
  }
  return components;
};

const buildTreeDataSource = (publishers, layer = 0) => {
  const dataSource = [];
  if (publishers) {
    publishers.forEach((p) => {
      dataSource.push({
        ...p,
        label: p.name,
        children: buildTreeDataSource(p.subPublishers, layer + 1),
      });
    });
  }
  return dataSource;
};

const mergeTreeDataSource = (publishers, treeDataSource) => {
  if (!(treeDataSource?.length > 0)) {
    return;
  }
  const result = [];
  const matchedTreeData = [];
  if (publishers) {
    for (const p of publishers) {
      const treeData = treeDataSource?.find((a) => a.id == p.id);
      matchedTreeData.push(treeData);
      if (treeData) {
        const np = {
          ...p,
          name: treeData.label,
          subPublishers: mergeTreeDataSource(p.subPublishers, treeData.children),
        };
        result.push(np);
      }
    }
  }
  const restTreeData = treeDataSource.filter((t) => matchedTreeData.indexOf(t) == -1);
  for (const p of restTreeData) {
    result.push({
      name: p.label,
      subPublishers: mergeTreeDataSource(undefined, p.children),
    });
  }

  for (const p of result) {
    if (!(p.subPublishers?.length > 0)) {
      delete p.subPublishers;
    }
  }

  return result;
};

export default ({ resource, reloadResource }) => {
  return (
    <Property
      requestKey={'publishers'}
      renderValue={() => renderPublishers(resource.publishers)}
      initValue={buildTreeDataSource(resource.publishers)}
      editable
      EditComponent={EditableTree}
      editComponentProps={{
      }}
      convertToRequesting={(v) => mergeTreeDataSource(resource.publishers, v)}
      resourceId={resource.id}
      reloadResource={reloadResource}
    />
  );
};

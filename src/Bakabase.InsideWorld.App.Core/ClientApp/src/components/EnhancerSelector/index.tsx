import React, { useEffect, useState } from 'react';
import './index.scss';
import i18n from 'i18next';
import IceLabel from '@icedesign/label';
import { Balloon, Icon, Message, Table } from '@alifd/next';
import { useUpdateEffect } from 'react-use';
import { GetAllEnhancers, GetComponentDescriptors } from '@/sdk/apis';
import { ComponentType, reservedResourceFileTypes, reservedResourceProperties } from '@/sdk/constants';
import SortableEnhancerList from '@/components/EnhancerSelector/components/SortableEnhancerList';
import BasicCategoryComponentSelector from '@/components/BasicCategoryComponentSelector';
import type {
  BakabaseInsideWorldModelsModelsDtosComponentDescriptor,
  BakabaseInsideWorldModelsModelsDtosResourceCategoryEnhancementOptions,
} from '@/sdk/Api';
import BApi from '@/sdk/BApi';
import { extractEnhancerTargetDescription } from '@/components/utils';

interface EnhancerSelectorValue extends BakabaseInsideWorldModelsModelsDtosResourceCategoryEnhancementOptions {
  enhancerKeys?: string[];
}

export default ({
                  defaultValue = {},
                  onChange = (v) => {
                  },
                }: {defaultValue?: EnhancerSelectorValue; onChange?: (v: EnhancerSelectorValue) => void}) => {
  const [value, setValue] = useState<EnhancerSelectorValue>(JSON.parse(JSON.stringify(defaultValue || {})));
  const [allEnhancers, setAllEnhancers] = useState<BakabaseInsideWorldModelsModelsDtosComponentDescriptor[]>([]);
  const [enhanceTargetDescriptions, setEnhanceTargetDescriptions] = useState({});

  useUpdateEffect(() => {
    onChange(value);
  }, [value]);

  const loadAllEnhancers = async () => {
    const rsp = await BApi.component.getComponentDescriptors({ type: ComponentType.Enhancer });
    if (rsp.data) {
      for (const t of rsp.data) {
        if (t.targets) {
          t.targets.forEach((x) => {
            if (!(x in enhanceTargetDescriptions)) {
              enhanceTargetDescriptions[x] = extractEnhancerTargetDescription(x);
            }
          });
        }
      }
    }
    setEnhanceTargetDescriptions({ ...enhanceTargetDescriptions });
    setAllEnhancers(rsp.data);
  };

  useEffect(() => {
    loadAllEnhancers();
  }, []);

  const renderPriorityTable = () => {
    console.log('Raw value', JSON.parse(JSON.stringify(value)));
    const enhancerKeys = value.enhancerKeys || [];
    const enhancementPriorities = value.enhancementPriorities || {};
    const enhancers = allEnhancers.filter(a => enhancerKeys.indexOf(a.id) > -1);
    const validEnhancerKeys = enhancers.map(e => e.id);
    const defaultPriority = (value.defaultPriority || []).filter(a => validEnhancerKeys!.indexOf(a) > -1);
    const newEnhancerKeys = validEnhancerKeys!.filter(a => defaultPriority.indexOf(a) == -1);
    defaultPriority.splice(defaultPriority.length, 0, ...newEnhancerKeys);

    let allTargets = enhancers.reduce((s, t) => {
      if (t.targets) {
        return s.concat(t.targets);
      }
      return s;
    }, []);
    allTargets = allTargets.filter((t, i) => allTargets.indexOf(t) == i);

    const tableDataSource = allTargets.map(t => {
      const currentTargetEnhancers = enhancers.filter(a => a.targets?.indexOf(t) > -1);
      const orderedEnhancers = enhancementPriorities[t]?.map(k => currentTargetEnhancers.find(a => a.id == k)).filter(x => x) ?? [];
      const restEnhancers = currentTargetEnhancers.filter(a => orderedEnhancers.indexOf(a) == -1);
      console.log(t, currentTargetEnhancers, orderedEnhancers, restEnhancers, enhancementPriorities[t]);
      return {
        target: t,
        enhancers: orderedEnhancers.concat(restEnhancers),
      };
    });

    if (enhancers.length > 0) {
      tableDataSource.splice(0, 0, {
        enhancers: defaultPriority.map(k => enhancers.find(a => a.id == k)),
      });
    }

    console.log(value, tableDataSource);

    return (
      <Table
        size={'small'}
        dataSource={tableDataSource}
        className={'priorities-table'}
        emptyContent={i18n.t('Please select at least one enhancer.').toString()}
      >
        <Table.Column
          title={i18n.t('Target')}
          dataIndex={'target'}
          width={'15%'}
          cell={(c) => {
            if (c == undefined) {
              return i18n.t('Default priority');
            }
            return (
              <>
                <IceLabel
                  inverse={false}
                  status={'info'}
                >{enhanceTargetDescriptions[c].type}
                </IceLabel>
                &nbsp;
                {enhanceTargetDescriptions[c].key}
              </>
            );
          }}
        />
        <Table.Column
          title={i18n.t('Priority')}
          dataIndex={'enhancers'}
          cell={(es, i, r) => {
            if (r.target == 'p:Tag') {
              return (
                <div className={'enhancer-orders'}>
                  {
                    es.map((e, i) => (
                      <>
                        <div className={'sortable-enhancer-order'}>{i18n.t(e.name)}</div>
                        {i != es.length - 1 && '∪'}
                      </>
                    ))
                  }
                </div>
              );
            }
            return (
              <SortableEnhancerList
                enhancers={es}
                axis={'x'}
                onSortEnd={({
                              newIndex,
                              oldIndex,
                            }) => {
                  // console.log(enhancementPriorities, orders);
                  const orders = es.map(e => e.id);
                  const [removed] = orders.splice(oldIndex, 1);
                  orders.splice(newIndex, 0, removed);

                  if (i == 0) {
                    setValue({
                      ...value,
                      defaultPriority: orders,
                    });
                  } else {
                    setValue({
                      ...value,
                      enhancementPriorities: {
                        ...(value?.enhancementPriorities || {}),
                        [r.target]: orders,
                      },
                    });
                  }
                }}
              />
            );
          }}
        />
      </Table>
    );
  };

  return (
    <div className={'enhancer-selector'}>
      <div className={'enhancers'}>
        <BasicCategoryComponentSelector
          maxCount={99999}
          onChange={keys => {
            value.defaultPriority = value.defaultPriority?.filter(d => keys?.indexOf(d) > -1) ?? keys;
            const enhancers = allEnhancers.filter(e => keys?.indexOf(e.id) > -1);
            const tmpTargets = enhancers.reduce((s, t) => s.concat(t.targets || []), []);
            const targets = tmpTargets.filter((t, i) => tmpTargets.indexOf(t) == i);
            if (targets.length > 0) {
              value.enhancementPriorities ??= {};
              for (const t of targets) {
                if (!(t in value.enhancementPriorities)) {
                  value.enhancementPriorities[t] = [];
                }
                const currentTargetEnhancerKeys = enhancers.filter(e => e.targets?.indexOf(t) > -1).map(e => e.id);
                const orders = value.enhancementPriorities[t].filter(a => currentTargetEnhancerKeys.indexOf(a) > -1);
                const unknownEnhancerKeys = currentTargetEnhancerKeys.filter(s => orders.indexOf(s) == -1);
                value.enhancementPriorities[t] = orders.concat(unknownEnhancerKeys);
              }
            } else {
              value.enhancementPriorities = undefined;
            }
            value.enhancerKeys = keys;
            console.log(value);
            setValue({
              ...value,
            });
          }}
          componentType={ComponentType.Enhancer}
          value={value.enhancerKeys}
        />
      </div>
      {value.enhancerKeys?.length > 0 && (
        <>
          <h3>
            {i18n.t('Set enhancement target priorities')}
          </h3>
          <Message type={'notice'}>
            {i18n.t('You can change the priorities of enhancements by dragging and dropping.')}
            <br />
            {i18n.t('Enhancements will be saved only if it\'s not exist, and the system will use priorities below to fill properties or files of resources.')}
          </Message>
          {renderPriorityTable()}
        </>
      )}
    </div>
  );
};

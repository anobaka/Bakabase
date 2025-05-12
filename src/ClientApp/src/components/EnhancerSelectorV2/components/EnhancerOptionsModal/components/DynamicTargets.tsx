import { ApartmentOutlined, PlusCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useEffect, useRef, useState } from 'react';
import { useUpdate, useUpdateEffect } from 'react-use';
import _ from 'lodash';
import type { EnhancerFullOptions, EnhancerTargetFullOptions } from '../models';
import { createEnhancerTargetOptions } from '../models';
import type { EnhancerDescriptor, EnhancerTargetDescriptor } from '../../../models';
import TargetRow from './TargetRow';
import type { IProperty } from '@/components/Property/models';
import { Button, Divider, Popover, Table, TableBody, TableColumn, TableHeader, Tooltip } from '@/components/bakaui';
import type { PropertyPool } from '@/sdk/constants';
import { buildLogger, generateNextWithPrefix } from '@/components/utils';
import OtherOptionsTip
  from '@/components/EnhancerSelectorV2/components/EnhancerOptionsModal/components/OtherOptionsTip';
import type { PropertyMap } from '@/components/types';

type Props = {
  propertyMap?: PropertyMap;
  enhancer: EnhancerDescriptor;
  onPropertyChanged?: () => any;
  optionsList?: EnhancerTargetFullOptions[];
  onChange?: (options: EnhancerTargetFullOptions[]) => any;
};

type Group = {
  descriptor: EnhancerTargetDescriptor;
  subOptions: EnhancerTargetFullOptions[];
};

const log = buildLogger('DynamicTargets');

const buildGroups = (descriptors: EnhancerTargetDescriptor[], optionsList?: EnhancerTargetFullOptions[]) => {
  return descriptors.map(descriptor => {
    const subOptions = optionsList?.filter(x => x.target == descriptor.id) || [];
    let defaultOptions = subOptions.find(x => x.dynamicTarget == undefined);
    if (defaultOptions == undefined) {
      defaultOptions = createEnhancerTargetOptions(descriptor);
    } else {
      const defaultIdx = subOptions.findIndex(x => x == defaultOptions);
      subOptions.splice(defaultIdx, 1);
    }
    subOptions.splice(0, 0, defaultOptions);
    return {
      descriptor: descriptor,
      subOptions: subOptions,
    };
  });
};

export default (props: Props) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const {
    propertyMap,
    optionsList,
    enhancer,
    onPropertyChanged,
    onChange,
  } = props;
  const dynamicTargetDescriptors = enhancer.targets.filter(x => x.isDynamic);
  const [groups, setGroups] = useState<Group[]>([]);

  useEffect(() => {
    setGroups(buildGroups(dynamicTargetDescriptors, optionsList));
  }, []);

  const updateGroups = (groups: Group[]) => {
    setGroups([...groups]);

    const ol = _.flatMap(groups, g => g.subOptions);
    onChange?.(ol);
  };

  // log('rendering', optionsList, groups);

  return groups.length > 0 ? (
    <div className={'flex flex-col gap-y-2'}>
      {groups.map(g => {
        const {
          descriptor,
          subOptions,
        } = g;
        return (
          <div>
            {/* NextUI doesn't support the wrap of TableRow, use div instead for now, waiting the updates of NextUI */}
            {/* see https://github.com/nextui-org/nextui/issues/729 */}
            <Table removeWrapper aria-label={'Dynamic target'}>
              <TableHeader>
                <TableColumn width={'41.666667%'}>
                  {descriptor.name}
                  &nbsp;
                  <Popover trigger={(
                    <ApartmentOutlined className={'text-medium'} />
                  )}
                  >
                    {t('This is not a fixed enhancement target, which will be replaced with other content when data is collected')}
                  </Popover>
                </TableColumn>
                <TableColumn width={'25%'}>{t('Save as property')}</TableColumn>
                <TableColumn width={'25%'}>
                  <div className={'flex items-center gap-1'}>
                    {t('Other options')}
                    <OtherOptionsTip />
                  </div>
                </TableColumn>
                <TableColumn>{t('Operations')}</TableColumn>
              </TableHeader>
              {/* @ts-ignore */}
              <TableBody />
            </Table>
            <div className={'flex flex-col gap-y-2'}>
              {subOptions.map((data, i) => {
                return (
                  <>
                    <TargetRow
                      key={i}
                      dynamicTarget={data.dynamicTarget}
                      options={data}
                      descriptor={descriptor}
                      propertyMap={propertyMap}
                      onPropertyChanged={onPropertyChanged}
                      onDeleted={() => {
                        subOptions?.splice(i, 1);
                        updateGroups(groups);
                      }}
                      onChange={(newOptions) => {
                        subOptions[i] = newOptions;
                        updateGroups(groups);
                      }}
                    />
                    <Divider orientation={'horizontal'} />
                  </>
                );
              })}
            </div>
            <Button
              size={'sm'}
              variant={'light'}
              color={'success'}
              onPress={() => {
                const currentTargets = subOptions.filter(x => x.dynamicTarget != undefined).map(x => x.dynamicTarget!);
                const nextTarget = generateNextWithPrefix(t('Target'), currentTargets);
                const newOptions = createEnhancerTargetOptions(descriptor);
                newOptions.dynamicTarget = nextTarget;
                subOptions.push(newOptions);
                setGroups([...groups]);
              }}
            >
              <PlusCircleOutlined className={'text-sm'} />
              {t('Specify dynamic target')}
            </Button>
          </div>
        );
      })}
    </div>
  ) : null;
};

import { PlusCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { useUpdate } from 'react-use';
import type { EnhancerFullOptions, EnhancerTargetFullOptions } from '../../models';
import { defaultCategoryEnhancerTargetOptions } from '../../models';
import type { EnhancerDescriptor, EnhancerTargetDescriptor } from '../../../../models';
import DynamicTargetLabel from '../DynamicTargetLabel';
import TargetRow from '../TargetRow';
import type { IProperty } from '@/components/Property/models';
import { Button, Table, TableBody, TableColumn, TableHeader } from '@/components/bakaui';
import BApi from '@/sdk/BApi';

const sortOptions = (a: EnhancerTargetFullOptions, b: EnhancerTargetFullOptions) => {
  if (a.dynamicTarget == undefined) {
    return -1;
  }
  if (b.dynamicTarget == undefined) {
    return 1;
  }
  return a.dynamicTarget.localeCompare(b.dynamicTarget);
};

interface Props {
  propertyMap?: { [key: number]: IProperty };
  options?: EnhancerFullOptions;
  category: { name: string; id: number; customPropertyIds?: number[] };
  enhancer: EnhancerDescriptor;
}

type Group = {
  descriptor: EnhancerTargetDescriptor;
  subOptions: EnhancerTargetFullOptions[];
};

export default (props: Props) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const {
    propertyMap,
    options: propsOptions,
    category,
    enhancer,
  } = props;
  const dynamicTargetDescriptors = enhancer.targets.filter(x => x.isDynamic);
  const [groups, setGroups] = useState<Group[]>([]);
  const [options, setOptions] = useState(propsOptions ?? {});

  useEffect(() => {
  }, []);

  useEffect(() => {
    updateGroups();
  }, [options]);

  const updateGroups = () => {
    const newGroups = dynamicTargetDescriptors.map(descriptor => {
      const subOptions = options.targetOptions?.filter(x => x.target == descriptor.id) || [];
      let defaultOptions = subOptions.find(x => x.dynamicTarget == undefined);
      if (defaultOptions == undefined) {
        defaultOptions = defaultCategoryEnhancerTargetOptions(descriptor);
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
    setGroups(newGroups);
    console.log('updateGroups', newGroups);
  };

  const createNewOptions = (descriptor: EnhancerTargetDescriptor, otherOptions: EnhancerTargetFullOptions[] | undefined) => {
    const options: EnhancerTargetFullOptions = defaultCategoryEnhancerTargetOptions(descriptor);
    let maxNo = 0;
    if (otherOptions != undefined) {
      const regex = new RegExp(String.raw`^${t('Target')}(?<no>\d+)$`, 'g');
      for (const o of otherOptions) {
        if (o.dynamicTarget != undefined) {
          regex.lastIndex = 0;
          const match = regex.exec(o.dynamicTarget)?.groups?.no;
          if (match != undefined) {
            const no = parseInt(match, 10);
            // console.log(`${o.dynamicTarget}`, no);
            if (no > maxNo) {
              maxNo = no;
            }
          }
        }
      }
    }
    // console.log('current max no', maxNo, otherOptions);
    options.dynamicTarget = `${t('Target')}${maxNo + 1}`;
    return options;
  };

  console.log('rendering', options, groups);

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
                  <DynamicTargetLabel />
                </TableColumn>
                <TableColumn width={'25%'}>{t('Save as property')}</TableColumn>
                <TableColumn width={'25%'}>{t('Other options')}</TableColumn>
                <TableColumn >{t('Operations')}</TableColumn>
              </TableHeader>
              {/* @ts-ignore */}
              <TableBody />
            </Table>
            <div className={'flex flex-col gap-y-2'}>
              {subOptions.map((data, i) => {
                return (
                  <TargetRow
                    key={i}
                    target={data.target}
                    dynamicTarget={data.dynamicTarget}
                    options={data}
                    descriptor={descriptor}
                    category={category}
                    propertyMap={propertyMap}
                    enhancer={enhancer}
                    onDeleted={() => {
                      const idx = options.targetOptions?.findIndex(x => x == data);
                      if (idx != undefined) {
                        options.targetOptions?.splice(idx, 1);
                        setOptions({ ...options });
                      }
                    }}
                    onChange={(newOptions) => {
                      Object.assign(data, newOptions);
                      forceUpdate();
                    }}
                  />
                );
              })}
            </div>
            <Button
              size={'sm'}
              variant={'light'}
              color={'success'}
              onClick={async () => {
                const newTargetOptions = createNewOptions(descriptor, subOptions);
                await BApi.category.patchCategoryEnhancerTargetOptions(category.id, enhancer.id, { target: descriptor.id, dynamicTarget: newTargetOptions.dynamicTarget }, newTargetOptions);
                options.targetOptions ??= [];
                options.targetOptions.push(newTargetOptions);
                setOptions({ ...options });
                console.log('updating options', options);
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

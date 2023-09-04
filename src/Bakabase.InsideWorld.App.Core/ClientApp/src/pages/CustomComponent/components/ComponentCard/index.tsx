import './index.scss';
import type { DOMAttributes } from 'react';
import i18n from 'i18next';
import { Balloon, Button, Dialog } from '@alifd/next';
import { useEffect, useRef, useState } from 'react';
import IceLabel from '@icedesign/label';
import { useTranslation } from 'react-i18next';
import type { BakabaseInsideWorldModelsModelsDtosComponentDescriptor } from '@/sdk/Api';
import CustomIcon from '@/components/CustomIcon';
import { ComponentDescriptorType, ComponentType } from '@/sdk/constants';
import Detail from '@/pages/CustomComponent/Detail';
import BApi from '@/sdk/BApi';
import ComponentDetail from '@/pages/CustomComponent/Detail';
import { extractEnhancerTargetDescription } from '@/components/utils';
import ClickableIcon from '@/components/ClickableIcon';
import SimpleLabel from '@/components/SimpleLabel';

interface DescriptorCardProps extends DOMAttributes<unknown> {
  descriptor: BakabaseInsideWorldModelsModelsDtosComponentDescriptor;
  selected?: boolean;
  onDeleted?: () => void;
}

const TypeLabelProps = {
  [ComponentDescriptorType.Fixed]: {
    status: 'default',
    label: 'Reserved',
  },
  [ComponentDescriptorType.Instance]: {
    status: 'info',
    label: 'Custom',
  },
};

export default (props: DescriptorCardProps) => {
  const { t } = useTranslation();
  const {
    descriptor: propsDescriptor,
    selected,
    onDeleted,
    ...otherProps
  } = props;

  const [descriptor, setDescriptor] = useState(propsDescriptor);
  const labelProps = TypeLabelProps[descriptor.type];
  const domRef = useRef();

  useEffect(() => {
    setDescriptor(propsDescriptor);
  }, [propsDescriptor]);

  console.log('Rendering', props);

  const renderExtra = () => {
    switch (descriptor.componentType) {
      case ComponentType.Enhancer: {
        if (descriptor.targets?.length > 0) {
          const descriptions = descriptor.targets.map(t => extractEnhancerTargetDescription(t));
          const descriptionGroups = descriptions.reduce<{type: string; keys: string[]}[]>((s, t) => {
            let g = s.find(g => g.type == t.type);
            if (g == undefined) {
              g = {
                type: t.type,
                keys: [],
              };
              s.push(g);
            }
            g.keys.push(t.key);
            return s;
          }, []);
          return (
            <div className={'target-groups'}>
              {descriptionGroups.map((t, i) => {
                return (
                  <div className={'group'}>
                    {t.type}
                    {t.keys.map(k => (<SimpleLabel status={'default'}>{k}</SimpleLabel>))}
                  </div>
                );
              })}
            </div>
          );
        }
        return;
      }
      default:
        return;
    }
  };

  const categories = descriptor?.associatedCategories ?? [];

  return (
    <div className={`component-card ${selected ? 'selected' : ''}`} {...otherProps} ref={domRef}>
      {selected && (
        <CustomIcon className={'selected-icon'} type={'check-circle'} size={'large'} />
      )}
      {descriptor.type == ComponentDescriptorType.Instance && (
        <div className={'top-right-operations'}>
          <div
            className={'edit'}
            onClick={e => {
              e.preventDefault();
              e.stopPropagation();
              ComponentDetail.show({
                componentType: descriptor.componentType,
                componentKey: descriptor.id,
                onClosed: hasChanges => {
                  if (hasChanges) {
                    BApi.component.getComponentDescriptorByKey({ key: descriptor.optionsId })
                      .then(a => {
                        setDescriptor(a.data);
                      });
                  }
                },
              });
            }}
          >
            <ClickableIcon colorType={'normal'} type={'edit-square'} />
          </div>
          <div
            className={'delete'}
            onClick={e => {
              e.preventDefault();
              e.stopPropagation();
              Dialog.confirm({
                title: t('Sure to delete?'),
                content: t(''),
                v2: true,
                closeMode: ['mask', 'esc', 'close'],
                onOk: () => BApi.componentOptions.removeComponentOptions(descriptor.optionsId).then(a => {
                  if (!a.code) {
                    if (onDeleted) {
                      onDeleted();
                    }
                  }
                }),
              });
            }}
          >
            <ClickableIcon colorType={'danger'} type={'delete'} />
          </div>
        </div>
      )}
      <div className="top">
        <div className="name">
          {labelProps && (
            <>
              <SimpleLabel status={labelProps.status}>{t(labelProps.label)}</SimpleLabel>
              &nbsp;
            </>
          )}
          {t(descriptor.name)}
        </div>
        {descriptor.description && <div className="description">{t(descriptor.description)}</div>}
        {renderExtra()}
      </div>
      <div className="bottom">
        {categories.length > 0 && (
          <div className="categories">
            <div className="label">
              {t('Applied to {{count}} categories', { count: categories.length })}
            </div>
          </div>
        )}
        <div className="versions">
          <SimpleLabel
            status={'default'}
            className={`version ${descriptor.version?.length > 0 ? '' : 'empty'}`}
            title={`${t('Version')}:${t('May be different in incoming versions of app')}`}
          >{descriptor.version}</SimpleLabel>
          <SimpleLabel
            className={`version ${descriptor.dataVersion?.length > 0 ? '' : 'empty'}`}
            title={`${t('Data version')}:${t('May be different after configuration change')}`}
          >{descriptor.dataVersion}</SimpleLabel>
        </div>
      </div>
    </div>
  );
};

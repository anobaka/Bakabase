import './index.scss';
import type { DOMAttributes } from 'react';
import i18n from 'i18next';
import { Balloon, Button, Dialog } from '@alifd/next';
import { useEffect, useRef, useState } from 'react';
import IceLabel from '@icedesign/label';
import type { BakabaseInsideWorldModelsModelsDtosComponentDescriptor } from '@/sdk/Api';
import CustomIcon from '@/components/CustomIcon';
import { ComponentDescriptorType, ComponentType } from '@/sdk/constants';
import Detail from '@/pages/CustomComponent/Detail';
import BApi from '@/sdk/BApi';
import ComponentDetail from '@/pages/CustomComponent/Detail';
import { extractEnhancerTargetDescription } from '@/components/utils';
import ClickableIcon from '@/components/ClickableIcon';

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
          return (
            <div className={'targets'}>
              {descriptor.targets.map((t, i) => {
                const description = extractEnhancerTargetDescription(t);
                return (
                  <div className={'target'} key={i}>
                    {description.type}:{description.key}
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
                title: i18n.t('Sure to delete?'),
                content: i18n.t(''),
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
            <IceLabel inverse={false} status={labelProps.status}>{i18n.t(labelProps.label)}</IceLabel>
          )}
          {i18n.t(descriptor.name)}
        </div>
        {descriptor.description && <div className="description">{i18n.t(descriptor.description)}</div>}
        {renderExtra()}
      </div>
      <div className="bottom">
        {categories.length > 0 && (
          <div className="categories">
            <div className="label">
              {i18n.t('Applied to')}
            </div>
            {categories.map(c => (
              <div className={'category'} key={c.id}>
                {c.name}
              </div>
          ))}
          </div>
        )}
        <div className="versions">
          <div
            className={`version ${descriptor.version?.length > 0 ? '' : 'empty'}`}
            title={`${i18n.t('Version')}:${i18n.t('May be different in incoming versions of app')}`}
          >{descriptor.version}</div>
          <div
            className={`version ${descriptor.dataVersion?.length > 0 ? '' : 'empty'}`}
            title={`${i18n.t('Data version')}:${i18n.t('May be different after configuration change')}`}
          >{descriptor.dataVersion}</div>
        </div>
      </div>
    </div>
  );
};

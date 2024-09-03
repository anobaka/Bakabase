import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { PlusCircleOutlined } from '@ant-design/icons';
import BApi from '@/sdk/BApi';
import { ComponentDescriptorAdditionalItem, ComponentDescriptorType, ComponentType, componentTypes } from '@/sdk/constants';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import type { CustomComponentDetailDialogProps } from '@/pages/CustomComponent/Detail';
import Detail from '@/pages/CustomComponent/Detail';
import ComponentDescriptorCard from '@/pages/CustomComponent/components/ComponentCard';
import FeatureStatusTip from '@/components/FeatureStatusTip';
import { Button } from '@/components/bakaui';

const ComponentTypeIcons = {
  [ComponentType.Enhancer]: 'flashlight',
  [ComponentType.Player]: 'play-circle',
  [ComponentType.PlayableFileSelector]: 'filesearch',
};
export default () => {
  const { t } = useTranslation();
  const [allComponents, setAllComponents] = useState([]);

  const [selectedComponent, setSelectedComponent] = useState<CustomComponentDetailDialogProps>();

  const loadAllComponents = async () => {
    const rsp = await BApi.component.getComponentDescriptors({ additionalItems: ComponentDescriptorAdditionalItem.AssociatedCategories });
    setAllComponents(rsp.data);
  };

  useEffect(() => {
    loadAllComponents();
  }, []);

  const showDetail = (componentType, componentKey) => {
    setSelectedComponent({
      componentType,
      componentKey,
      onClosed: () => {
        setSelectedComponent(undefined);
      },
    });
  };

  return (
    <div className={'custom-component-page'}>
      {selectedComponent && (
        <Detail
          {...selectedComponent}
          onClosed={hasChanges => {
            setSelectedComponent(undefined);
            if (hasChanges) {
              loadAllComponents();
            }
          }}
        />
      )}
      {componentTypes.filter(x => x.value != ComponentType.Enhancer).map(ct => {
        const components = allComponents.filter(c => c.componentType == ct.value && c.type == ComponentDescriptorType.Instance);
        return (
          <div className={'component-type'} key={ct.value}>
            <div className="type-name">
              <div className="name flex items-center gap-1">
                <CustomIcon type={ComponentTypeIcons[ct.value]} size={'large'} />
                {t(ct.label)}
              </div>
              <Button
                color={'primary'}
                variant={'light'}
                onClick={() => {
                    showDetail(ct.value);
                  }}
              >
                <PlusCircleOutlined className={'text-base'} />
                {t('Add')}
              </Button>
            </div>
            {(components.length > 0 ? (<div className="components">
              {components.map(c => {
                return (
                  <ComponentDescriptorCard
                    key={c.id}
                    descriptor={c}
                    onDeleted={() => {
                      loadAllComponents();
                    }}
                  />
                );
              })}
            </div>) : (
              <div className={'no-components'}>
                {t('Nothing here yet')}
              </div>
            ))}
          </div>
        );
      })}
    </div>
  );
};

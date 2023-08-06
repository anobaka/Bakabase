import { useEffect, useState } from 'react';
import i18n from 'i18next';
import BApi from '@/sdk/BApi';
import { ComponentDescriptorAdditionalItem, ComponentDescriptorType, ComponentType, componentTypes } from '@/sdk/constants';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import type { CustomComponentDetailDialogProps } from '@/pages/CustomComponent/Detail';
import Detail from '@/pages/CustomComponent/Detail';
import ComponentDescriptorCard from '@/pages/CustomComponent/components/ComponentCard';
import { Button } from '@alifd/next';

const ComponentTypeIcons = {
  [ComponentType.Enhancer]: 'flashlight',
  [ComponentType.Player]: 'play-circle',
  [ComponentType.PlayableFileSelector]: 'filesearch',
};
export default () => {
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
      {componentTypes.map(t => {
        const components = allComponents.filter(c => c.componentType == t.value && c.type == ComponentDescriptorType.Instance);
        return (
          <div className={'component-type'} key={t.value}>
            <div className="type-name">
              <div className="name">
                <CustomIcon type={ComponentTypeIcons[t.value]} size={'large'} />
                {i18n.t(t.label)}
              </div>
              <Button
                type={'primary'}
                text
                className="add"
                onClick={() => {
                  showDetail(t.value);
                }}
              >
                <CustomIcon type={'plus-circle'} />
                {i18n.t('Add')}
              </Button>
            </div>
            {components.length > 0 ? (<div className="components">
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
                {i18n.t('Nothing here yet')}
              </div>
            )}

          </div>
        );
      })}
    </div>
  );
};

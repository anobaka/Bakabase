import { Button, Dialog, Input, Overlay, Progress, Select, Switch } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import { createPortalOfComponent } from '@/components/utils';
import { CustomPropertyAdditionalItem, CustomPropertyType } from '@/sdk/constants';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import BApi from '@/sdk/BApi';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import SingleProperty from '@/pages/CustomProperty/components/SingleProperty';

const { Popup } = Overlay;

interface IProps extends DialogProps {
  category: { id: number; name: string };
  onSaved?: () => any;
}

const CategoryCustomPropertyBinderDialog = ({
                                              category,
                                              onSaved,
                                              ...dialogProps
                                            }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);

  const [properties, setProperties] = useState<ICustomProperty[]>([]);
  const [selectedPropertyIds, setSelectedPropertyIds] = useState<number[]>();

  const loadProperties = async () => {
    const rsp = await BApi.customProperty.getAllCustomPropertiesV2({ additionalItems: CustomPropertyAdditionalItem.Category });
    // @ts-ignore
    setProperties(rsp.data || []);
  };

  useUpdateEffect(() => {
    if (selectedPropertyIds == undefined) {
      setSelectedPropertyIds(properties.filter(p => p.categories?.some(c => c.id === category.id)).map(p => p.id));
    }
  }, [properties]);

  useEffect(() => {
    loadProperties();
  }, []);

  const close = () => {
    setVisible(false);
  };

  return (
    <Dialog
      visible={visible}
      className={'category-custom-property-binder-dialog'}
      onClose={close}
      onCancel={close}
      v2
      closeMode={['close', 'mask', 'esc']}
      onOk={async () => {
        const rsp = await BApi.resourceCategory.bindCustomPropertiesToCategory(category.id
        , { customPropertyIds: selectedPropertyIds });
        if (!rsp.code) {
          onSaved?.();
          close();
        }
      }}
      title={t('Binding custom properties to category {{categoryName}}', { categoryName: category.name })}
      style={{ minWidth: '800px' }}
      {...dialogProps}
    >
      <div className={'custom-properties'}>
        <div className="selected">
          <div className="title">{t('Selected')}</div>
          <div className="list">
            {selectedPropertyIds?.map(id => {
              const property = properties.find(p => p.id === id);
              if (!property) return null;
              return (
                <SingleProperty
                  property={property}
                  key={id}
                  onClick={() => {
                    setSelectedPropertyIds(selectedPropertyIds.filter(pId => pId !== id));
                  }}
                />
              );
            })}
          </div>
        </div>
        <div className="not-selected">
          <div className="title">{t('Not selected')}</div>
          <div className="list">
            {properties.filter(p => selectedPropertyIds?.includes(p.id) != true).map(p => {
              return (
                <SingleProperty
                  property={p}
                  key={p.id}
                  onClick={() => setSelectedPropertyIds([...(selectedPropertyIds || []), p.id])}
                />
              );
            })}
          </div>
        </div>
      </div>
    </Dialog>
  );
};


CategoryCustomPropertyBinderDialog.show = (props: IProps) => createPortalOfComponent(CategoryCustomPropertyBinderDialog, props);

export default CategoryCustomPropertyBinderDialog;

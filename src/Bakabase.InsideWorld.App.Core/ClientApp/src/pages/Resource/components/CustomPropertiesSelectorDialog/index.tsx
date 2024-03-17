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
  onSelected?: (ids?: number[]) => any;
  selectedPropertyIds?: number[];
}

const CustomPropertiesSelectorDialog = ({
                                        selectedPropertyIds: propSelectedPropertyIds,
                                          onSelected,
                                              ...dialogProps
                                            }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);

  const [properties, setProperties] = useState<ICustomProperty[]>([]);
  const [selectedPropertyIds, setSelectedPropertyIds] = useState<number[]>(propSelectedPropertyIds || []);

  const loadProperties = async () => {
    const rsp = await BApi.customProperty.getAllCustomPropertiesV2({ additionalItems: CustomPropertyAdditionalItem.Category });
    // @ts-ignore
    setProperties(rsp.data || []);
  };

  useEffect(() => {
    loadProperties();
  }, []);

  const close = () => {
    setVisible(false);
  };

  return (
    <Dialog
      visible={visible}
      className={'resource-page-custom-property-selector-dialog'}
      onClose={close}
      onCancel={close}
      v2
      closeMode={['close', 'mask', 'esc']}
      onOk={async () => {
        onSelected?.(selectedPropertyIds);
        close();
      }}
      title={t('Select custom properties to search')}
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


CustomPropertiesSelectorDialog.show = (props: IProps) => createPortalOfComponent(CustomPropertiesSelectorDialog, props);

export default CustomPropertiesSelectorDialog;

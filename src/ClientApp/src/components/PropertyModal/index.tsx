import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import ModalContent from './components/ModalContent';
import { buildLogger, createPortalOfComponent } from '@/components/utils';
import type { PropertyType } from '@/sdk/constants';
import './index.scss';
import BApi from '@/sdk/BApi';
import { Modal } from '@/components/bakaui';
import type { IProperty } from '@/components/Property/models';
import type { DestroyableProps } from '@/components/bakaui/types';

type Props = {
  value?: CustomPropertyForm;
  onSaved?: (property: IProperty) => any;
  validValueTypes?: PropertyType[];
} & DestroyableProps;

type CustomPropertyForm = {
  id?: number;
  name: string;
  type: PropertyType;
  options?: any;
};

const log = buildLogger('PropertyModal');

export default ({
                          value,
                          onSaved,
                          validValueTypes,
                          ...props
                        }: Props) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [property, setProperty] = useState<CustomPropertyForm>();

  const close = () => {
    setVisible(false);
  };

  log(property);

  return (
    <Modal
      size={'lg'}
      visible={visible}
      title={t('Custom property')}
      onClose={close}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          isDisabled: !property,
        },
      }}
      onOk={async () => {
        if (!property) {
          throw new Error('Property is required');
        }
        const model = {
          ...property,
          // todo: this serialization is a hardcode
          options: JSON.stringify(property.options),
        };

        const isUpdate = (property.id != undefined && property.id > 0);

        const rsp = isUpdate
          ? await BApi.customProperty.putCustomProperty(property.id!, model)
          : await BApi.customProperty.addCustomProperty(model);
        if (!rsp.code) {
          // console.log('on saved', onSaved);
          onSaved?.(rsp.data!);
          close();
        }
      }}
      {...props}
    >
      <ModalContent
        value={value}
        validValueTypes={validValueTypes}
        onChange={v => setProperty(v)}
      />
    </Modal>
  );
};

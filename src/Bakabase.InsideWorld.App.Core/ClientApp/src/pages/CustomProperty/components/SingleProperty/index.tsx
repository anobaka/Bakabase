import { useTranslation } from 'react-i18next';
import PropertyDialog from '@/pages/CustomProperty/components/PropertyDialog';
import CustomIcon from '@/components/CustomIcon';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import { PropertyTypeIconMap } from '@/pages/CustomProperty/models';
import ClickableIcon from '@/components/ClickableIcon';
import SimpleLabel from '@/components/SimpleLabel';
import './index.scss';
import { Dialog } from '@alifd/next';
import BApi from '@/sdk/BApi';
interface IProps {
  property: ICustomProperty;
  onSaved?: (property: ICustomProperty) => any;
  onRemoved?: () => any;
  onClick?: () => any;
}

export default ({ property, onSaved, onClick, onRemoved }: IProps) => {
  const { t } = useTranslation();

  return (
    <div
      key={property.id}
      className="standalone-custom-property"
      onClick={onClick}
    >
      <div className="line1">
        <div className="left">
          <div className="name">{property.name}</div>
          <div className="type">
            <CustomIcon type={PropertyTypeIconMap[property.type]} size={'small'} />
          </div>
        </div>
        <div className="right">
          <ClickableIcon
            colorType={'normal'}
            size={'small'}
            type={'edit-square'}
            onClick={() => {
              PropertyDialog.show({
                value: property,
                onSaved: onSaved,
              });
            }}
          />
          <ClickableIcon
            colorType={'danger'}
            size={'small'}
            type={'delete'}
            onClick={async () => {
              Dialog.confirm({
                title: t('Removing property'),
                content: t('This operation can not be undone, are you sure?'),
                v2: true,
                width: 'auto',
                closeMode: ['close', 'mask', 'esc'],
                onOk: async () => {
                  await BApi.customProperty.removeCustomProperty(property.id);
                  onRemoved?.();
                },
              });
            }}
          />
        </div>
      </div>
      <div className="categories">
        {property.categories?.map(c => {
          return (
            <SimpleLabel key={c.id} className="category">
              {c.name}
            </SimpleLabel>
          );
        }) ?? t('No category bound')}
      </div>
    </div>
  );
};

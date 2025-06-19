import { useTranslation } from 'react-i18next';
import type { IProperty } from '@/components/Property/models';
import { Chip } from '@/components/bakaui';
import { PropertyPool } from '@/sdk/constants';
import PropertyTypeIcon from '@/components/Property/components/PropertyTypeIcon';

type SimpleProperty = Pick<IProperty, 'name' | 'type' | 'pool'>;

interface IProps {
  property: SimpleProperty;
  showPool?: boolean;
}

export default ({ property, showPool }: IProps) => {
const { t } = useTranslation();
  return (
    <div className={'inline-flex items-center'}>
      {showPool && (
        <Chip
          size={'sm'}
          radius={'sm'}
          variant={'flat'}
        >
          {t(`PropertyPool.${PropertyPool[property.pool]}`)}
        </Chip>
      )}
      <PropertyTypeIcon type={property.type} textVariant={'none'} />
      <span>{property.name}</span>
    </div>
  );
};

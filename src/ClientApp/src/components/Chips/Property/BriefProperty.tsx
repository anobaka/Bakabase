import { useTranslation } from 'react-i18next';
import PropertyPoolIcon from '@/components/Property/components/PropertyPoolIcon';
import PropertyTypeIcon from '@/components/Property/components/PropertyTypeIcon';
import type { IProperty } from '@/components/Property/models';
import { Chip } from '@/components/bakaui';

type Props = {
  property?: Pick<IProperty, 'pool' | 'type' | 'name'>;
  hideType?: boolean;
};

export default ({ property, hideType }: Props) => {
  const { t } = useTranslation();
  return (
    <div className="flex items-center gap-1">
      {property ? (
        <>
          <PropertyPoolIcon pool={property.pool} />
          {!hideType && (
            <PropertyTypeIcon type={property.type} />
          )}
          <Chip
            size="sm"
            variant={'flat'}
            radius={'sm'}
          >{property.name}</Chip>
        </>
      ) : (t('Unknown property'))}
    </div>
  );
};

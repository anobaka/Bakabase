import { useTranslation } from 'react-i18next';
import PropertyPoolIcon from '@/components/Property/components/PropertyPoolIcon';
import PropertyTypeIcon from '@/components/Property/components/PropertyTypeIcon';
import type { IProperty } from '@/components/Property/models';

type Props = {
  property?: Pick<IProperty, 'pool' | 'type' | 'name'>;
};

export default ({ property }: Props) => {
  const { t } = useTranslation();
  return (
    <div className="flex items-center gap-1">
      {property ? (
        <>
          <PropertyPoolIcon pool={property.pool} />
          <PropertyTypeIcon type={property.type} />
          {property.name}
        </>
      ) : (t('Unknown property'))}
    </div>
  );
};

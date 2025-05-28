import PropertyPoolIcon from '@/components/Property/components/PropertyPoolIcon';
import PropertyTypeIcon from '@/components/Property/components/PropertyTypeIcon';
import type { IProperty } from '@/components/Property/models';

type Props = {
  property: Pick<IProperty, 'pool' | 'type' | 'name'>;
};

export default ({ property }: Props) => {
  return (
    <div className="flex items-center gap-1">
      <PropertyPoolIcon pool={property.pool} />
      <PropertyTypeIcon type={property.type} />
      {property.name}
    </div>
  );
};

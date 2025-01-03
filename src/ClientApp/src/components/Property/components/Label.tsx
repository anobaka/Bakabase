import { useTranslation } from 'react-i18next';
import type { IProperty } from '@/components/Property/models';
import { PropertyTypeIconMap } from '@/components/Property/models';
import { Chip, Icon } from '@/components/bakaui';
import { PropertyPool } from '@/sdk/constants';

interface IProps {
  property: IProperty;
  showPool?: boolean;
}

export default ({ property, showPool }: IProps) => {
const { t } = useTranslation();
  const icon = PropertyTypeIconMap[property.type];
  return (
    <>
      {showPool && (
        <Chip
          size={'sm'}
          radius={'sm'}
          variant={'flat'}
        >
          {t(`PropertyPool.${PropertyPool[property.pool]}`)}
        </Chip>
      )}
      <Icon
        type={icon}
        className={'text-base'}
      />
      <span>{property.name}</span>
    </>
  );
};

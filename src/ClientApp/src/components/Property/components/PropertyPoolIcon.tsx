import { useTranslation } from 'react-i18next';
import type { ChipProps } from '@/components/bakaui';
import { Chip, Tooltip } from '@/components/bakaui';
import { PropertyPool } from '@/sdk/constants';

type Props = {
  pool: PropertyPool;
};

export default ({ pool }: Props) => {
  const { t } = useTranslation();

  let color: ChipProps['color'] = 'default';
  switch (pool) {
    case PropertyPool.Internal:
      color = 'default';
      break;
    case PropertyPool.Reserved:
      color = 'secondary';
      break;
    case PropertyPool.Custom:
      color = 'primary';
      break;
    default:
      break;
  }

  return (
    <Tooltip
      color={'foreground'}
      content={t(`PropertyPool.${PropertyPool[pool]}`)}
    >
      <Chip
        size={'sm'}
        variant={'flat'}
        color={color}
      >
        {t(`PropertyPool.Abbreviation.${PropertyPool[pool]}`)}
      </Chip>
    </Tooltip>
  );
};

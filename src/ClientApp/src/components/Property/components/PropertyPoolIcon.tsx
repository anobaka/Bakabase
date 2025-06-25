import { useTranslation } from 'react-i18next';
import type { ChipProps } from '@/components/bakaui';
import { Chip, Tooltip } from '@/components/bakaui';
import { PropertyPool } from '@/sdk/constants';

type Props = {
  pool?: PropertyPool;
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
      color = 'warning';
      break;
  }

  const poolName = pool ? t(`PropertyPool.${PropertyPool[pool]}`) : t('Unknown');
  const poolAbbreviation = pool ? t(`PropertyPool.Abbreviation.${PropertyPool[pool]}`) : '?';

  return (
    <Tooltip
      color={'foreground'}
      content={poolName}
    >
      <Chip
        size={'sm'}
        variant={'flat'}
        color={color}
      >
        {poolAbbreviation}
      </Chip>
    </Tooltip>
  );
};

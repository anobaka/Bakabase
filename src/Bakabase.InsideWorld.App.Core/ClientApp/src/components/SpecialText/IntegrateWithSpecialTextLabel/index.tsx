import { useTranslation } from 'react-i18next';
import { history, useLocation } from 'ice';
import { Chip, Link, Tooltip } from '@/components/bakaui';
import { SpecialTextType } from '@/sdk/constants';

interface IProps {
  type: SpecialTextType;
}

export default ({ type }: IProps) => {
  const { t } = useTranslation();

  let tooltipContent = '';
  switch (type) {
    case SpecialTextType.DateTime:
      tooltipContent = t('Data will be attempted to be converted into date&time data according to relevant conversions.');
      break;
    case SpecialTextType.Useless:
    case SpecialTextType.Language:
    case SpecialTextType.Wrapper:
    case SpecialTextType.Standardization:
    case SpecialTextType.Volume:
    case SpecialTextType.Trim:
      tooltipContent = t('Data will be processed with special texts.');
      break;
    default:
      break;
  }

  return (
    <Tooltip
      content={(
        <span>
          {tooltipContent}
          <Link
            className={'active:no-underline'}
            // href={'#'}
            size={'sm'}
            onClick={(e) => {
              e.preventDefault();
              e.stopPropagation();

              history?.push('/');
            }}
          >{t('Click to check')}</Link>
        </span>
      )}
    >
      <Chip
        size={'sm'}
        variant={'flat'}
        radius={'sm'}
      >
        {t('Integrate with special text')}
      </Chip>
    </Tooltip>
  );
};

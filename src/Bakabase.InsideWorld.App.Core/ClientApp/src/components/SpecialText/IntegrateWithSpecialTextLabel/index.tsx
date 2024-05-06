import { useTranslation } from 'react-i18next';
import { history, useLocation } from 'ice';
import { Chip, Link, Modal, Tooltip } from '@/components/bakaui';
import { SpecialTextType } from '@/sdk/constants';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

interface IProps {
  type: SpecialTextType;
}

export default ({ type }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

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
        <div className={'flex items-center gap-1'}>
          {tooltipContent}
          <Link
            className={'active:no-underline cursor-pointer'}
            // href={'#'}
            size={'sm'}
            onClick={(e) => {
              e.preventDefault();
              e.stopPropagation();

              createPortal(Modal, {
                title: t('We are leaving current page'),
                children: t('Are you sure?'),
                defaultVisible: true,
                onOk: () => {
                  history?.push('/text');
                },
                },
              );
            }}
          >{t('Click to check special texts')}</Link>
        </div>
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

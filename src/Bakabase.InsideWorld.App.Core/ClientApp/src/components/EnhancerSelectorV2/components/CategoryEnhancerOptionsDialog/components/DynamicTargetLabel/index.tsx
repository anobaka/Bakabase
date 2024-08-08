import { ApartmentOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { Chip, Tooltip } from '@/components/bakaui';

export default () => {
  const { t } = useTranslation();
  return (
    <Tooltip content={t('This is not a fixed enhancement target, which will be replaced with other content when data is collected')}>
      <ApartmentOutlined className={'text-sm'} />
      {/* <Chip */}
      {/*   size={'sm'} */}
      {/*   radius={'sm'} */}
      {/*   className={'flex items-center gap-1'} */}
      {/* > */}
      {/*   {t('Dynamic target')} */}
      {/* </Chip> */}
    </Tooltip>
  );
};

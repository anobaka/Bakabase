import { useTranslation } from 'react-i18next';
import { useCallback } from 'react';
import { Chip } from '@/components/bakaui';

type AtomValue = string | number | boolean | null | undefined;

interface Props {
  value: AtomValue | AtomValue[] | null | undefined;
  onClick?: () => any;
}

export default ({ value, onClick }: Props) => {
  const { t } = useTranslation();

  const renderValue = useCallback((v: Props['value']) => {
    const arr = v as AtomValue[];
    if (arr) {
      const validArr = arr.filter((v) => v !== null && v !== undefined);
      if (validArr.length > 0) {
        return validArr.join(', ');
      }
    } else {
      if (v !== null && v !== undefined) {
        return v;
      }
    }
    return t('Not set');
  }, []);

  return (
    <Chip
      onClick={onClick}
      color={'primary'}
      size={'sm'}
      variant={'light'}
      className={'cursor-pointer'}
    >
      {renderValue(value)}
    </Chip>
  );
};

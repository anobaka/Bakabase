import i18n from 'i18next';
import React from 'react';
import { useTranslation } from 'react-i18next';
import CustomIcon from '@/components/CustomIcon';


export default (props) => {
  const { style = {}, className, ...otherProps } = props || {};
  const { t } = useTranslation();
  return (
    <CustomIcon
      style={{ cursor: 'all-scroll', ...style }}
      size={'small'}
      type={'menu'}
      title={t('Drag to sort')}
      className={`drag-handle ${className || ''} cursor-pointer`}
      {...(otherProps || {})}
    />
  );
};

import i18n from 'i18next';
import React from 'react';
import CustomIcon from '@/components/CustomIcon';

export default ((props) => (
  <CustomIcon
    {...(props || {})}
    style={{ cursor: 'all-scroll' }}
    size={'small'}
    type={'menu'}
    title={i18n.t('Drag to sort')}
  />
));

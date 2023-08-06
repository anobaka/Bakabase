import React from 'react';
import type { IconProps } from '@alifd/next/types/icon';
import CustomIcon from '@/components/CustomIcon';

import './index.scss';

interface IProps extends IconProps {
  colorType: 'normal' | 'danger';
}

export default ({ colorType, ...otherProps }: IProps) => {
  return (
    <CustomIcon
      type={'delete'}
      className={`clickable-icon ${colorType}`}
      {...otherProps}
    />
  );
};

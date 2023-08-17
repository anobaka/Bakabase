import React from 'react';
import type { IconProps } from '@alifd/next/types/icon';
import { Icon } from '@alifd/next';
import CustomIcon from '@/components/CustomIcon';

import './index.scss';

interface IProps extends IconProps {
  colorType: 'normal' | 'danger';
  useInBuildIcon?: boolean;
}

export default ({ colorType, useInBuildIcon = false, className, ...otherProps }: IProps) => {
  if (useInBuildIcon) {
    return (
      <span className={`clickable-icon ${colorType} ${className}`}>
        <Icon
          {...otherProps}
        />
      </span>
    );
  }
  return (
    <CustomIcon
      className={`clickable-icon ${colorType} ${className}`}
      {...otherProps}
    />
  );
};

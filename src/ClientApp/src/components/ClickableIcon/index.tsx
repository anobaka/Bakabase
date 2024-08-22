import React, { forwardRef } from 'react';
import { Icon } from '@alifd/next';
import styles from './index.module.scss';
import CustomIcon from '@/components/CustomIcon';


interface IProps extends Omit<React.HTMLProps<HTMLSpanElement>, 'ref' | 'size'>{
  colorType: 'normal' | 'danger';
  useInBuildIcon?: boolean;
  type: string;
  size?: 'small' | 'medium' | 'large';
}

const ClickableIcon = forwardRef<any, IProps>(({ size, colorType, useInBuildIcon = false, className, ...otherProps }, ref) => {
  if (useInBuildIcon) {
    return (
      // todo: change to antd built-in icon
      <span className={`${styles.clickableIcon} ${colorType} ${className}`}>
        <Icon
          ref={ref}
          size={size}
          {...otherProps}
        />
      </span>
    );
  }
  return (
    <CustomIcon
      ref={ref}
      className={`${styles.clickableIcon} ${styles[colorType]} ${className}`}
      {...otherProps}
    />
  );
});

export default ClickableIcon;

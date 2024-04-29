import React, { forwardRef } from 'react';
import { createFromIconfontCN } from '@ant-design/icons';
import '@/assets/iconfont/iconfont.js';
import type { IconFontProps } from '@ant-design/icons/lib/components/IconFont';

const DefaultComponent = createFromIconfontCN({
  scriptUrl: '',
});

export interface CustomIconProps extends IconFontProps {
  type: string;
}

const CustomIconV2 = forwardRef<HTMLSpanElement, CustomIconProps>(({ type, ...otherProps }: CustomIconProps, ref) => {
  return (
    <DefaultComponent type={`icon-${type}`} {...otherProps} ref={ref} />
  );
});

export default CustomIconV2;

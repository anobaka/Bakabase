import React, { forwardRef } from 'react';
import { createFromIconfontCN } from '@ant-design/icons';
import '@/assets/iconfont/iconfont.js';
import type { IconFontProps } from '@ant-design/icons/lib/components/IconFont';

const DefaultComponent = createFromIconfontCN({
  scriptUrl: '',
});

interface IProps extends IconFontProps {
  type: string;
}

const CustomIconV2 = forwardRef<HTMLSpanElement, IProps>(({ type, ...otherProps }: IProps, ref) => {
  return (
    <DefaultComponent type={`icon-${type}`} {...otherProps} ref={ref} />
  );
});

export default CustomIconV2;

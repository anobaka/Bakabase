import { forwardRef, lazy, Suspense, useEffect, useRef, useState } from 'react';
import { WarningOutlined } from '@ant-design/icons';
import * as React from 'react';

interface IProps extends React.ComponentPropsWithRef<any>{
  type: string;
}


const Icon = forwardRef(({ type, ...otherProps }: IProps, ref) => {
  const iconRef = useRef(lazy(() => import(`@ant-design/icons/es/icons/${type}.js`).catch(err => import('@ant-design/icons/es/icons/WarningOutlined.js'))));

  return (
    <Suspense fallback={(<WarningOutlined />)}>
      <iconRef.current ref={ref} {...otherProps} />
    </Suspense>
  );
});

export default Icon;

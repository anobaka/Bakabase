"use client";

import { forwardRef, lazy, Suspense, useRef } from "react";
import { WarningOutlined } from "@ant-design/icons";
import React from "react";

export interface IconProps extends React.ComponentPropsWithRef<any> {
  type: string;
}

const Icon = forwardRef(({ type, ...otherProps }: IconProps, ref) => {
  const iconRef = useRef(
    lazy(() =>
      import(/* @vite-ignore */ `@ant-design/icons/es/icons/${type}.js`).catch(
        (err) =>
          import(
            /* @vite-ignore */ "@ant-design/icons/es/icons/WarningOutlined.js"
          ),
      ),
    ),
  );

  return (
    <Suspense fallback={<WarningOutlined />}>
      <iconRef.current ref={ref} {...otherProps} />
    </Suspense>
  );
});

export default Icon;

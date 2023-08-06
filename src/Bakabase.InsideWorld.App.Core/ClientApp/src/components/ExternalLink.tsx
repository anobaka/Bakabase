import { Button } from '@alifd/next';
import React from 'react';
import { OpenUrlInDefaultBrowser } from '@/sdk/apis';


export default ({ to, children, ...otherProps } = {}) => {
  return (
    <a
      href={to}
      {...otherProps}
      target={'_blank'}
      onClick={(e) => {
        e.preventDefault();
        OpenUrlInDefaultBrowser({
          url: to,
        }).invoke();
      }}
      rel="noreferrer"
    >{children}
    </a>
  );
};

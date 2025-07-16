'use client';

import React, { useEffect } from 'react';
import '@/styles/globals.css';
import { usePathname } from 'next/navigation';
import BasicLayout from '@/layouts/BasicLayout';
import BlankLayout from '@/layouts/BlankLayout';
import { buildLogger } from '@/components/utils';

const log = buildLogger('Layout');

export default function RootLayout({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();

  useEffect(() => {
    log('Initializing...');
  }, []);

  return (
    <html>
      <head>
        <meta charSet="utf-8" />
        <meta httpEquiv="x-ua-compatible" content="ie=edge,chrome=1" />
        <meta name="viewport" content="width=device-width" />
        <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no" />
      </head>
      <body>
        {pathname === '/welcome' ? (
          <BlankLayout>{children}</BlankLayout>
        ) : (
          <BasicLayout>{children}</BasicLayout>
        )}
      </body>
    </html>
  );
}

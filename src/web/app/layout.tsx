'use client';

import React, { useEffect } from 'react';
import { usePathname } from 'next/navigation';
import BasicLayout from '@/layouts/BasicLayout';
import BlankLayout from '@/layouts/BlankLayout';
import BakabaseContextProvider from '@/components/ContextProvider/BakabaseContextProvider';
import { buildLogger } from '@/components/utils';

const log = buildLogger('Layout');

const Layout = () => {
  const pathname = usePathname();

  useEffect(() => {
    log('Initializing...');
  }, []);

  if (pathname === '/welcome') {
    return <BlankLayout />;
  } else {
    return <BasicLayout />;
  }
};

export default () => {
  useEffect(() => {
    log('Initializing App...');
  }, []);

  return (
    <BakabaseContextProvider>
      <Layout />
    </BakabaseContextProvider>
  );
};

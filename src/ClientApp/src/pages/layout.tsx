import React, { useContext, useEffect } from 'react';
import { useLocation } from 'ice';
import BasicLayout from '@/layouts/BasicLayout';
import BlankLayout from '@/layouts/BlankLayout';
import BakabaseContextProvider, { BakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { buildLogger } from '@/components/utils';

const log = buildLogger('Layout');

const Layout = () => {
  const location = useLocation();

  useEffect(() => {
    log('Initializing...');
  }, []);

  if (location.pathname === '/welcome') {
    return (
      <BlankLayout />
    );
  } else {
    return (
      <BasicLayout />
    );
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

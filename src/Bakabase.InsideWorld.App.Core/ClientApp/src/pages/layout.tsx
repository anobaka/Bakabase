import React, { useContext } from 'react';
import { useLocation } from 'ice';
import BasicLayout from '@/layouts/BasicLayout';
import BlankLayout from '@/layouts/BlankLayout';
import BakabaseContextProvider, { BakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

const Layout = () => {
  const location = useLocation();

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
  const bbContext = useContext(BakabaseContext);

  return (
    <BakabaseContextProvider>
      <div className={`${bbContext.isDarkMode ? 'dark' : 'light'}`}>
        <Layout />
      </div>
    </BakabaseContextProvider>
  );
};

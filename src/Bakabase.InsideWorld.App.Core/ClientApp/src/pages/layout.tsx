import React, { useContext, useEffect } from 'react';
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
  const location = useLocation();
  const bbContext = useContext(BakabaseContext);

  useEffect(() => {
    console.log('Basic layout initialized');
  }, []);

  console.log('Basic layout rendering');
  console.trace();


  return (
    <BakabaseContextProvider key={location.key}>
      <div className={`${bbContext.isDarkMode ? 'dark' : 'light'}`}>
        <Layout />
      </div>
    </BakabaseContextProvider>
  );
};

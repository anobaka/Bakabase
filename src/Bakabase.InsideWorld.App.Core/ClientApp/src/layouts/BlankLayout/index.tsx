import React, { useEffect, useState } from 'react';
import { Outlet } from "ice";
// import './index.scss';

export default function BlankLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className={'inside-world'} style={{height: '100%'}}>
      <Outlet/>
    </div>
  );
}

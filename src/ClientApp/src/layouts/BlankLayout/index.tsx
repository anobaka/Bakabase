import React from 'react';
import { Outlet } from 'ice';
import styles from './index.module.scss';
export default function BlankLayout() {
  return (
    <div className={styles.insideWorld}>
      <Outlet />
    </div>
  );
}

import React from "react";
// import styles from './index.module.scss'; // 如有样式可后续适配

export default function WelcomeLayoutPage({
  children,
}: {
  children: React.ReactNode;
}) {
  return <div /* className={styles.insideWorld} */>{children}</div>;
}

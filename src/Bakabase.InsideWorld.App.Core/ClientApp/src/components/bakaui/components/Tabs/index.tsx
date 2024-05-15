import type { TabsProps as NextUITabsProps } from '@nextui-org/react';
import { Tabs as NextUiTabs, Tab as NextUiTab } from '@nextui-org/react';
import { forwardRef } from 'react';

interface TabsProps extends NextUITabsProps {
  children?: React.ReactNode;
}

const Tabs = forwardRef<any, TabsProps>(({ children, ...props }: TabsProps, ref) => {
  // console.log(66666666, props.children);
  return (
    <NextUiTabs ref={ref} {...props}>
      {children}
    </NextUiTabs>
  );
});

const Tab = NextUiTab;


export { Tabs, Tab };

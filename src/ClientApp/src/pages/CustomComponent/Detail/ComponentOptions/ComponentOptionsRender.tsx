import { useEffect, useState } from 'react';
import React from 'react';
import type { BRjsfProps } from '@/components/BRjsf';

const ComponentOptionsRender = React.forwardRef((props: BRjsfProps, ref) => {
  const [Component, setComponent] = useState<any>();
  const init = async () => {
    const a = await import(`./${props.schema.title}Rjsf`);
    setComponent(a.default);
  };

  useEffect(() => {
    init();
    console.log('[ComponentOptionsRender]Initialized', props);
  }, []);

  return Component && (
    <Component {...props} ref={ref} />
  );
});

export default ComponentOptionsRender;

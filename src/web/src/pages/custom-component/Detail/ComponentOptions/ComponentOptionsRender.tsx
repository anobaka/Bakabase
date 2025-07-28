"use client";

import type { BRjsfProps } from "@/components/BRjsf";

import { useEffect, useState } from "react";
import React from "react";

const ComponentOptionsRenderPage = React.forwardRef((props: BRjsfProps, ref) => {
  const [Component, setComponent] = useState<any>();
  const init = async () => {
    const a = await import(/* @vite-ignore */ `./${props.schema.title}Rjsf`);

    setComponent(a.default);
  };

  useEffect(() => {
    init();
    console.log("[ComponentOptionsRender]Initialized", props);
  }, []);

  return Component && <Component {...props} ref={ref} />;
});

export default ComponentOptionsRenderPage;

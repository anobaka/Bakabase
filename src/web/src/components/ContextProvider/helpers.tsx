"use client";

import type { ComponentType } from "react";
import type { DestroyableProps } from "@/components/bakaui/types";
import ReactDOM from "react-dom/client";

import { uuidv4 } from "@/components/utils";
import BakabaseContextProvider from "@/components/ContextProvider/BakabaseContextProvider";
import { HashRouter } from "react-router-dom";

export function createPortal<P extends DestroyableProps>(
  C: ComponentType<P>,
  props: P,
): { destroy: () => void; key: string } {
  const key = uuidv4();
  const node = document.createElement("div");

  document.body.appendChild(node);

  const root = ReactDOM.createRoot(node);

  console.log("Mounting", key);

  const unmount = () => {
    console.log("Unmounting", key);
    // Warning: Attempted to synchronously unmount a root while React was already rendering.
    // React cannot finish unmounting the root until the current render has completed, which may lead to a race condition.
    setTimeout(() => {
      root.unmount();
      node.remove();
    }, 1);
  };

  root.render(
    <HashRouter>
      <BakabaseContextProvider>
        <C
          {...props}
          onDestroyed={() => {
            if (props.onDestroyed) {
              props.onDestroyed();
            }
            unmount();
          }}
        />
      </BakabaseContextProvider>
    </HashRouter>
  );

  return {
    key,
    destroy: unmount,
  };
}

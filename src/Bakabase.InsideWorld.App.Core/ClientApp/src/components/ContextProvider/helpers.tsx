import type { ComponentType } from 'react';
import React from 'react';
import ReactDOM from 'react-dom/client';
import { uuidv4 } from '@/components/utils';
import type { DestroyableProps } from '@/components/bakaui/types';
import store from '@/store';
import BakabaseContextProvider from '@/components/ContextProvider/BakabaseContextProvider';

export function createPortal<P extends DestroyableProps>(C: ComponentType<P>, props: P): {destroy: () => void; key: string} {
  const key = uuidv4();
  const node = document.createElement('div');
  document.body.appendChild(node);

  const root = ReactDOM.createRoot(node);

  // console.log(19282, node, props, Component);

  const unmount = () => {
    console.log('Unmounting', key);
    // console.trace(19282);
    // Warning: Attempted to synchronously unmount a root while React was already rendering.
    // React cannot finish unmounting the root until the current render has completed, which may lead to a race condition.
    setTimeout(() => {
      root.unmount();
      node.remove();
    }, 1);
  };

  console.log('Mounting', key);

  root.render(
    <store.Provider>
      <BakabaseContextProvider >
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
    </store.Provider>,
);

  return {
    key,
    destroy: unmount,
  };
}

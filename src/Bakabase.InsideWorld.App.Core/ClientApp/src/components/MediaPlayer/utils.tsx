import React from 'react';
import ReactDOM from 'react-dom/client';
import { uuidv4 } from '@/components/utils';
import MediaPlayer from '@/components/MediaPlayer/index';
import store from '@/store';

const createMediaPlayer = (props) => {
  const { key = `message-${uuidv4()}` } = props;
  const node = document.createElement('div');
  document.body.appendChild(node);

  const root = ReactDOM.createRoot(node);

  const unmount = () => {
    console.log(key, 19282);
    root.unmount();
    node.parentElement?.removeChild(node);
  };

  root.render(
    <React.StrictMode>
      <store.Provider>
        <MediaPlayer
          {...props}
          afterClose={unmount}
        />
      </store.Provider>
    </React.StrictMode>,
  );

  return {
    key,
    close: unmount,
  };
};

export {
  createMediaPlayer,
};

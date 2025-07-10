// http://localhost:5000/file/play?fullname=Z%3A%5CAnime%5CAdded%20recently%5CArcane%20S01%5CS01E01%20-%20Welcome%20to%20the%20Playground.mkv

import ReactPlayer from 'react-player';
import serverConfig from '@/serverConfig';

export default () => {
  return (
    <ReactPlayer
      width={800}
      height={600}
      src={'http://localhost:5000/file/play?fullname=Z%3A%5CAnime%5CAdded%20recently%5CArcane%20S01%5CS01E01%20-%20Welcome%20to%20the%20Playground.mkv'}
      onDuration={e => {
        // console.log('duration', e);
      }}
      autoPlay
      controls
      config={{
        file: {
          attributes: {
            preload: 'auto', // 或 'metadata' 或 'none'
          },
        },
      }}
      // config={{
      //   file: {
      //     attributes: {
      //       crossOrigin: 'anonymous',
      //     },
      //   },
      // }}
    />
  );
};

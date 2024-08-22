import type IwFsEntryChangeEvent from '@/core/models/FileExplorer/IwFsEntryChangeEvent';

export default {
  state: {
    events: [{ path: '123' }] as IwFsEntryChangeEvent[],
  },
  reducers: {
    addRange: (prev, current) => {
      // console.log(prev.events, current);
      prev.events.splice(prev.events.length, 0, ...current);
      console.log('receiving new events', current, prev.events);
      return prev;
    },
    clear: (prev, current) => {
      console.log('clearing events');
      return {
        events: [],
      };
    },
  },
};

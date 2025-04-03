import type { HubConnection } from '@microsoft/signalr';
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Message } from '@alifd/next';
import store from '@/store';
import { buildLogger, sleep, uuidv4 } from '@/components/utils';
import serverConfig from '@/serverConfig';
import fileMovingProgresses from '@/models/fileMovingProgresses';

const hubEndpoint = `${serverConfig.apiEndpoint}/hub/ui`;
export default class UIHubConnection {
  private _conn: HubConnection;

  log = buildLogger(`UIHubConnection:${uuidv4()}`);

  constructor() {
    // this.log('123');
    const conn = new HubConnectionBuilder()
      .withUrl(hubEndpoint)
      .configureLogging(LogLevel.Debug)
      .build();

    conn.onclose(async () => {
      await this._start();
    });

    // todo: auto mapping
    conn.on('GetData', (key, data) => {
      // this.log(projects);
      // this.log(store);
      this.log(key, data);
      switch (key) {
        case 'BackgroundTask':
          store.dispatch.backgroundTasks.setState(data);
          break;
        case 'DownloadTask':
          store.dispatch.downloadTasks.setState(data);
          break;
        case 'DependentComponentContext':
          store.dispatch.dependentComponentContexts.setState(data);
          break;
        case 'FileMovingProgress':
          store.dispatch.fileMovingProgresses.setState(data);
          break;
        case 'AppContext':
          store.dispatch.appContext.update(data);
          break;
        case 'BulkModificationInternals':
          store.dispatch.bulkModificationInternals.update(data);
          break;
        case 'BTask':
          store.dispatch.bTasks.setState(data);
          break;
      }
    });

    // todo: auto mapping
    conn.on('GetIncrementalData', (key, data) => {
      console.log(`Got incremental data ${key}`, data);
      switch (key) {
        case 'BackgroundTask':
          store.dispatch.backgroundTasks.update(data);
          break;
        case 'DownloadTask':
          store.dispatch.downloadTasks.update(data);
          break;
        case 'DependentComponentContext':
          store.dispatch.dependentComponentContexts.update(data);
          break;
        case 'FileMovingProgress':
          store.dispatch.fileMovingProgresses.update(data);
          break;
        case 'BTask':
          store.dispatch.bTasks.update(data);
          break;
      }
    });

    conn.on('GetResponse', (rsp) => {
      if (rsp.code == 0) {
        Message.success('Success');
      } else {
        Message.error(`[${rsp.code}]${rsp.message}`);
      }
    });

    conn.on('IwFsEntriesChange', events => {
      // this.log(events);
      store.dispatch.iwFsEntryChangeEvents.addRange(events);
    });

    conn.on('OptionsChanged', (name, options) => {
      if (name.toLowerCase() == 'uioptions') {
        name = 'uiOptions';
      }
      this.log('options changed', name, options);
      store.dispatch[name].update(options);
    });

    conn.on('GetAppUpdaterState', state => {
      store.dispatch.appUpdaterState.setState(state);
    });

    this._conn = conn;
    this._start().then((r) => {});
  }

  async send(methodName: string, ...args: any[]) {
    while (this._conn.state != HubConnectionState.Connected) {
      this.log('Waiting for connecting');
      await sleep(1000);
    }
    this.log('sending ', methodName);
    Message.loading('Requesting...');
    await this._conn.send(methodName, ...args);
  }

  private async _onConnected() {
    await this._conn.send('GetInitialData');
  }

  private async _start() {
    const self = this;
    try {
      // Message.loading({
      //   title: 'Connecting to main process',
      //   duration: 0,
      // });
      await self._conn.start();
      // Message.success('GuiHub Connected.');
      await self._onConnected();
    } catch (err) {
      this.log(err);
      this.log(self, self._start);
      setTimeout(() => self._start(), 5000);
    }
  }
}

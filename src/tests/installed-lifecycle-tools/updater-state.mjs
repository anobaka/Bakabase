// Read the product's existing SignalR event, without adding a testing endpoint.
import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr';

const [flag, value, ...extra] = process.argv.slice(2);
if (flag !== '--port' || !/^[1-9][0-9]{0,4}$/.test(value ?? '') || Number(value) > 65535 || extra.length) {
  throw new Error('Expected --port with one valid loopback port');
}
const connection = new HubConnectionBuilder()
  .withUrl(`http://127.0.0.1:${value}/hub/ui`, {
    skipNegotiation: true,
    transport: HttpTransportType.WebSockets,
  })
  .configureLogging(LogLevel.None)
  .build();

// GetInitialData also emits unrelated events. Only this documented event is read.
let acceptState;
const stateReceived = new Promise(resolve => { acceptState = resolve; });
connection.on('GetAppUpdaterState', acceptState);
const deadline = setTimeout(() => {
  process.stderr.write('Timed out reading native updater state\n');
  process.exit(1);
}, 15000);
try {
  await connection.start();
  await connection.invoke('GetInitialData');
  const state = await stateReceived;
  if (!state || !Number.isInteger(state.status) || state.status < 1 || state.status > 6) {
    throw new Error('Unexpected native updater state');
  }
  process.stdout.write(`${JSON.stringify(state)}\n`);
} finally {
  await connection.stop();
  clearTimeout(deadline);
}

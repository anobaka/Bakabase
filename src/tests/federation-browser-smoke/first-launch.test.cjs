const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { until } = require('./first-launch.cjs');

test('a partial options write is polled again and its exact completed state is returned', async t => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'bakabase-notice-poll-'));
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const file = path.join(directory, 'ui.json');
  fs.writeFileSync(file, '{"UI":');
  const completed = { UI: { Notices: { BaselinePending: false, ReadIds: ['multi-device'] } } };
  let reads = 0;
  let predicateCalls = 0;

  const result = await until(() => {
    reads++;
    const json = fs.readFileSync(file, 'utf8');
    // Complete the same file after the poll has captured its partial contents.
    if (reads === 1) fs.writeFileSync(file, JSON.stringify(completed));
    return JSON.parse(json).UI.Notices;
  }, state => {
    predicateCalls++;
    return state.BaselinePending === false;
  }, 'the notice baseline', 5000);

  assert.equal(reads, 2);
  assert.equal(predicateCalls, 1);
  assert.deepEqual(result, completed.UI.Notices);
});

test('persistent invalid JSON fails at the deadline with the last valid state and parse error', async t => {
  t.mock.timers.enable({ apis: ['Date', 'setTimeout'], now: 0 });
  let reads = 0;
  const previous = { BaselinePending: true, ReadIds: [] };
  const failed = assert.rejects(until(() => {
    reads++;
    return reads === 1 ? previous : JSON.parse('');
  }, state => state.BaselinePending === false, 'the notice baseline', 250), error => {
    assert.equal(error.code, 'ERR_ASSERTION');
    assert.match(error.message, /Timed out waiting for the notice baseline/);
    assert.ok(error.message.includes(JSON.stringify(previous)));
    assert.match(error.message, /last JSON parse error: SyntaxError:/);
    return true;
  });
  t.mock.timers.tick(200);
  await Promise.resolve();
  t.mock.timers.tick(200);
  await failed;
  assert.equal(reads, 2);
});

test('non-JSON read errors fail immediately without being retried', async () => {
  const failure = new Error('permission denied');
  failure.code = 'EACCES';
  let reads = 0;
  await assert.rejects(until(() => {
    reads++;
    throw failure;
  }, () => true, 'the notice baseline'), error => error === failure);
  assert.equal(reads, 1);
});

test('the condition still fails immediately even when it throws a SyntaxError', async () => {
  const failure = new SyntaxError('invalid condition');
  let reads = 0;
  await assert.rejects(until(() => {
    reads++;
    return { BaselinePending: true };
  }, () => { throw failure; }, 'the notice baseline'), error => error === failure);
  assert.equal(reads, 1);
});

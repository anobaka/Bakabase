// Drives production web assets and real Service listeners; no mocked API responses.
const { chromium } = require('playwright');
const serverSwitching = require('./switching.cjs');
const resourceProfiles = require('./resource-profiles.cjs');
const dataSync = require('./data-sync.cjs');
const firstLaunch = require('./first-launch.cjs');
const mappingSmoke = require('./mapping.cjs');
const { LAUNCH_ARGS, proveConfinement, assertNoAnalytics } = require('./network.cjs');
const fs = require('node:fs');
const path = require('node:path');
const config = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const { unified, source } = config.hosts;
const artifacts = file => path.join(config.results, file);

(async () => {
  // Before any page loads: what each Service's frontend is told about analytics.
  for (const host of [unified, source]) await assertNoAnalytics(host.base);
  const browser = await chromium.launch({ headless: true, args: LAUNCH_ARGS });
  try {
    // And that a tracker arriving some other way would be stopped and named.
    const refusedCanary = await proveConfinement(browser, unified.base);
    const report = { fixtureScope: 'Production Service HTTP pipelines; not an installed native package', networkConfinementProven: refusedCanary };
    report.firstLaunch = await firstLaunch({ browser, config });
    report.serverSwitching = await serverSwitching({ browser, config, artifacts });
    report.resourceProfiles = await resourceProfiles({ browser, config, artifacts });
    report.managementMappings = await mappingSmoke({ browser, config, artifacts });
    report.dataSync = await dataSync({ browser, config, artifacts });
    report.passed = true;
    fs.writeFileSync(artifacts('result.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify(report, null, 2));
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });

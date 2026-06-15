const { chromium } = require('playwright');
(async() => {
  const browser = await chromium.launch({ headless: true, channel: 'msedge' }).catch(async () => {
    return await chromium.launch({ headless: true });
  });
  const page = await browser.newPage();
  const logs = [];
  page.on('console', msg => logs.push(`console:${msg.type()}:${msg.text()}`));
  page.on('pageerror', err => logs.push(`pageerror:${err.message}`));
  await page.goto('http://localhost:5076/Rule12', { waitUntil: 'networkidle' });
  await page.waitForTimeout(2000);
  console.log(logs.join('\n') || 'no-errors');
  await browser.close();
})();

const puppeteer = require('puppeteer');
const orchard = require('./orchard.js');

let browser;
let page;
let basePath;

// e.g., npm test --debug
// In debug mode we show the editor, slow down operations, and increase the timeout for each test
let debug = process.env.npm_config_debug || false;
jest.setTimeout(debug ? 60000 : 30000);

beforeAll(async () => {

    basePath = orchard.run('../../src/OrchardCore.Mvc.Web', 'OrchardCore.Mvc.Web.dll');
    browser = await puppeteer.launch(debug ? { headless: false, slowMo: 100 } : {});
    page = await browser.newPage();
});

afterAll(async () => {
    if (browser) {
        await browser.close();
    }

    orchard.stop();
    orchard.cleanAppData('../../src/OrchardCore.Cms.Web');
    orchard.printLog();
});

describe('ASP.NET MVC', () => {

    it('should display "Hello World"', async () => {
        await page.goto(`${basePath}`);
        await expect(await page.content()).toMatch('Hello World');
    });
});

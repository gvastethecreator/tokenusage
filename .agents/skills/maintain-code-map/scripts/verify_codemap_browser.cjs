#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const { chromium } = require("playwright");


function parseArgs(argv) {
  const args = { files: [], screenshotDir: null };
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === "--screenshot-dir") {
      args.screenshotDir = path.resolve(argv[index + 1]);
      index += 1;
    } else {
      args.files.push(path.resolve(argv[index]));
    }
  }
  if (!args.files.length) args.files.push(path.resolve("docs/codemap/codemap.html"));
  return args;
}


async function verifyFile(browser, file, screenshotDir) {
  if (!fs.existsSync(file)) throw new Error(`file does not exist: ${file}`);
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  const errors = [];
  page.on("pageerror", (error) => errors.push(`pageerror: ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error") errors.push(`console: ${message.text()}`);
  });
  await page.goto(pathToFileURL(file).href, { waitUntil: "load" });
  await page.locator("g.node").first().waitFor();

  const model = await page.locator("#codemap-data").evaluate((element) => {
    const data = JSON.parse(element.textContent);
    return {
      nodes: data.nodes.length,
      edges: data.edges.length,
      flows: data.flows.length,
      firstId: data.nodes[0].id,
    };
  });
  const initial = {
    title: await page.title(),
    repo: await page.locator("#repo-name").textContent(),
    nodes: await page.locator("g.node").count(),
    edges: await page.locator("path.edge").count(),
    flowButtons: await page.locator(".flow-button").count(),
    overflow: await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth),
    svg: await page.locator("#map-svg").boundingBox(),
  };

  await page.locator("g.node").first().click();
  const selection = {
    focused: await page.locator("g.node.focus").count(),
    upstream: await page.locator("g.node.up").count(),
    downstream: await page.locator("g.node.down").count(),
    details: (await page.locator("#details").innerText()).trim().length,
  };
  await page.locator(".flow-button").first().click();
  const flow = {
    active: await page.locator(".flow-button.active").count(),
    nodes: await page.locator("g.node.flow").count(),
    edges: await page.locator("path.edge.flow").count(),
  };
  await page.locator(".flow-button.active").click();
  await page.locator("#search").fill(model.firstId);
  const searchMatches = await page.locator("g.node.match").count();
  await page.locator("#search").fill("");

  const transformBefore = await page.locator("#viewport").getAttribute("transform");
  await page.locator("#zoom-in").click();
  const transformAfter = await page.locator("#viewport").getAttribute("transform");
  const filter = page.locator("#type-filters input[type=checkbox]").first();
  const nodesBeforeFilter = await page.locator("g.node").count();
  await filter.uncheck();
  const nodesAfterFilter = await page.locator("g.node").count();
  await filter.check();
  await page.locator("#fit").click();

  const dragNode = page.locator("g.node").first();
  const dragBefore = await dragNode.boundingBox();
  if (dragBefore) {
    const x = dragBefore.x + dragBefore.width / 2;
    const y = dragBefore.y + dragBefore.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + 45, y + 28, { steps: 5 });
    await page.mouse.up();
  }
  const dragAfter = await dragNode.boundingBox();
  await page.locator("#search").focus();
  await page.keyboard.press("Tab");
  const tabTarget = await page.evaluate(() => ({
    tag: document.activeElement?.tagName,
    id: document.activeElement?.id || "",
    label: document.activeElement?.getAttribute("aria-label") || "",
  }));

  const slug = path.basename(path.dirname(path.dirname(path.dirname(file)))) || "codemap";
  if (screenshotDir) {
    fs.mkdirSync(screenshotDir, { recursive: true });
    await page.screenshot({ path: path.join(screenshotDir, `${slug}-desktop.png`), fullPage: true });
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await page.reload({ waitUntil: "load" });
  await page.locator("g.node").first().waitFor();
  const mobile = {
    overflow: await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth),
    svg: await page.locator("#map-svg").boundingBox(),
    search: await page.locator("#search").boundingBox(),
    details: await page.locator("#details").boundingBox(),
  };
  await page.locator("g.node").first().click();
  const mobileDetailsOpen = await page.locator("#details").isVisible();
  if (screenshotDir) {
    await page.screenshot({ path: path.join(screenshotDir, `${slug}-mobile-details.png`), fullPage: true });
  }
  await page.keyboard.press("Escape");
  const mobileDetailsClosed = !(await page.locator("#details").isVisible());
  if (screenshotDir) {
    await page.screenshot({ path: path.join(screenshotDir, `${slug}-mobile.png`), fullPage: true });
  }
  await page.close();

  const dragChanged = Boolean(
    dragBefore
      && dragAfter
      && (Math.abs(dragAfter.x - dragBefore.x) > 10 || Math.abs(dragAfter.y - dragBefore.y) > 10)
  );
  const checks = {
    counts: initial.nodes === model.nodes && initial.edges === model.edges && initial.flowButtons === model.flows,
    title: Boolean(initial.title && initial.repo),
    desktopFit: initial.overflow === 0 && initial.svg?.width > 300 && initial.svg?.height > 250,
    selection: selection.focused === 1 && selection.details > 0,
    flow: flow.active === 1 && flow.nodes >= 2 && flow.edges >= 1,
    search: searchMatches >= 1,
    zoom: transformBefore !== transformAfter,
    filter: nodesAfterFilter < nodesBeforeFilter,
    drag: dragChanged,
    keyboard: Boolean(tabTarget.tag),
    mobileFit: mobile.overflow === 0 && mobile.svg?.width > 300 && mobile.search?.width > 300,
    mobileDetails: mobileDetailsOpen && mobileDetailsClosed,
    errors: errors.length === 0,
  };
  return { file, model, initial, selection, flow, searchMatches, tabTarget, mobile, errors, checks, ok: Object.values(checks).every(Boolean) };
}


async function main() {
  const args = parseArgs(process.argv.slice(2));
  const browser = await chromium.launch({ headless: true });
  try {
    const results = [];
    for (const file of args.files) results.push(await verifyFile(browser, file, args.screenshotDir));
    const output = { ok: results.every((result) => result.ok), results };
    process.stdout.write(`${JSON.stringify(output, null, 2)}\n`);
    process.exitCode = output.ok ? 0 : 1;
  } finally {
    await browser.close();
  }
}


main().catch((error) => {
  process.stderr.write(`${error.stack || error.message}\n`);
  process.exitCode = 1;
});

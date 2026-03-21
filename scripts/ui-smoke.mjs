import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";

const baseUrl = process.env.BASE_URL ?? "http://127.0.0.1:4040";
const bootstrapServers = process.env.KAFKA_BOOTSTRAP_SERVERS ?? "localhost:9092";
const scriptPath = fileURLToPath(import.meta.url);
const repoRoot = path.resolve(path.dirname(scriptPath), "..");
const exportDir = process.env.UI_EXPORT_DIR ?? path.join(repoRoot, "verification-data", "smoke-exports-ui");
const backendExportDir = process.env.UI_BACKEND_EXPORT_DIR ?? exportDir;
const expectedExportDir = process.env.UI_EXPECTED_EXPORT_DIR ?? exportDir;
const timeoutMs = Number(process.env.UI_SMOKE_TIMEOUT_MS ?? 30000);

async function waitFor(check, description, timeout = timeoutMs, intervalMs = 1000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (await check()) {
      return;
    }

    await new Promise((resolve) => setTimeout(resolve, intervalMs));
  }

  throw new Error(`Timed out waiting for ${description}`);
}

async function countJsonFiles(directory) {
  try {
    const entries = await fs.readdir(directory, { withFileTypes: true });
    return entries.filter((entry) => entry.isFile() && entry.name.endsWith(".json")).length;
  } catch (error) {
    if (error && error.code === "ENOENT") {
      return 0;
    }

    throw error;
  }
}

async function selectAndVerify(locator, value, description) {
  await waitFor(
    async () => (await locator.locator(`option[value="${value}"]`).count()) > 0,
    `${description} option ${value} to exist`
  );
  await locator.evaluate((element, selectedValue) => {
    element.value = selectedValue;
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  }, value);
  await waitFor(
    async () => (await locator.inputValue()) === value,
    `${description} to select ${value}`
  );
}

async function openWorkspaceTab(page, tabName, markerText) {
  await page.locator(".tab-strip").getByRole("button", { name: tabName, exact: true }).click();
  await page.getByRole("heading", { name: markerText, exact: true }).waitFor({ state: "visible", timeout: timeoutMs });
}

await fs.rm(expectedExportDir, { recursive: true, force: true });
await fs.mkdir(expectedExportDir, { recursive: true });

const consoleErrors = [];
const pageErrors = [];
const requestFailures = [];

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext();
const page = await context.newPage();

page.on("console", (message) => {
  if (message.type() === "error") {
    consoleErrors.push(message.text());
  }
});

page.on("pageerror", (error) => {
  pageErrors.push(String(error));
});

page.on("requestfailed", (request) => {
  requestFailures.push(`${request.method()} ${request.url()} -> ${request.failure()?.errorText ?? "unknown"}`);
});

try {
  console.log("== Resetting Steak session");
  try {
    await fetch(`${baseUrl}/api/connection`, { method: "DELETE" });
  } catch {
    // Ignore cleanup failures and let the UI path handle the current state.
  }

  console.log("== Opening Steak UI");
  await page.goto(baseUrl, { waitUntil: "networkidle", timeout: timeoutMs });

  const brandSrc = await page.locator(".brand-mark").getAttribute("src");
  if (!brandSrc || !brandSrc.includes("steak-logo")) {
    throw new Error(`Unexpected brand image source: ${brandSrc ?? "<null>"}`);
  }

  const connectButton = page.getByRole("button", { name: "Connect" });
  const disconnectButton = page.getByRole("button", { name: "Disconnect" });
  await waitFor(
    async () =>
      ((await connectButton.count()) > 0 && (await connectButton.isVisible())) ||
      ((await disconnectButton.count()) > 0 && (await disconnectButton.isVisible())),
    "connection controls to render"
  );

  if ((await disconnectButton.count()) > 0 && (await disconnectButton.isVisible())) {
    await disconnectButton.click();
    await connectButton.waitFor({ state: "visible", timeout: timeoutMs });
  }

  console.log("== Verifying connection form");
  const advancedSummary = page.locator("summary").filter({ hasText: "Advanced settings" }).first();
  await advancedSummary.waitFor({ state: "visible", timeout: timeoutMs });
  await advancedSummary.click();
  await page.getByText("SSL CA PEM").waitFor({ timeout: timeoutMs });
  await advancedSummary.click();

  const bootstrapInput = page.locator('input[placeholder="broker-1:9092,broker-2:9092"]');
  await bootstrapInput.evaluate((element, value) => {
    element.value = value;
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  }, bootstrapServers);
  await waitFor(async () => await connectButton.isEnabled(), "connect button to become enabled");
  await connectButton.click();
  await page.getByRole("button", { name: "Disconnect" }).waitFor({ state: "visible", timeout: timeoutMs });

  console.log("== Exercising single publish");
  await openWorkspaceTab(page, "Publish", "Batch & single publish");
  await page.getByRole("button", { name: "Refresh Topics" }).click();
  const publishTopicSelect = page.locator('label:has-text("Topic") select').last();
  await selectAndVerify(publishTopicSelect, "orders", "single publish topic");
  await page.getByRole("button", { name: "Load Sample" }).click();
  await page.getByRole("button", { name: "Preview" }).click();
  await waitFor(
    async () => (await page.getByLabel("UTF-8 Preview").inputValue()).includes("queued"),
    "publish preview payload"
  );
  await page.getByRole("button", { name: "Pretty Format" }).click();
  await page.getByRole("button", { name: "Publish", exact: true }).nth(1).click();
  await page.getByText("Delivered to orders partition").waitFor({ timeout: timeoutMs });

  console.log("== Exercising view workspace");
  await openWorkspaceTab(page, "View", "Live message viewer");
  await page.getByRole("button", { name: "Refresh Topics" }).click();
  await page.getByLabel("Topic Filter").fill("ord");
  await page.locator(".topic-tile", { hasText: "orders" }).waitFor({ timeout: timeoutMs });
  await page.getByLabel("Topic Filter").fill("");
  const viewTopicSelect = page.locator('label:has-text("Topic") select').first();
  await selectAndVerify(viewTopicSelect, "orders", "view topic");
  await selectAndVerify(page.getByLabel("Offset Mode"), "Earliest", "view offset mode");
  await page.getByRole("button", { name: "Start Live View" }).click();
  await waitFor(
    async () => (await page.locator(".message-row").count()) > 0,
    "view workspace to render message rows",
    timeoutMs,
    1500
  );
  await page.getByLabel("Message Filter").fill("queued");
  await waitFor(
    async () => (await page.locator(".message-row").count()) > 0,
    "message filter to keep matching rows"
  );
  await page.locator(".message-row").first().click();
  const inspectorPreview = await page.getByLabel("UTF-8 Preview").nth(0).inputValue();
  if (!inspectorPreview.includes("queued")) {
    throw new Error("Message inspector did not show the expected payload preview.");
  }
  await page.getByRole("button", { name: "Stop" }).click();

  console.log("== Exercising consume workspace");
  await openWorkspaceTab(page, "Consume", "Batch export");
  await page.getByRole("button", { name: "Refresh Topics" }).click();
  const consumeTopicSelect = page.locator('label:has-text("Topic") select').first();
  await selectAndVerify(consumeTopicSelect, "orders", "consume topic");
  await page.getByLabel("Consumer Group").fill(`ui-smoke-${Date.now()}`);
  await selectAndVerify(page.getByLabel("Offset Mode"), "Earliest", "consume offset mode");
  await page.getByLabel("Max Messages (0 = unlimited)").fill("2");
  await page.getByLabel("Folder Path").fill(backendExportDir);
  await page.getByRole("button", { name: "Start Export" }).click();
  await waitFor(
    async () => (await countJsonFiles(expectedExportDir)) >= 2,
    "consume workspace to export files"
  );

  if (await page.getByRole("button", { name: "Stop" }).isEnabled()) {
    await page.getByRole("button", { name: "Stop" }).click();
  }

  console.log("== Exercising batch publish");
  await openWorkspaceTab(page, "Publish", "Batch & single publish");
  await page.getByRole("button", { name: "Refresh Topics" }).click();
  await page.getByLabel("Source Folder").fill(backendExportDir);
  await selectAndVerify(page.locator('label:has-text("Topic Override") select'), "payments", "batch topic override");
  await page.getByLabel("Max Messages (0 = unlimited)").fill("2");
  await page.getByRole("button", { name: "Start Batch Publish" }).click();
  await waitFor(
    async () => (await page.locator(".status-pill").first().textContent())?.includes("published") ?? false,
    "batch publish status to update"
  );

  if (await page.getByRole("button", { name: "Stop" }).isEnabled()) {
    await page.getByRole("button", { name: "Stop" }).click();
  }

  console.log("== Verifying batch target in view workspace");
  await openWorkspaceTab(page, "View", "Live message viewer");
  await page.getByRole("button", { name: "Refresh Topics" }).click();
  await page.getByLabel("Message Filter").fill("");
  await selectAndVerify(viewTopicSelect, "payments", "view topic");
  await selectAndVerify(page.getByLabel("Offset Mode"), "Earliest", "view offset mode");
  await page.getByRole("button", { name: "Start Live View" }).click();
  await waitFor(
    async () => (await page.locator(".message-row").count()) > 0,
    "batch target view to render message rows",
    timeoutMs,
    1500
  );
  await page.locator(".message-row").first().click();
  const paymentsPreview = await page.getByLabel("UTF-8 Preview").nth(0).inputValue();
  if (!paymentsPreview.includes("queued")) {
    throw new Error("Batch-published message preview did not contain the expected payload.");
  }

  if (await page.getByRole("button", { name: "Stop" }).isEnabled()) {
    await page.getByRole("button", { name: "Stop" }).click();
  }

  await page.getByRole("button", { name: "Disconnect" }).click();
  await page.getByRole("button", { name: "Connect" }).waitFor({ state: "visible", timeout: timeoutMs });

  if (consoleErrors.length > 0 || pageErrors.length > 0 || requestFailures.length > 0) {
    throw new Error(
      [
        ...consoleErrors.map((entry) => `console: ${entry}`),
        ...pageErrors.map((entry) => `pageerror: ${entry}`),
        ...requestFailures.map((entry) => `requestfailed: ${entry}`)
      ].join("\n")
    );
  }

  console.log(
    JSON.stringify(
      {
        baseUrl,
        bootstrapServers,
        backendExportDir,
        expectedExportDir,
        exportedFileCount: await countJsonFiles(expectedExportDir),
        brandSrc
      },
      null,
      2
    )
  );
} finally {
  await context.close();
  await browser.close();
}
